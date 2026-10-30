using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Control;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Samy;

public sealed partial class MainWindow : Window
{
    private const int MaxClipboardItems = 30;
    private const int CollapsedWidth = 520;
    private const int ExpandedWidth = 940;
    private const int ExpandedHeight = 760;
    private const int HwndTopmost = -1;
    private const int HwndNotTopmost = -2;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private readonly DispatcherQueueTimer _uiTimer;
    private readonly DispatcherQueueTimer _clipboardTimer;
    private readonly Process _currentProcess;
    private readonly string _dataDirectory;
    private readonly string _notePath;
    private readonly string _clipboardPath;
    private readonly string _filesPath;
    private readonly string _settingsPath;
    private readonly List<string> _clipboardItems = [];
    private readonly List<string> _fileItems = [];
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuSample = DateTime.UtcNow;
    private int _focusSecondsRemaining = 25 * 60;
    private bool _focusIsRunning;
    private bool _expanded = true;
    private bool _alwaysOnTop;
    private bool _clipboardEnabled;
    private bool _loadingSettings;
    private uint _lastClipboardSequence;
    private AppWindow? _appWindow;
    private GlobalSystemMediaTransportControlsSessionManager? _mediaManager;
    private GlobalSystemMediaTransportControlsSession? _mediaSession;

    public MainWindow()
    {
        InitializeComponent();
        Title = "SAMY";

        _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SAMY");
        Directory.CreateDirectory(_dataDirectory);
        _notePath = Path.Combine(_dataDirectory, "quick-note.txt");
        _clipboardPath = Path.Combine(_dataDirectory, "clipboard-history.json");
        _filesPath = Path.Combine(_dataDirectory, "file-shelf.json");
        _settingsPath = Path.Combine(_dataDirectory, "settings.json");

        ConfigureWindow();
        _currentProcess = Process.GetCurrentProcess();
        _lastCpuTime = _currentProcess.TotalProcessorTime;

        _uiTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _uiTimer.Interval = TimeSpan.FromSeconds(1);
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();

        _clipboardTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _clipboardTimer.Interval = TimeSpan.FromMilliseconds(850);
        _clipboardTimer.Tick += ClipboardTimer_Tick;

        Closed += MainWindow_Closed;
        UpdateClock();
        UpdateFocusDisplay();
        RefreshMetrics();
        CalendarDateText.Text = DateTime.Today.ToString("dddd, dd MMMM yyyy");
        CalendarControl.SelectedDates.Add(DateTimeOffset.Now);
        _ = InitializeAsync();
    }

    private void ConfigureWindow()
    {
        nint hwnd = WindowNative.GetWindowHandle(this);
        WindowId id = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(id);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        _appWindow.TitleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        _appWindow.TitleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        _appWindow.Resize(new Windows.Graphics.SizeInt32(ExpandedWidth, ExpandedHeight));
        PositionWindow(ExpandedWidth, ExpandedHeight);
    }

    private void PositionWindow(int width, int height)
    {
        nint hwnd = WindowNative.GetWindowHandle(this);
        int screenWidth = GetSystemMetrics(0);
        int x = Math.Max(0, (screenWidth - width) / 2);
        SetWindowPos(hwnd, _alwaysOnTop ? HwndTopmost : HwndNotTopmost, x, 12, width, height, SwpNoActivate | SwpShowWindow);
    }

    private async Task InitializeAsync()
    {
        await LoadNoteAsync();
        LoadJsonList(_clipboardPath, _clipboardItems);
        LoadJsonList(_filesPath, _fileItems);
        RefreshClipboardList();
        RefreshFilesList();
        LoadSettings();
        await RefreshMediaAsync();
    }

    private void UiTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        UpdateClock();
        RefreshMetrics();
        if (!_focusIsRunning) return;
        _focusSecondsRemaining = Math.Max(0, _focusSecondsRemaining - 1);
        UpdateFocusDisplay();
        if (_focusSecondsRemaining == 0)
        {
            _focusIsRunning = false;
            FocusButton.Content = "Start";
            NoteStatusText.Text = "Focus session complete · time for a break";
        }
    }

    private void UpdateClock()
    {
        DateTime now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm");
        DateText.Text = now.ToString("ddd, dd MMM");
    }

    private void RefreshMetrics()
    {
        try
        {
            _currentProcess.Refresh();
            TimeSpan cpuNow = _currentProcess.TotalProcessorTime;
            DateTime sampleNow = DateTime.UtcNow;
            double elapsedMs = (sampleNow - _lastCpuSample).TotalMilliseconds;
            double cpuDeltaMs = (cpuNow - _lastCpuTime).TotalMilliseconds;
            if (elapsedMs > 0)
            {
                double cpuPercent = cpuDeltaMs / (elapsedMs * Environment.ProcessorCount) * 100d;
                CpuText.Text = $"SAMY CPU · {Math.Clamp(cpuPercent, 0d, 100d):0}%";
            }
            _lastCpuTime = cpuNow;
            _lastCpuSample = sampleNow;
            MemoryText.Text = $"SAMY memory · {_currentProcess.WorkingSet64 / 1024d / 1024d:0} MB";
        }
        catch (InvalidOperationException)
        {
            CpuText.Text = "CPU unavailable";
            MemoryText.Text = "Memory unavailable";
        }
    }

    private void FocusButton_Click(object sender, RoutedEventArgs e)
    {
        if (_focusSecondsRemaining == 0) _focusSecondsRemaining = 25 * 60;
        _focusIsRunning = !_focusIsRunning;
        FocusButton.Content = _focusIsRunning ? "Pause" : "Resume";
    }

    private void ResetFocusButton_Click(object sender, RoutedEventArgs e)
    {
        _focusIsRunning = false;
        _focusSecondsRemaining = 25 * 60;
        FocusButton.Content = "Start";
        UpdateFocusDisplay();
    }

    private void UpdateFocusDisplay()
    {
        TimeSpan remaining = TimeSpan.FromSeconds(_focusSecondsRemaining);
        FocusText.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
    }

    private async Task LoadNoteAsync()
    {
        try
        {
            if (File.Exists(_notePath)) NoteBox.Text = await File.ReadAllTextAsync(_notePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NoteStatusText.Text = "Couldn't load the local note";
        }
    }

    private async void SaveNoteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await File.WriteAllTextAsync(_notePath, NoteBox.Text);
            NoteStatusText.Text = $"Saved locally · {DateTime.Now:HH:mm}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NoteStatusText.Text = "Save failed · check local storage";
        }
    }

    private void ShowPage(UIElement page)
    {
        HomePage.Visibility = Visibility.Collapsed;
        ClipboardPage.Visibility = Visibility.Collapsed;
        FilesPage.Visibility = Visibility.Collapsed;
        MediaPage.Visibility = Visibility.Collapsed;
        CalendarPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
    }

    private void HomeNav_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage);
    private void ClipboardNav_Click(object sender, RoutedEventArgs e) => ShowPage(ClipboardPage);
    private void FilesNav_Click(object sender, RoutedEventArgs e) => ShowPage(FilesPage);
    private void MediaNav_Click(object sender, RoutedEventArgs e) => ShowPage(MediaPage);
    private void CalendarNav_Click(object sender, RoutedEventArgs e) => ShowPage(CalendarPage);
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage);

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _expanded = !_expanded;
        ExpandedContent.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        PositionWindow(_expanded ? ExpandedWidth : CollapsedWidth, _expanded ? ExpandedHeight : 66);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _alwaysOnTop = !_alwaysOnTop;
        PinButton.Content = _alwaysOnTop ? "PINNED" : "PIN";
        PositionWindow(_expanded ? ExpandedWidth : CollapsedWidth, _expanded ? ExpandedHeight : 66);
        SaveSettings();
    }

    private void AlwaysOnTopToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _alwaysOnTop = AlwaysOnTopToggle.IsOn;
        PinButton.Content = _alwaysOnTop ? "PINNED" : "PIN";
        PositionWindow(_expanded ? ExpandedWidth : CollapsedWidth, _expanded ? ExpandedHeight : 66);
        SaveSettings();
    }

    private void StartWithWindowsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        try
        {
            using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (StartWithWindowsToggle.IsOn)
                key.SetValue("SAMY", $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue("SAMY", false);
            SaveSettings();
        }
        catch (UnauthorizedAccessException)
        {
            NoteStatusText.Text = "Windows startup setting couldn't be changed";
        }
    }

    private void ClipboardToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _clipboardEnabled = ClipboardToggle.IsOn;
        _lastClipboardSequence = GetClipboardSequenceNumber();
        if (_clipboardEnabled) _clipboardTimer.Start(); else _clipboardTimer.Stop();
        ClipboardStatusText.Text = _clipboardEnabled ? "Capture on · text stays on this PC" : "Capture is off.";
        SaveSettings();
    }

    private void LoadSettings()
    {
        _loadingSettings = true;
        try
        {
            if (File.Exists(_settingsPath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(_settingsPath));
                JsonElement root = doc.RootElement;
                _alwaysOnTop = root.TryGetProperty("alwaysOnTop", out var pin) && pin.GetBoolean();
                _clipboardEnabled = root.TryGetProperty("clipboardEnabled", out var clip) && clip.GetBoolean();
            }
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            StartWithWindowsToggle.IsOn = key?.GetValue("SAMY") is not null;
            AlwaysOnTopToggle.IsOn = _alwaysOnTop;
            ClipboardToggle.IsOn = _clipboardEnabled;
            PinButton.Content = _alwaysOnTop ? "PINNED" : "PIN";
            if (_clipboardEnabled)
            {
                _lastClipboardSequence = GetClipboardSequenceNumber();
                _clipboardTimer.Start();
                ClipboardStatusText.Text = "Capture on · text stays on this PC";
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            ClipboardStatusText.Text = "Settings reset to safe defaults";
            _clipboardEnabled = false;
            ClipboardToggle.IsOn = false;
        }
        finally { _loadingSettings = false; }
        PositionWindow(_expanded ? ExpandedWidth : CollapsedWidth, _expanded ? ExpandedHeight : 66);
    }

    private void SaveSettings()
    {
        try
        {
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new { alwaysOnTop = _alwaysOnTop, clipboardEnabled = _clipboardEnabled }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async void ClipboardTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (!_clipboardEnabled) return;
        uint sequence = GetClipboardSequenceNumber();
        if (sequence == _lastClipboardSequence) return;
        _lastClipboardSequence = sequence;
        try
        {
            DataPackageView data = Clipboard.GetContent();
            if (!data.Contains(StandardDataFormats.Text)) return;
            string text = (await data.GetTextAsync()).Trim();
            if (string.IsNullOrWhiteSpace(text) || text.Length > 4000 || _clipboardItems.FirstOrDefault() == text) return;
            _clipboardItems.Insert(0, text);
            if (_clipboardItems.Count > MaxClipboardItems) _clipboardItems.RemoveRange(MaxClipboardItems, _clipboardItems.Count - MaxClipboardItems);
            SaveJsonList(_clipboardPath, _clipboardItems);
            RefreshClipboardList();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException) { }
    }

    private void RefreshClipboardList()
    {
        ClipboardList.Items.Clear();
        foreach (string item in _clipboardItems)
            ClipboardList.Items.Add(item.Length > 110 ? item[..110] + "…" : item);
        if (ClipboardList.Items.Count == 0) ClipboardList.Items.Add("No saved text yet");
    }

    private async void ClipboardItem_Click(object sender, ItemClickEventArgs e)
    {
        int index = ClipboardList.Items.IndexOf(e.ClickedItem);
        if (index < 0 || index >= _clipboardItems.Count) return;
        var package = new DataPackage();
        package.SetText(_clipboardItems[index]);
        Clipboard.SetContent(package);
        ClipboardStatusText.Text = "Copied selected history item";
        await Task.CompletedTask;
    }

    private void CopyLatestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_clipboardItems.Count == 0) return;
        var package = new DataPackage();
        package.SetText(_clipboardItems[0]);
        Clipboard.SetContent(package);
        ClipboardStatusText.Text = "Copied latest item";
    }

    private void ClearClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        _clipboardItems.Clear();
        SaveJsonList(_clipboardPath, _clipboardItems);
        RefreshClipboardList();
        ClipboardStatusText.Text = "Clipboard history cleared";
    }

    private async void AddFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        IReadOnlyList<Windows.Storage.StorageFile> files = await picker.PickMultipleFilesAsync();
        foreach (var file in files)
            if (!_fileItems.Contains(file.Path, StringComparer.OrdinalIgnoreCase)) _fileItems.Add(file.Path);
        SaveJsonList(_filesPath, _fileItems);
        RefreshFilesList();
    }

    private void RefreshFilesList()
    {
        FilesList.Items.Clear();
        foreach (string path in _fileItems) FilesList.Items.Add(Path.GetFileName(path));
    }

    private void FileItem_Click(object sender, ItemClickEventArgs e)
    {
        int index = FilesList.Items.IndexOf(e.ClickedItem);
        if (index < 0 || index >= _fileItems.Count) return;
        try
        {
            if (File.Exists(_fileItems[index]))
                Process.Start(new ProcessStartInfo(_fileItems[index]) { UseShellExecute = true });
            else
                NoteStatusText.Text = "That file is no longer available";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            NoteStatusText.Text = "Windows couldn't open that file";
        }
    }

    private void RemoveFileButton_Click(object sender, RoutedEventArgs e)
    {
        int index = FilesList.SelectedIndex;
        if (index < 0 || index >= _fileItems.Count) return;
        _fileItems.RemoveAt(index);
        SaveJsonList(_filesPath, _fileItems);
        RefreshFilesList();
    }

    private async Task RefreshMediaAsync()
    {
        try
        {
            _mediaManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _mediaSession = _mediaManager.GetCurrentSession();
            if (_mediaSession is null)
            {
                MediaTitleText.Text = "No active media session";
                MediaArtistText.Text = "Start playing media in a supported Windows app.";
                return;
            }
            var info = await _mediaSession.TryGetMediaPropertiesAsync();
            MediaTitleText.Text = string.IsNullOrWhiteSpace(info.Title) ? "Untitled media" : info.Title;
            MediaArtistText.Text = string.IsNullOrWhiteSpace(info.Artist) ? "Windows media session" : info.Artist;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            MediaTitleText.Text = "Media controls unavailable";
            MediaArtistText.Text = "This app or session does not expose Windows media controls.";
        }
    }

    private async void RefreshMediaButton_Click(object sender, RoutedEventArgs e) => await RefreshMediaAsync();
    private async void PreviousMediaButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshMediaAsync();
        if (_mediaSession is not null) await _mediaSession.TrySkipPreviousAsync();
    }
    private async void PlayPauseMediaButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshMediaAsync();
        if (_mediaSession is not null) await _mediaSession.TryTogglePlayPauseAsync();
    }
    private async void NextMediaButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshMediaAsync();
        if (_mediaSession is not null) await _mediaSession.TrySkipNextAsync();
    }

    private async void OpenCalendarButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-calendar:"));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            NoteStatusText.Text = "No calendar app is registered on this PC";
        }
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_dataDirectory) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { NoteStatusText.Text = "Couldn't open the data folder"; }
    }

    private static void LoadJsonList(string path, List<string> target)
    {
        try
        {
            if (!File.Exists(path)) return;
            var values = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
            if (values is not null) target.AddRange(values);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }

    private static void SaveJsonList(string path, List<string> values)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(values)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _uiTimer.Stop();
        _clipboardTimer.Stop();
        _currentProcess.Dispose();
        SaveSettings();
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, int hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}