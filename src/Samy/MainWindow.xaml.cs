using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Samy;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherQueueTimer _uiTimer;
    private readonly Process _currentProcess;
    private readonly string _notePath;
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuSample = DateTime.UtcNow;
    private int _focusSecondsRemaining = 25 * 60;
    private bool _focusIsRunning;

    public MainWindow()
    {
        InitializeComponent();
        Title = "SAMY";

        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SAMY");
        Directory.CreateDirectory(dataDirectory);
        _notePath = Path.Combine(dataDirectory, "quick-note.txt");

        _currentProcess = Process.GetCurrentProcess();
        _lastCpuTime = _currentProcess.TotalProcessorTime;

        _uiTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _uiTimer.Interval = TimeSpan.FromSeconds(1);
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();

        Closed += MainWindow_Closed;
        UpdateClock();
        UpdateFocusDisplay();
        RefreshMetrics();
        _ = LoadNoteAsync();
    }

    private void UiTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        UpdateClock();
        RefreshMetrics();

        if (_focusIsRunning)
        {
            _focusSecondsRemaining = Math.Max(0, _focusSecondsRemaining - 1);
            UpdateFocusDisplay();

            if (_focusSecondsRemaining == 0)
            {
                _focusIsRunning = false;
                FocusButton.Content = "Start focus";
                FocusStatusText.Text = "Session complete. Take a short break.";
            }
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
                CpuText.Text = $"{Math.Clamp(cpuPercent, 0d, 100d):0}%";
            }

            _lastCpuTime = cpuNow;
            _lastCpuSample = sampleNow;
            MemoryText.Text = $"{_currentProcess.WorkingSet64 / 1024d / 1024d:0} MB";
        }
        catch (InvalidOperationException)
        {
            CpuText.Text = "—";
            MemoryText.Text = "—";
        }
    }

    private void FocusButton_Click(object sender, RoutedEventArgs e)
    {
        if (_focusSecondsRemaining == 0)
        {
            _focusSecondsRemaining = 25 * 60;
        }

        _focusIsRunning = !_focusIsRunning;
        FocusButton.Content = _focusIsRunning ? "Pause focus" : "Resume focus";
        FocusStatusText.Text = _focusIsRunning
            ? "Stay with one thing at a time."
            : "Paused. Pick it up when you're ready.";
    }

    private void ResetFocusButton_Click(object sender, RoutedEventArgs e)
    {
        _focusIsRunning = false;
        _focusSecondsRemaining = 25 * 60;
        FocusButton.Content = "Start focus";
        FocusStatusText.Text = "A little progress adds up.";
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
            if (File.Exists(_notePath))
            {
                NoteBox.Text = await File.ReadAllTextAsync(_notePath);
            }
        }
        catch (IOException)
        {
            NoteStatusText.Text = "Couldn't load the local note";
        }
        catch (UnauthorizedAccessException)
        {
            NoteStatusText.Text = "Note folder isn't accessible";
        }
    }

    private async void SaveNoteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_notePath)!);
            await File.WriteAllTextAsync(_notePath, NoteBox.Text);
            NoteStatusText.Text = $"Saved locally · {DateTime.Now:HH:mm}";
        }
        catch (IOException)
        {
            NoteStatusText.Text = "Save failed · check local storage";
        }
        catch (UnauthorizedAccessException)
        {
            NoteStatusText.Text = "Save blocked · check folder access";
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _uiTimer.Stop();
        _currentProcess.Dispose();
    }
}
