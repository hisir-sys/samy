using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
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
    private readonly string _clipboardPinsPath;
    private readonly string _clipboardTimesPath;
    private readonly string _filesPath;
    private readonly string _settingsPath;
    private readonly string _remindersPath;
    private readonly string _linksPath;
    private readonly string _tasksPath;
    private readonly string _screenTimePath;
    private readonly string _shelvesPath;
    private readonly string _providerCredentialPath;
    private readonly string _calendarFeedsPath;
    private readonly List<string> _calendarFeedUrls = [];
    private DateTime _lastCalendarFeedRefresh = DateTime.MinValue;
    private bool _calendarFeedRefreshRunning;
    private readonly Dictionary<string, List<string>> _shelves = new(StringComparer.OrdinalIgnoreCase);
    private string _selectedShelf = "General";
    private DateTime _lastSystemExtrasRefresh = DateTime.MinValue;
    private bool _notificationsRegistered;
    private readonly List<SamyLink> _savedLinks = [];
    private readonly List<SamyTask> _tasks = [];
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly Dictionary<string, int> _foregroundSeconds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, int>> _screenTimeData = new(StringComparer.Ordinal);
    private DateTime _lastScreenTimeSave = DateTime.UtcNow;
    private bool _screenTimeEnabled;
    private ulong _lastSystemIdle;
    private ulong _lastSystemKernel;
    private ulong _lastSystemUser;
    private int _toolSeconds = 25 * 60;
    private bool _toolTimerRunning;
    private bool _stopwatchMode;
    private int _stopwatchSeconds;
    private readonly List<SamyReminder> _reminders = [];
    private readonly List<string> _clipboardItems = [];
    private readonly List<string> _visibleClipboardItems = [];
    private readonly HashSet<string> _pinnedClipboardItems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _clipboardCapturedAt = new(StringComparer.Ordinal);
    private List<string> _clipboardExclusions = ["password", "one-time code", "verification code", "otp"];
    private int _clipboardRetentionDays = 7;
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
        _clipboardPinsPath = Path.Combine(_dataDirectory, "clipboard-pins.json");
        _clipboardTimesPath = Path.Combine(_dataDirectory, "clipboard-times.json");
        _filesPath = Path.Combine(_dataDirectory, "file-shelf.json");
        _settingsPath = Path.Combine(_dataDirectory, "settings.json");
        _remindersPath = Path.Combine(_dataDirectory, "reminders.json");
        _linksPath = Path.Combine(_dataDirectory, "saved-links.json");
        _tasksPath = Path.Combine(_dataDirectory, "tasks.json");
        _screenTimePath = Path.Combine(_dataDirectory, "screen-time.json");
        _shelvesPath = Path.Combine(_dataDirectory, "file-shelves.json");
        _providerCredentialPath = Path.Combine(_dataDirectory, "analytics-provider.json");
        _calendarFeedsPath = Path.Combine(_dataDirectory, "calendar-feeds.json");

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
        try
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
        catch (Exception ex)
        {
            // A title-bar/window-manager API failure must not prevent the main workspace from appearing.
            try
            {
                File.AppendAllText(
                    Path.Combine(_dataDirectory, "startup-error.log"),
                    $"[{DateTimeOffset.Now:O}] Window configuration fallback{Environment.NewLine}{ex}{Environment.NewLine}");
            }
            catch { }
        }
    }

    private void PositionWindow(int width, int height)
    {
        nint hwnd = WindowNative.GetWindowHandle(this);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        nint monitor = MonitorFromWindow(hwnd, 2);
        int x, y;
        if (monitor != 0 && GetMonitorInfo(monitor, ref info))
        {
            int workWidth = info.Work.Right - info.Work.Left;
            x = info.Work.Left + Math.Max(0, (workWidth - width) / 2);
            y = info.Work.Top + 12;
        }
        else { x = Math.Max(0, (GetSystemMetrics(0) - width) / 2); y = 12; }
        SetWindowPos(hwnd, _alwaysOnTop ? HwndTopmost : HwndNotTopmost, x, y, width, height, SwpNoActivate | SwpShowWindow);
    }

    private async Task InitializeAsync()
    {
        await LoadNoteAsync();
        LoadJsonList(_clipboardPath, _clipboardItems);
        var savedPins = new List<string>();
        LoadJsonList(_clipboardPinsPath, savedPins);
        foreach (string pin in savedPins) _pinnedClipboardItems.Add(pin);
        try
        {
            if (File.Exists(_clipboardTimesPath))
            {
                var savedTimes = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(_clipboardTimesPath));
                if (savedTimes is not null) foreach (var pair in savedTimes) _clipboardCapturedAt[pair.Key] = pair.Value;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        foreach (string item in _clipboardItems) if (!_clipboardCapturedAt.ContainsKey(item)) _clipboardCapturedAt[item] = DateTime.Now;
        ApplyClipboardRetention();
        LoadJsonList(_filesPath, _fileItems);
        LoadShelves();
        LoadReminders();
        LoadCalendarFeeds();
        LoadToolData();
        LoadScreenTime();
        RefreshClipboardList();
        RefreshFilesList();
        LoadSettings();
        LoadSavedProviderSettings();
        RegisterNativeNotifications();
        _ = RefreshCalendarFeedsAsync();
        await RefreshMediaAsync();
    }

    private void UiTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        UpdateClock();
        RefreshMetrics();
        CheckReminders();
        UpdateWorldClocks();
        TrackForegroundApp();
        if ((DateTime.UtcNow - _lastSystemExtrasRefresh).TotalSeconds >= 10) RefreshSystemDetails();
        if (_calendarFeedUrls.Count > 0 && !_calendarFeedRefreshRunning && (DateTime.UtcNow - _lastCalendarFeedRefresh).TotalHours >= 12) _ = RefreshCalendarFeedsAsync();
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
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                ulong idleNow = FileTimeToUInt64(idle);
                ulong kernelNow = FileTimeToUInt64(kernel);
                ulong userNow = FileTimeToUInt64(user);
                if (_lastSystemKernel != 0)
                {
                    ulong totalDelta = (kernelNow - _lastSystemKernel) + (userNow - _lastSystemUser);
                    ulong idleDelta = idleNow - _lastSystemIdle;
                    double cpuPercent = totalDelta == 0 ? 0 : 100d * (totalDelta - idleDelta) / totalDelta;
                    CpuText.Text = $"PC CPU · {Math.Clamp(cpuPercent, 0d, 100d):0}%";
                }
                _lastSystemIdle = idleNow;
                _lastSystemKernel = kernelNow;
                _lastSystemUser = userNow;
            }

            var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref memory))
            {
                double totalGb = memory.TotalPhysical / 1024d / 1024d / 1024d;
                double usedGb = (memory.TotalPhysical - memory.AvailablePhysical) / 1024d / 1024d / 1024d;
                MemoryText.Text = $"PC RAM · {usedGb:0.0} / {totalGb:0.0} GB ({memory.MemoryLoad}%)";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or EntryPointNotFoundException)
        {
            CpuText.Text = "PC CPU unavailable";
            MemoryText.Text = "PC memory unavailable";
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
        InsightsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
    }

    private void HomeNav_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage);
    private void ClipboardNav_Click(object sender, RoutedEventArgs e) => ShowPage(ClipboardPage);
    private void FilesNav_Click(object sender, RoutedEventArgs e) => ShowPage(FilesPage);
    private void MediaNav_Click(object sender, RoutedEventArgs e) => ShowPage(MediaPage);
    private void CalendarNav_Click(object sender, RoutedEventArgs e) => ShowPage(CalendarPage);
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage);
    private void InsightsNav_Click(object sender, RoutedEventArgs e) { ShowPage(InsightsPage); RefreshSystemDetails(); }
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
                _screenTimeEnabled = root.TryGetProperty("screenTimeEnabled", out var screen) && screen.GetBoolean();
                if (root.TryGetProperty("clipboardRetentionDays", out var retention) && retention.TryGetInt32(out int days))
                    _clipboardRetentionDays = days is 1 or 7 or 30 ? days : 0;
                if (root.TryGetProperty("clipboardExclusions", out var exclusions) && exclusions.ValueKind == JsonValueKind.Array)
                    _clipboardExclusions = exclusions.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString() ?? "").Where(v => v.Length > 0).ToList();
            }
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            StartWithWindowsToggle.IsOn = key?.GetValue("SAMY") is not null;
            AlwaysOnTopToggle.IsOn = _alwaysOnTop;
            ClipboardToggle.IsOn = _clipboardEnabled;
            ScreenTimeToggle.IsOn = _screenTimeEnabled;
            ClipboardRetentionBox.SelectedIndex = _clipboardRetentionDays switch { 1 => 0, 7 => 1, 30 => 2, _ => 3 };
            ClipboardExclusionsBox.Text = string.Join(", ", _clipboardExclusions);
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
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new { alwaysOnTop = _alwaysOnTop, clipboardEnabled = _clipboardEnabled, clipboardRetentionDays = _clipboardRetentionDays, clipboardExclusions = _clipboardExclusions, screenTimeEnabled = _screenTimeEnabled }));
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
            if (_clipboardExclusions.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                ClipboardStatusText.Text = "Skipped: matched a sensitive-content exclusion";
                return;
            }
            _clipboardItems.Insert(0, text);
            _clipboardCapturedAt[text] = DateTime.Now;
            while (_clipboardItems.Count > MaxClipboardItems)
            {
                int removable = _clipboardItems.FindLastIndex(item => !_pinnedClipboardItems.Contains(item));
                if (removable < 0) break;
                string removed = _clipboardItems[removable];
                _clipboardItems.RemoveAt(removable);
                _clipboardCapturedAt.Remove(removed);
            }
            ApplyClipboardRetention();
            SaveClipboardData();
            RefreshClipboardList();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException) { }
    }

    private void RefreshClipboardList()
    {
        ClipboardList.Items.Clear();
        _visibleClipboardItems.Clear();
        string query = ClipboardSearchBox?.Text?.Trim() ?? "";
        foreach (string item in _clipboardItems.Where(item => query.Length == 0 || item.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            _visibleClipboardItems.Add(item);
            string pin = _pinnedClipboardItems.Contains(item) ? "📌 " : "";
            ClipboardList.Items.Add(pin + (item.Length > 110 ? item[..110] + "…" : item));
        }
        if (ClipboardList.Items.Count == 0) ClipboardList.Items.Add(query.Length == 0 ? "No saved text yet" : "No matches");
    }

    private void ClipboardSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshClipboardList();

    private void SaveClipboardData()
    {
        SaveJsonList(_clipboardPath, _clipboardItems);
        SaveJsonList(_clipboardPinsPath, _pinnedClipboardItems.ToList());
        try { File.WriteAllText(_clipboardTimesPath, JsonSerializer.Serialize(_clipboardCapturedAt)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void ApplyClipboardRetention()
    {
        if (_clipboardRetentionDays <= 0) return;
        DateTime cutoff = DateTime.Now.AddDays(-_clipboardRetentionDays);
        foreach (string item in _clipboardItems.ToList())
        {
            if (_pinnedClipboardItems.Contains(item)) continue;
            if (_clipboardCapturedAt.TryGetValue(item, out DateTime captured) && captured < cutoff)
            {
                _clipboardItems.Remove(item);
                _clipboardCapturedAt.Remove(item);
            }
        }
    }

    private void PinClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        int index = ClipboardList.SelectedIndex;
        if (index < 0 || index >= _visibleClipboardItems.Count) return;
        string item = _visibleClipboardItems[index];
        if (!_pinnedClipboardItems.Add(item)) _pinnedClipboardItems.Remove(item);
        SaveClipboardData();
        RefreshClipboardList();
        ClipboardStatusText.Text = _pinnedClipboardItems.Contains(item) ? "Item pinned · excluded from retention" : "Item unpinned";
    }

    private void DeleteClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        int index = ClipboardList.SelectedIndex;
        if (index < 0 || index >= _visibleClipboardItems.Count) return;
        string item = _visibleClipboardItems[index];
        _clipboardItems.Remove(item);
        _pinnedClipboardItems.Remove(item);
        _clipboardCapturedAt.Remove(item);
        SaveClipboardData();
        RefreshClipboardList();
        ClipboardStatusText.Text = "Selected item deleted";
    }

    private void ClipboardRetentionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || ClipboardRetentionBox.SelectedIndex < 0) return;
        _clipboardRetentionDays = ClipboardRetentionBox.SelectedIndex switch { 0 => 1, 1 => 7, 2 => 30, _ => 0 };
    }

    private void SaveClipboardPrivacyButton_Click(object sender, RoutedEventArgs e)
    {
        _clipboardExclusions = ClipboardExclusionsBox.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ApplyClipboardRetention();
        SaveClipboardData();
        SaveSettings();
        RefreshClipboardList();
        ClipboardStatusText.Text = "Clipboard privacy settings saved";
    }

    private async void ClipboardItem_Click(object sender, ItemClickEventArgs e)
    {
        int index = ClipboardList.SelectedIndex;
        if (index < 0 || index >= _visibleClipboardItems.Count) return;
        var package = new DataPackage();
        package.SetText(_visibleClipboardItems[index]);
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
        _pinnedClipboardItems.Clear();
        _clipboardCapturedAt.Clear();
        SaveClipboardData();
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
        SaveShelves();
        RefreshFilesList();
    }

    private void RefreshFilesList()
    {
        FilesList.Items.Clear();
        foreach (string path in _fileItems) FilesList.Items.Add(File.Exists(path) ? Path.GetFileName(path) : $"{Path.GetFileName(path)} · missing");
    }

    private void LoadShelves()
    {
        try
        {
            if (File.Exists(_shelvesPath))
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_shelvesPath));
                if (saved is not null) foreach (var shelf in saved) _shelves[shelf.Key] = shelf.Value;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        if (_shelves.Count == 0) _shelves["General"] = new List<string>(_fileItems);
        _selectedShelf = _shelves.Keys.First();
        _fileItems.Clear();
        _fileItems.AddRange(_shelves[_selectedShelf]);
        ShelfPicker.Items.Clear();
        foreach (string name in _shelves.Keys.OrderBy(k => k)) ShelfPicker.Items.Add(name);
        ShelfPicker.SelectedItem = _selectedShelf;
        RefreshFilesList();
        SaveShelves();
    }

    private void SaveShelves()
    {
        _shelves[_selectedShelf] = new List<string>(_fileItems);
        try { File.WriteAllText(_shelvesPath, JsonSerializer.Serialize(_shelves)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ConversionStatusText.Text = "Couldn't save file shelves."; }
        SaveJsonList(_filesPath, _fileItems);
    }

    private void ShelfPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShelfPicker.SelectedItem is not string selected || !_shelves.ContainsKey(selected) || selected == _selectedShelf) return;
        _shelves[_selectedShelf] = new List<string>(_fileItems);
        _selectedShelf = selected;
        _fileItems.Clear();
        _fileItems.AddRange(_shelves[selected]);
        RefreshFilesList();
        SaveShelves();
    }

    private void AddShelfButton_Click(object sender, RoutedEventArgs e)
    {
        string name = NewShelfNameBox.Text.Trim();
        if (name.Length is 0 or > 40 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { ConversionStatusText.Text = "Enter a valid shelf name (1–40 characters)."; return; }
        if (_shelves.ContainsKey(name)) { ConversionStatusText.Text = "That shelf already exists."; return; }
        _shelves[_selectedShelf] = new List<string>(_fileItems);
        _shelves[name] = [];
        _selectedShelf = name;
        _fileItems.Clear();
        ShelfPicker.Items.Add(name);
        ShelfPicker.SelectedItem = name;
        RefreshFilesList();
        NewShelfNameBox.Text = "";
        SaveShelves();
    }

    private void FileDropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void FileDropZone_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items.OfType<Windows.Storage.StorageFile>())
                if (!_fileItems.Contains(item.Path, StringComparer.OrdinalIgnoreCase)) _fileItems.Add(item.Path);
            SaveShelves();
            RefreshFilesList();
            ConversionStatusText.Text = $"Added files to shelf · {_selectedShelf}";
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            ConversionStatusText.Text = "Windows couldn't read the dropped files.";
        }
    }

    private void OpenSelectedFileButton_Click(object sender, RoutedEventArgs e)
    {
        int index = FilesList.SelectedIndex;
        if (index >= 0 && index < _fileItems.Count && File.Exists(_fileItems[index]))
            Process.Start(new ProcessStartInfo(_fileItems[index]) { UseShellExecute = true });
        else ConversionStatusText.Text = "Select an available file first.";
    }

    private async void ConvertImageButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var open = new FileOpenPicker();
            foreach (string extension in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif" }) open.FileTypeFilter.Add(extension);
            InitializeWithWindow.Initialize(open, WindowNative.GetWindowHandle(this));
            var source = await open.PickSingleFileAsync();
            if (source is null) return;
            string format = (ImageFormatBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "PNG";
            string extensionOut = format == "JPG" ? ".jpg" : "." + format.ToLowerInvariant();
            var save = new FileSavePicker();
            save.FileTypeChoices.Add(format, new List<string> { extensionOut });
            save.SuggestedFileName = Path.GetFileNameWithoutExtension(source.Name) + "-converted";
            InitializeWithWindow.Initialize(save, WindowNative.GetWindowHandle(this));
            var destination = await save.PickSaveFileAsync();
            if (destination is null) return;
            ConversionStatusText.Text = "Converting…";
            await AdvancedServices.ConvertImageAsync(source.Path, destination.Path, format);
            ConversionStatusText.Text = $"Saved {format} · {destination.Path}";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or COMException)
        {
            ConversionStatusText.Text = "Conversion failed · " + ex.Message;
        }
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
        SaveShelves();
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




    private async void GetWeatherButton_Click(object sender, RoutedEventArgs e)
    {
        string city = WeatherCityBox.Text.Trim();
        if (city.Length == 0) { WeatherResultText.Text = "Enter a city."; return; }
        WeatherResultText.Text = "Looking up weather…";
        try
        {
            string geoUrl = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" + Uri.EscapeDataString(city);
            using var geo = JsonDocument.Parse(await _http.GetStringAsync(geoUrl));
            if (!geo.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            { WeatherResultText.Text = "City not found."; return; }
            var place = results[0];
            double lat = place.GetProperty("latitude").GetDouble();
            double lon = place.GetProperty("longitude").GetDouble();
            string display = place.GetProperty("name").GetString() ?? city;
            string weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&longitude={lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m";
            using var weather = JsonDocument.Parse(await _http.GetStringAsync(weatherUrl));
            var current = weather.RootElement.GetProperty("current");
            WeatherResultText.Text = $"{display}\n{current.GetProperty("temperature_2m").GetDouble():0.#}°C · feels like {current.GetProperty("apparent_temperature").GetDouble():0.#}°C\nHumidity {current.GetProperty("relative_humidity_2m").GetInt32()}% · wind {current.GetProperty("wind_speed_10m").GetDouble():0.#} km/h\nWeather code {current.GetProperty("weather_code").GetInt32()} · live provider data";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        { WeatherResultText.Text = "Weather unavailable. Check your connection and city name."; }
    }

    private async void ConvertCurrencyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(CurrencyAmountBox.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal amount) || amount < 0)
        { CurrencyResultText.Text = "Enter a valid non-negative amount."; return; }
        string from = CurrencyFromBox.Text.Trim().ToUpperInvariant();
        string to = CurrencyToBox.Text.Trim().ToUpperInvariant();
        if (from.Length != 3 || to.Length != 3) { CurrencyResultText.Text = "Use three-letter currency codes, e.g. USD and INR."; return; }
        CurrencyResultText.Text = "Fetching exchange rate…";
        try
        {
            using var response = JsonDocument.Parse(await _http.GetStringAsync($"https://api.frankfurter.app/latest?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}"));
            decimal rate = response.RootElement.GetProperty("rates").GetProperty(to).GetDecimal();
            string date = response.RootElement.GetProperty("date").GetString() ?? "latest";
            CurrencyResultText.Text = $"{amount:0.##} {from} = {(amount * rate):0.####} {to}\nRate {rate:0.######} · dated {date} · Frankfurter";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        { CurrencyResultText.Text = "Rate unavailable. Check currency codes and internet connection."; }
    }

    private void TrackForegroundApp()
    {
        if (!_screenTimeEnabled) return;
        nint hwnd = GetForegroundWindow();
        if (hwnd == 0) return;
        _ = GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId == 0 || processId == (uint)Environment.ProcessId) return;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            string name = process.ProcessName;
            _foregroundSeconds[name] = _foregroundSeconds.GetValueOrDefault(name) + 1;
            string today = DateTime.Today.ToString("yyyy-MM-dd");
            _screenTimeData[today] = new Dictionary<string, int>(_foregroundSeconds, StringComparer.OrdinalIgnoreCase);
            var top = _foregroundSeconds.OrderByDescending(p => p.Value).Take(6)
                .Select(p => $"{p.Key} · {TimeSpan.FromSeconds(p.Value):hh\\:mm\\:ss}");
            ScreenTimeText.Text = $"Today · {today}\n" + string.Join("\n", top);
            if ((DateTime.UtcNow - _lastScreenTimeSave).TotalSeconds >= 30) SaveScreenTime();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private void LoadScreenTime()
    {
        try
        {
            if (File.Exists(_screenTimePath))
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(File.ReadAllText(_screenTimePath));
                if (saved is not null) foreach (var day in saved) _screenTimeData[day.Key] = day.Value;
            }
            string today = DateTime.Today.ToString("yyyy-MM-dd");
            if (_screenTimeData.TryGetValue(today, out var entries))
                foreach (var entry in entries) _foregroundSeconds[entry.Key] = entry.Value;
            if (_screenTimeEnabled)
                ScreenTimeText.Text = $"Today · {today}\n" + string.Join("\n", _foregroundSeconds.OrderByDescending(p => p.Value).Take(6).Select(p => $"{p.Key} · {TimeSpan.FromSeconds(p.Value):hh\\:mm\\:ss}"));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            ScreenTimeText.Text = "Couldn't load local screen-time history.";
        }
    }

    private void SaveScreenTime()
    {
        try
        {
            string today = DateTime.Today.ToString("yyyy-MM-dd");
            _screenTimeData[today] = new Dictionary<string, int>(_foregroundSeconds, StringComparer.OrdinalIgnoreCase);
            foreach (string oldDay in _screenTimeData.Keys.OrderByDescending(k => k).Skip(90).ToList()) _screenTimeData.Remove(oldDay);
            File.WriteAllText(_screenTimePath, JsonSerializer.Serialize(_screenTimeData));
            _lastScreenTimeSave = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ScreenTimeText.Text = "Couldn't save local screen-time history.";
        }
    }

    private void ScreenTimeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _screenTimeEnabled = ScreenTimeToggle.IsOn;
        if (_screenTimeEnabled) LoadScreenTime();
        else ScreenTimeText.Text = "Tracking paused. Existing local reports are preserved.";
        SaveSettings();
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
        string recurrence = (ReminderRecurrenceBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ONCE";
        _reminders.Add(new SamyReminder { Text = text, DueAt = due, Recurrence = recurrence });
        SaveReminders();
        RefreshRemindersList();
        ReminderTextBox.Text = "";
        ReminderStatusText.Text = "Reminder saved on this PC";
    }

    private void CheckReminders()
    {
        bool changed = false;
        foreach (var reminder in _reminders.Where(r => !r.Notified && r.DueAt <= DateTime.Now).ToList())
        {
            ShowNativeNotification("SAMY reminder", reminder.Text);
            NoteStatusText.Text = $"Reminder · {reminder.Text}";
            ReminderStatusText.Text = $"Reminder due · {reminder.Text}";
            if (reminder.Recurrence is "DAILY" or "WEEKLY" or "MONTHLY")
            {
                DateTime next = reminder.DueAt;
                do { next = reminder.Recurrence switch { "DAILY" => next.AddDays(1), "WEEKLY" => next.AddDays(7), _ => next.AddMonths(1) }; } while (next <= DateTime.Now);
                reminder.DueAt = next;
                reminder.Notified = false;
            }
            else reminder.Notified = true;
            changed = true;
        }
        if (changed)
        {
            SaveReminders();
            RefreshRemindersList();
        }
    }

    private void LoadCalendarFeeds()
    {
        try
        {
            if (File.Exists(_calendarFeedsPath))
            {
                var saved = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_calendarFeedsPath));
                if (saved is not null) _calendarFeedUrls.AddRange(saved.Where(IsHttpsFeed).Distinct(StringComparer.OrdinalIgnoreCase));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }

    private static bool IsHttpsFeed(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private void SaveCalendarFeeds()
    {
        try { File.WriteAllText(_calendarFeedsPath, JsonSerializer.Serialize(_calendarFeedUrls)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ReminderStatusText.Text = "Couldn't save calendar subscriptions."; }
    }

    private async void SubscribeCalendarFeedButton_Click(object sender, RoutedEventArgs e)
    {
        string url = CalendarFeedUrlBox.Text.Trim();
        if (!IsHttpsFeed(url)) { ReminderStatusText.Text = "Calendar feeds must use HTTPS."; return; }
        if (!_calendarFeedUrls.Contains(url, StringComparer.OrdinalIgnoreCase))
        {
            _calendarFeedUrls.Add(url);
            SaveCalendarFeeds();
        }
        await RefreshCalendarFeedsAsync();
    }

    private async Task RefreshCalendarFeedsAsync()
    {
        if (_calendarFeedRefreshRunning || _calendarFeedUrls.Count == 0) return;
        _calendarFeedRefreshRunning = true;
        int added = 0, failed = 0;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            foreach (string url in _calendarFeedUrls.ToList())
            {
                try
                {
                    string ics = await client.GetStringAsync(url);
                    if (ics.Length > 2_000_000) { failed++; continue; }
                    var imported = AdvancedServices.ParseCalendarFeed(ics, url, DateTime.Now);
                    foreach (var item in imported)
                    {
                        if (_reminders.Any(existing => existing.FeedSource == item.FeedSource && existing.Text == item.Text && existing.DueAt == item.DueAt)) continue;
                        _reminders.Add(item); added++;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
                { failed++; }
            }
            if (added > 0) { SaveReminders(); RefreshRemindersList(); }
            _lastCalendarFeedRefresh = DateTime.UtcNow;
            ReminderStatusText.Text = $"Calendar feeds refreshed · {added} new events · {failed} feed errors · {_calendarFeedUrls.Count} subscriptions saved.";
        }
        finally { _calendarFeedRefreshRunning = false; }
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

    private void RefreshSystemDetailsButton_Click(object sender, RoutedEventArgs e) => RefreshSystemDetails();

    private void RefreshSystemDetails()
    {
        SystemExtrasText.Text = AdvancedServices.GetSystemExtras();
        _lastSystemExtrasRefresh = DateTime.UtcNow;
    }

    private async void ChooseCodingRepoButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) CodingRepoPathBox.Text = folder.Path;
    }

    private async void AnalyzeCodingButton_Click(object sender, RoutedEventArgs e)
    {
        string folder = CodingRepoPathBox.Text.Trim();
        if (folder.Length == 0) { CodingReportText.Text = "Choose a local Git repository first."; return; }
        CodingReportText.Text = "Analyzing local Git history…";
        try
        {
            var report = await Task.Run(() => AdvancedServices.AnalyzeGitRepository(folder));
            CodingReportText.Text = $"Last 30 days\nCommits · {report.CommitsLast30Days}\nActive days · {report.ActiveDays}\nChanged-file entries · {report.ChangedFileEntries}\nFile types · {(report.TopFileTypes.Count == 0 ? "No file extensions in the selected history" : string.Join(", ", report.TopFileTypes))}\nLocal repository · {report.Repository}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException or UnauthorizedAccessException)
        { CodingReportText.Text = "Analysis unavailable · " + ex.Message; }
    }

    private void LoadSavedProviderSettings()
    {
        try
        {
            if (!File.Exists(_providerCredentialPath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(_providerCredentialPath));
            ProviderEndpointBox.Text = doc.RootElement.GetProperty("endpoint").GetString() ?? "";
            ProviderStatusText.Text = "Encrypted token saved for this Windows account. Press Test to verify the live provider again.";
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { ProviderStatusText.Text = "Saved provider settings could not be read."; }
    }

    private async void ConnectProviderButton_Click(object sender, RoutedEventArgs e)
    {
        string endpoint = ProviderEndpointBox.Text.Trim();
        string token = ProviderTokenBox.Password;
        if (token.Length == 0)
        {
            try
            {
                using var saved = JsonDocument.Parse(File.ReadAllText(_providerCredentialPath));
                token = AdvancedServices.UnprotectToken(saved.RootElement.GetProperty("protectedToken").GetString() ?? "");
            }
            catch { ProviderStatusText.Text = "Enter the provider's bearer token."; return; }
        }
        ProviderStatusText.Text = "Checking the real provider endpoint…";
        try
        {
            var result = await AdvancedServices.ProbeProviderAsync(endpoint, token);
            if (!result.Success) { ProviderStatusText.Text = $"Provider returned HTTP {result.StatusCode}. Token was not saved."; return; }
            string protectedToken = AdvancedServices.ProtectToken(token);
            File.WriteAllText(_providerCredentialPath, JsonSerializer.Serialize(new { endpoint, protectedToken }));
            ProviderTokenBox.Password = "";
            ProviderStatusText.Text = $"Connected · HTTP {result.StatusCode} · {result.Summary}. No metrics are inferred; this endpoint's response was verified."; 
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException or CryptographicException or UnauthorizedAccessException)
        { ProviderStatusText.Text = "Connection failed · " + ex.Message; }
    }

    private void RegisterNativeNotifications()
    {
        try { AppNotificationManager.Default.Register(); _notificationsRegistered = true; }
        catch (Exception ex) { _notificationsRegistered = false; ProviderStatusText.Text = "Windows notifications could not be registered: " + ex.Message; }
    }

    private void ShowNativeNotification(string title, string body)
    {
        try
        {
            if (!_notificationsRegistered) RegisterNativeNotifications();
            string safeTitle = System.Security.SecurityElement.Escape(title) ?? "SAMY";
            string safeBody = System.Security.SecurityElement.Escape(body) ?? "";
            AppNotificationManager.Default.Show(new AppNotification($"<toast><visual><binding template='ToastGeneric'><text>{safeTitle}</text><text>{safeBody}</text></binding></visual></toast>"));
        }
        catch (Exception ex) { ReminderStatusText.Text = "In-app reminder shown; Windows notification unavailable."; }
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
        if (_screenTimeEnabled) SaveScreenTime();
        _clipboardTimer.Stop();
        _currentProcess.Dispose();
        SaveSettings();
    }

    private static ulong FileTimeToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME value)
        => ((ulong)(uint)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME idle, out System.Runtime.InteropServices.ComTypes.FILETIME kernel, out System.Runtime.InteropServices.ComTypes.FILETIME user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, int hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }
    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
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
    public string Recurrence { get; set; } = "ONCE";
    public string FeedSource { get; set; } = "";
}


public sealed class SamyLink { public string Title { get; set; } = ""; public string Url { get; set; } = ""; }
public sealed class SamyTask { public string Text { get; set; } = ""; public bool Done { get; set; } }
