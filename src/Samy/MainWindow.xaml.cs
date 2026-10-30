using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI;
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
    private readonly string _remindersPath;
    private readonly string _linksPath;
    private readonly string _tasksPath;
    private readonly List<SamyLink> _savedLinks = [];
    private readonly List<SamyTask> _tasks = [];
    private int _toolSeconds = 25 * 60;
    private bool _toolTimerRunning;
    private bool _stopwatchMode;
    private int _stopwatchSeconds;
    private readonly List<SamyReminder> _reminders = [];
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
        _remindersPath = Path.Combine(_dataDirectory, "reminders.json");
        _linksPath = Path.Combine(_dataDirectory, "saved-links.json");
        _tasksPath = Path.Combine(_dataDirectory, "tasks.json");

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
        LoadReminders();
        LoadToolData();
        RefreshClipboardList();
        RefreshFilesList();
        LoadSettings();
        await RefreshMediaAsync();
    }

    private void UiTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        UpdateClock();
        RefreshMetrics();
        CheckReminders();
        UpdateWorldClocks();
        if (_toolTimerRunning)
        {
            if (_stopwatchMode) _stopwatchSeconds++;
            else _toolSeconds = Math.Max(0, _toolSeconds - 1);
            UpdateToolTimerDisplay();
            if (!_stopwatchMode && _toolSeconds == 0) { _toolTimerRunning = false; MessagePreviewText.Text = "Timer complete"; }
        }
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
        ToolsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
    }

    private void HomeNav_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage);
    private void ClipboardNav_Click(object sender, RoutedEventArgs e) => ShowPage(ClipboardPage);
    private void FilesNav_Click(object sender, RoutedEventArgs e) => ShowPage(FilesPage);
    private void MediaNav_Click(object sender, RoutedEventArgs e) => ShowPage(MediaPage);
    private void CalendarNav_Click(object sender, RoutedEventArgs e) => ShowPage(CalendarPage);
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage);
    private void ToolsNav_Click(object sender, RoutedEventArgs e) { ShowPage(ToolsPage); RefreshToolLists(); UpdateWorldClocks(); UpdateToolTimerDisplay(); }

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



    private void LoadToolData()
    {
        try
        {
            if (File.Exists(_linksPath)) _savedLinks.AddRange(JsonSerializer.Deserialize<List<SamyLink>>(File.ReadAllText(_linksPath)) ?? []);
            if (File.Exists(_tasksPath)) _tasks.AddRange(JsonSerializer.Deserialize<List<SamyTask>>(File.ReadAllText(_tasksPath)) ?? []);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        RefreshToolLists();
    }

    private void SaveToolData()
    {
        try
        {
            File.WriteAllText(_linksPath, JsonSerializer.Serialize(_savedLinks));
            File.WriteAllText(_tasksPath, JsonSerializer.Serialize(_tasks));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void RefreshToolLists()
    {
        SavedLinksList.Items.Clear();
        foreach (var link in _savedLinks) SavedLinksList.Items.Add(link.Title + "  ·  " + link.Url);
        TasksList.Items.Clear();
        foreach (var task in _tasks) TasksList.Items.Add((task.Done ? "✓  " : "○  ") + task.Text);
    }

    private void SaveLinkButton_Click(object sender, RoutedEventArgs e)
    {
        string title = LinkTitleBox.Text.Trim();
        string rawUrl = LinkUrlBox.Text.Trim();
        if (title.Length == 0 || !Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
        { MessagePreviewText.Text = "Enter a title and a valid http/https URL."; return; }
        _savedLinks.Add(new SamyLink { Title = title, Url = uri.ToString() });
        SaveToolData(); RefreshToolLists(); LinkTitleBox.Text = ""; LinkUrlBox.Text = "";
    }

    private void SavedLinkItem_Click(object sender, ItemClickEventArgs e)
    {
        int index = SavedLinksList.Items.IndexOf(e.ClickedItem);
        if (index < 0 || index >= _savedLinks.Count) return;
        try { Process.Start(new ProcessStartInfo(_savedLinks[index].Url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { MessagePreviewText.Text = "Windows couldn't open this link."; }
    }

    private void RemoveLinkButton_Click(object sender, RoutedEventArgs e)
    {
        int index = SavedLinksList.SelectedIndex;
        if (index < 0 || index >= _savedLinks.Count) return;
        _savedLinks.RemoveAt(index); SaveToolData(); RefreshToolLists();
    }

    private void AddTaskButton_Click(object sender, RoutedEventArgs e)
    {
        string value = TaskBox.Text.Trim();
        if (value.Length == 0) return;
        _tasks.Add(new SamyTask { Text = value }); TaskBox.Text = ""; SaveToolData(); RefreshToolLists();
    }

    private void CompleteTaskButton_Click(object sender, RoutedEventArgs e)
    {
        int index = TasksList.SelectedIndex;
        if (index < 0 || index >= _tasks.Count) return;
        _tasks[index].Done = !_tasks[index].Done; SaveToolData(); RefreshToolLists();
    }

    private void ToolTimerToggle_Click(object sender, RoutedEventArgs e) { _toolTimerRunning = !_toolTimerRunning; }
    private void ToolTimerReset_Click(object sender, RoutedEventArgs e) { _toolTimerRunning = false; _toolSeconds = 25 * 60; _stopwatchSeconds = 0; _stopwatchMode = false; UpdateToolTimerDisplay(); }
    private void ToolPomodoro_Click(object sender, RoutedEventArgs e) { _toolTimerRunning = false; _stopwatchMode = false; _toolSeconds = 25 * 60; UpdateToolTimerDisplay(); }
    private void ToolStopwatch_Click(object sender, RoutedEventArgs e) { _toolTimerRunning = false; _stopwatchMode = true; _stopwatchSeconds = 0; UpdateToolTimerDisplay(); }
    private void ToolCountdown_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CountdownMinutesBox.Text, out int minutes) || minutes < 1 || minutes > 1440)
        { MessagePreviewText.Text = "Enter a countdown from 1 to 1440 minutes."; return; }
        _toolTimerRunning = false; _stopwatchMode = false; _toolSeconds = minutes * 60; UpdateToolTimerDisplay();
    }

    private void UpdateToolTimerDisplay()
    {
        int seconds = _stopwatchMode ? _stopwatchSeconds : _toolSeconds;
        ToolTimerText.Text = $"{seconds / 3600:00}:{(seconds / 60) % 60:00}:{seconds % 60:00}";
    }

    private void ConvertUnitButton_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(UnitInputBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
        { UnitResultText.Text = "Enter a valid number."; return; }
        double result = UnitTypeBox.SelectedIndex switch
        {
            0 => value * 3.280839895,
            1 => value * 2.2046226218,
            2 => value * 9 / 5 + 32,
            3 => value / 1024,
            _ => double.NaN
        };
        string unit = UnitTypeBox.SelectedIndex switch { 0 => "ft", 1 => "lb", 2 => "°F", 3 => "GB", _ => "" };
        UnitResultText.Text = $"{result:0.####} {unit}";
    }

    private void UpdateWorldClocks()
    {
        try
        {
            var kolkata = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));
            var london = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time"));
            var ny = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
            WorldClocksText.Text = $"Kolkata  {kolkata:ddd HH:mm}\nLondon  {london:ddd HH:mm}\nNew York  {ny:ddd HH:mm}";
        }
        catch (TimeZoneNotFoundException) { WorldClocksText.Text = "World clock data unavailable."; }
    }

    private void ShowMessageButton_Click(object sender, RoutedEventArgs e)
    {
        string message = MessageBox.Text.Trim();
        MessagePreviewText.Text = message.Length == 0 ? "Enter a message first." : "★  " + message;
    }

    private void LoadReminders()
    {
        try
        {
            if (File.Exists(_remindersPath))
            {
                var loaded = JsonSerializer.Deserialize<List<SamyReminder>>(File.ReadAllText(_remindersPath));
                if (loaded is not null) _reminders.AddRange(loaded);
            }
            RefreshRemindersList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            ReminderStatusText.Text = "Couldn't load local reminders";
        }
    }

    private void SaveReminders()
    {
        try { File.WriteAllText(_remindersPath, JsonSerializer.Serialize(_reminders)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReminderStatusText.Text = "Couldn't save reminders";
        }
    }

    private void RefreshRemindersList()
    {
        RemindersList.Items.Clear();
        foreach (var reminder in _reminders.OrderBy(r => r.DueAt))
        {
            string status = reminder.Notified ? " · notified" : "";
            RemindersList.Items.Add($"{reminder.DueAt:g}  ·  {reminder.Text}{status}");
        }
        if (_reminders.Count == 0) RemindersList.Items.Add("No reminders yet");
    }

    private void AddReminderButton_Click(object sender, RoutedEventArgs e)
    {
        string text = ReminderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            ReminderStatusText.Text = "Enter a reminder first";
            return;
        }
        DateTime day = CalendarControl.SelectedDates.Count > 0
            ? CalendarControl.SelectedDates[0].Date
            : DateTime.Today;
        DateTime due = day.Add(ReminderTimePicker.Time);
        if (due <= DateTime.Now)
        {
            ReminderStatusText.Text = "Choose a future time for this reminder";
            return;
        }
        _reminders.Add(new SamyReminder { Text = text, DueAt = due });
        SaveReminders();
        RefreshRemindersList();
        ReminderTextBox.Text = "";
        ReminderStatusText.Text = "Reminder saved on this PC";
    }

    private void CheckReminders()
    {
        bool changed = false;
        foreach (var reminder in _reminders.Where(r => !r.Notified && r.DueAt <= DateTime.Now))
        {
            reminder.Notified = true;
            NoteStatusText.Text = $"Reminder · {reminder.Text}";
            ReminderStatusText.Text = $"Reminder due · {reminder.Text}";
            changed = true;
        }
        if (changed)
        {
            SaveReminders();
            RefreshRemindersList();
        }
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

public sealed class SamyReminder
{
    public string Text { get; set; } = string.Empty;
    public DateTime DueAt { get; set; }
    public bool Notified { get; set; }
}


public sealed class SamyLink { public string Title { get; set; } = ""; public string Url { get; set; } = ""; }
public sealed class SamyTask { public string Text { get; set; } = ""; public bool Done { get; set; } }
