using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Samy.Core;

namespace Samy;

// Pomodoro cycles, multi-category unit conversion and configurable world clocks.
// The logic is in Samy.Core (unit-tested); this file only connects it to the window.
public sealed partial class MainWindow
{
    private readonly PomodoroEngine _pomodoro = new();
    private WorldClockList _worldClocks = new();
    private bool _upgradesReady;

    private string PomodoroPath => Path.Combine(_dataDirectory, "pomodoro.json");
    private string WorldClocksPath => Path.Combine(_dataDirectory, "world-clocks.json");

    private void InitializeUpgrades()
    {
        try
        {
            if (File.Exists(WorldClocksPath))
            {
                var ids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(WorldClocksPath));
                if (ids is not null) _worldClocks = new WorldClockList(ids);
            }
            if (File.Exists(PomodoroPath))
            {
                var saved = JsonSerializer.Deserialize<PomodoroSettings>(File.ReadAllText(PomodoroPath));
                if (saved is not null) _pomodoro.Configure(saved);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }

        UnitCategoryBox.Items.Clear();
        foreach (var category in UnitConverter.Categories) UnitCategoryBox.Items.Add(category.Name);
        UnitCategoryBox.SelectedIndex = 0;

        ShowPomodoroSettings();
        UpdatePomodoroDisplay();
        RenderWorldClocks();
        RefreshWorldClockRemoveList();
        _upgradesReady = true;
    }

    // ---- Unit converter ----

    private void UnitCategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UnitCategoryBox.SelectedItem is not string name || UnitConverter.FindCategory(name) is not { } category) return;
        UnitFromBox.Items.Clear();
        UnitToBox.Items.Clear();
        foreach (var unit in category.Units)
        {
            UnitFromBox.Items.Add(unit.Symbol);
            UnitToBox.Items.Add(unit.Symbol);
        }
        UnitFromBox.SelectedIndex = 0;
        UnitToBox.SelectedIndex = Math.Min(1, category.Units.Count - 1);
        UnitResultText.Text = "";
    }

    private void SwapUnitsButton_Click(object sender, RoutedEventArgs e)
    {
        int from = UnitFromBox.SelectedIndex;
        UnitFromBox.SelectedIndex = UnitToBox.SelectedIndex;
        UnitToBox.SelectedIndex = from;
    }

    private void ConvertUnitButton_Click(object sender, RoutedEventArgs e)
    {
        if (!UnitConverter.TryParseNumber(UnitInputBox.Text, out double value))
        { UnitResultText.Text = "Enter a valid number."; return; }
        if (UnitCategoryBox.SelectedItem is not string category
            || UnitFromBox.SelectedItem is not string from
            || UnitToBox.SelectedItem is not string to)
        { UnitResultText.Text = "Choose a category and both units."; return; }

        UnitResultText.Text = UnitConverter.TryConvert(category, from, to, value, out double result)
            ? $"{UnitConverter.Format(value)} {from} = {UnitConverter.Format(result)} {to}"
            : "That conversion isn't available.";
    }

    // ---- Pomodoro ----

    private void TickPomodoro()
    {
        if (!_upgradesReady) return;
        var result = _pomodoro.Tick();
        UpdatePomodoroDisplay();
        if (result != PomodoroTickResult.PhaseCompleted) return;

        var s = _pomodoro.Settings;
        (string title, string body) = _pomodoro.Phase switch
        {
            PomodoroPhase.ShortBreak => ("Focus session complete", $"Take a {s.ShortBreakMinutes}-minute break."),
            PomodoroPhase.LongBreak => ("Cycle complete", $"Take a {s.LongBreakMinutes}-minute break. You've earned it."),
            _ => ("Break over", $"Time for {s.FocusMinutes} minutes of focus."),
        };
        PomodoroStatusText.Text = title + " · " + body;
        ShowNativeNotification(title, body);
    }

    private void UpdatePomodoroDisplay()
    {
        int seconds = _pomodoro.RemainingSeconds;
        PomodoroPhaseText.Text = PomodoroEngine.Label(_pomodoro.Phase);
        PomodoroTimeText.Text = $"{seconds / 60:00}:{seconds % 60:00}";
        PomodoroStartButton.Content = _pomodoro.IsRunning ? "Pause" : "Start";
        int perCycle = _pomodoro.Settings.SessionsBeforeLongBreak;
        int current = _pomodoro.SessionsInCurrentCycle + (_pomodoro.Phase == PomodoroPhase.Focus ? 1 : 0);
        PomodoroSessionsText.Text = $"Session {current} of {perCycle} · {_pomodoro.CompletedFocusSessions} completed";
    }

    private void ShowPomodoroSettings()
    {
        var s = _pomodoro.Settings;
        PomodoroFocusBox.Value = s.FocusMinutes;
        PomodoroShortBox.Value = s.ShortBreakMinutes;
        PomodoroLongBox.Value = s.LongBreakMinutes;
        PomodoroCyclesBox.Value = s.SessionsBeforeLongBreak;
        PomodoroAutoToggle.IsOn = s.AutoAdvance;
    }

    private void PomodoroStart_Click(object sender, RoutedEventArgs e)
    {
        if (_pomodoro.IsRunning) _pomodoro.Pause(); else _pomodoro.Start();
        UpdatePomodoroDisplay();
    }

    private void PomodoroSkip_Click(object sender, RoutedEventArgs e)
    {
        _pomodoro.Skip();
        UpdatePomodoroDisplay();
    }

    private void PomodoroReset_Click(object sender, RoutedEventArgs e)
    {
        _pomodoro.Reset();
        PomodoroStatusText.Text = "";
        UpdatePomodoroDisplay();
    }

    private void PomodoroApply_Click(object sender, RoutedEventArgs e)
    {
        var current = _pomodoro.Settings;
        // NumberBox.Value is NaN when the box is empty or invalid; keep the existing value in that case.
        static int Read(NumberBox box, int fallback) => double.IsNaN(box.Value) ? fallback : (int)Math.Round(box.Value);
        var next = new PomodoroSettings(
            Read(PomodoroFocusBox, current.FocusMinutes),
            Read(PomodoroShortBox, current.ShortBreakMinutes),
            Read(PomodoroLongBox, current.LongBreakMinutes),
            Read(PomodoroCyclesBox, current.SessionsBeforeLongBreak),
            PomodoroAutoToggle.IsOn).Clamped();

        _pomodoro.Configure(next);
        ShowPomodoroSettings(); // show the clamped values the engine actually uses
        UpdatePomodoroDisplay();
        try { File.WriteAllText(PomodoroPath, JsonSerializer.Serialize(next)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        PomodoroStatusText.Text = "Pomodoro settings saved.";
    }

    // ---- World clocks ----

    private void RenderWorldClocks() =>
        WorldClocksText.Text = string.Join("\n", _worldClocks.Format(DateTimeOffset.UtcNow));

    private void RefreshWorldClockRemoveList()
    {
        WorldClockRemoveBox.Items.Clear();
        foreach (var id in _worldClocks.Ids) WorldClockRemoveBox.Items.Add(id);
    }

    private void SaveWorldClocks()
    {
        try { File.WriteAllText(WorldClocksPath, JsonSerializer.Serialize(_worldClocks.Ids)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void AddWorldClock_Click(object sender, RoutedEventArgs e)
    {
        bool added = _worldClocks.TryAdd(WorldClockAddBox.Text, out string message);
        WorldClockStatusText.Text = message;
        if (!added) return;
        WorldClockAddBox.Text = "";
        SaveWorldClocks();
        RefreshWorldClockRemoveList();
        RenderWorldClocks();
    }

    private void RemoveWorldClock_Click(object sender, RoutedEventArgs e)
    {
        if (WorldClockRemoveBox.SelectedItem is not string id) { WorldClockStatusText.Text = "Choose a clock to remove first."; return; }
        if (_worldClocks.Ids.Count <= 1) { WorldClockStatusText.Text = "Keep at least one clock."; return; }
        if (!_worldClocks.Remove(id)) return;
        WorldClockStatusText.Text = "Removed.";
        SaveWorldClocks();
        RefreshWorldClockRemoveList();
        RenderWorldClocks();
    }
}
