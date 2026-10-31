using Samy.Core;
using Xunit;

namespace Samy.Core.Tests;

public class UnitConverterTests
{
    [Theory]
    [InlineData("Length", "m", "ft", 1, 3.280839895)]
    [InlineData("Length", "mi", "km", 1, 1.609344)]
    [InlineData("Length", "in", "cm", 10, 25.4)]
    [InlineData("Weight", "kg", "lb", 1, 2.2046226218)]
    [InlineData("Weight", "oz", "g", 1, 28.349523125)]
    [InlineData("Temperature", "°C", "°F", 100, 212)]
    [InlineData("Temperature", "°F", "°C", -40, -40)]
    [InlineData("Temperature", "°C", "K", 0, 273.15)]
    [InlineData("Temperature", "K", "°F", 273.15, 32)]
    [InlineData("Data", "MB", "GB", 2048, 2)]
    [InlineData("Data", "GB", "MB", 1, 1024)]
    [InlineData("Volume", "gal", "L", 1, 3.785411784)]
    [InlineData("Volume", "cup", "mL", 1, 236.5882365)]
    [InlineData("Speed", "km/h", "m/s", 36, 10)]
    [InlineData("Speed", "mph", "km/h", 60, 96.56064)]
    [InlineData("Time", "h", "min", 1.5, 90)]
    [InlineData("Time", "wk", "day", 2, 14)]
    public void Converts_known_values(string cat, string from, string to, double input, double expected)
    {
        Assert.True(UnitConverter.TryConvert(cat, from, to, input, out double result));
        Assert.Equal(expected, result, 5);
    }

    [Fact]
    public void Converting_a_unit_to_itself_is_identity()
    {
        foreach (var cat in UnitConverter.Categories)
            foreach (var u in cat.Units)
            {
                Assert.True(UnitConverter.TryConvert(cat.Name, u.Symbol, u.Symbol, 12.5, out double r));
                Assert.Equal(12.5, r, 9);
            }
    }

    [Fact]
    public void Round_trip_returns_the_original_value()
    {
        foreach (var cat in UnitConverter.Categories)
            foreach (var a in cat.Units)
                foreach (var b in cat.Units)
                {
                    Assert.True(UnitConverter.TryConvert(cat.Name, a.Symbol, b.Symbol, 37.25, out double there));
                    Assert.True(UnitConverter.TryConvert(cat.Name, b.Symbol, a.Symbol, there, out double back));
                    Assert.Equal(37.25, back, 6);
                }
    }

    [Theory]
    [InlineData("Nope", "m", "ft")]
    [InlineData("Length", "zz", "ft")]
    [InlineData("Length", "m", "zz")]
    public void Unknown_category_or_unit_fails(string cat, string from, string to) =>
        Assert.False(UnitConverter.TryConvert(cat, from, to, 1, out _));

    [Fact]
    public void Non_finite_input_fails()
    {
        Assert.False(UnitConverter.TryConvert("Length", "m", "ft", double.NaN, out _));
        Assert.False(UnitConverter.TryConvert("Length", "m", "ft", double.PositiveInfinity, out _));
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData(" 3.5 ", 3.5)]
    [InlineData("1,5", 1.5)]
    [InlineData("-2.25", -2.25)]
    [InlineData("1e3", 1000)]
    public void Parses_numbers(string text, double expected)
    {
        Assert.True(UnitConverter.TryParseNumber(text, out double v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Rejects_bad_numbers(string text) => Assert.False(UnitConverter.TryParseNumber(text, out _));

    [Fact]
    public void Formats_without_trailing_zeros() =>
        Assert.Equal("3.28084", UnitConverter.Format(3.280839895));

    [Fact]
    public void Format_trims_whole_numbers() => Assert.Equal("212", UnitConverter.Format(212.0));

    [Fact]
    public void Every_category_has_unique_symbols()
    {
        foreach (var cat in UnitConverter.Categories)
            Assert.Equal(cat.Units.Count, cat.Units.Select(u => u.Symbol).Distinct().Count());
    }
}

public class PomodoroEngineTests
{
    private static void RunToEnd(PomodoroEngine e)
    {
        int guard = 0;
        while (e.Tick() != PomodoroTickResult.PhaseCompleted && guard++ < 100_000) { }
    }

    [Fact]
    public void Starts_idle_on_a_full_focus_phase()
    {
        var e = new PomodoroEngine();
        Assert.Equal(PomodoroPhase.Focus, e.Phase);
        Assert.Equal(25 * 60, e.RemainingSeconds);
        Assert.False(e.IsRunning);
    }

    [Fact]
    public void Does_not_count_down_while_paused()
    {
        var e = new PomodoroEngine();
        Assert.Equal(PomodoroTickResult.None, e.Tick());
        Assert.Equal(25 * 60, e.RemainingSeconds);
    }

    [Fact]
    public void Counts_down_once_per_tick_and_can_pause()
    {
        var e = new PomodoroEngine();
        e.Start();
        e.Tick(); e.Tick();
        Assert.Equal(25 * 60 - 2, e.RemainingSeconds);
        e.Pause();
        e.Tick();
        Assert.Equal(25 * 60 - 2, e.RemainingSeconds);
    }

    [Fact]
    public void Finishing_focus_starts_a_short_break_and_counts_the_session()
    {
        var e = new PomodoroEngine(new PomodoroSettings(FocusMinutes: 1, ShortBreakMinutes: 2));
        e.Start();
        RunToEnd(e);
        Assert.Equal(PomodoroPhase.ShortBreak, e.Phase);
        Assert.Equal(120, e.RemainingSeconds);
        Assert.Equal(1, e.CompletedFocusSessions);
        Assert.True(e.IsRunning);
    }

    [Fact]
    public void Fourth_focus_session_leads_to_a_long_break_then_the_cycle_restarts()
    {
        var e = new PomodoroEngine(new PomodoroSettings(1, 1, 3, 4, true));
        e.Start();
        var phases = new List<PomodoroPhase>();
        for (int i = 0; i < 8; i++) { RunToEnd(e); phases.Add(e.Phase); }

        Assert.Equal(new[]
        {
            PomodoroPhase.ShortBreak, PomodoroPhase.Focus,
            PomodoroPhase.ShortBreak, PomodoroPhase.Focus,
            PomodoroPhase.ShortBreak, PomodoroPhase.Focus,
            PomodoroPhase.LongBreak, PomodoroPhase.Focus,
        }, phases);
        Assert.Equal(4, e.CompletedFocusSessions);
        Assert.Equal(0, e.SessionsInCurrentCycle);
    }

    [Fact]
    public void Without_auto_advance_it_stops_at_each_phase_boundary()
    {
        var e = new PomodoroEngine(new PomodoroSettings(1, 1, 1, 4, AutoAdvance: false));
        e.Start();
        RunToEnd(e);
        Assert.Equal(PomodoroPhase.ShortBreak, e.Phase);
        Assert.False(e.IsRunning);
    }

    [Fact]
    public void Skipping_focus_does_not_count_and_stops_the_clock()
    {
        var e = new PomodoroEngine();
        e.Start();
        e.Skip();
        Assert.Equal(PomodoroPhase.ShortBreak, e.Phase);
        Assert.Equal(0, e.CompletedFocusSessions);
        Assert.False(e.IsRunning);
    }

    [Fact]
    public void Skipping_a_break_returns_to_focus()
    {
        var e = new PomodoroEngine();
        e.Skip();
        e.Skip();
        Assert.Equal(PomodoroPhase.Focus, e.Phase);
    }

    [Fact]
    public void Reset_returns_to_a_fresh_focus_phase()
    {
        var e = new PomodoroEngine(new PomodoroSettings(1, 1, 1, 2, true));
        e.Start();
        RunToEnd(e);
        e.Reset();
        Assert.Equal(PomodoroPhase.Focus, e.Phase);
        Assert.Equal(0, e.CompletedFocusSessions);
        Assert.False(e.IsRunning);
        Assert.Equal(60, e.RemainingSeconds);
    }

    [Fact]
    public void Settings_are_clamped_to_safe_ranges()
    {
        var s = new PomodoroSettings(0, -5, 9999, 1, true).Clamped();
        Assert.Equal(1, s.FocusMinutes);
        Assert.Equal(1, s.ShortBreakMinutes);
        Assert.Equal(120, s.LongBreakMinutes);
        Assert.Equal(2, s.SessionsBeforeLongBreak);
    }

    [Fact]
    public void Reconfiguring_an_unstarted_phase_applies_the_new_length()
    {
        var e = new PomodoroEngine();
        e.Configure(new PomodoroSettings(FocusMinutes: 50));
        Assert.Equal(50 * 60, e.RemainingSeconds);
    }

    [Fact]
    public void Reconfiguring_mid_phase_keeps_progress_unless_it_exceeds_the_new_length()
    {
        var e = new PomodoroEngine();
        e.Start();
        for (int i = 0; i < 100; i++) e.Tick();
        int before = e.RemainingSeconds;
        e.Configure(new PomodoroSettings(FocusMinutes: 50));
        Assert.Equal(before, e.RemainingSeconds);
        e.Configure(new PomodoroSettings(FocusMinutes: 10));
        Assert.Equal(10 * 60, e.RemainingSeconds);
    }

    [Fact]
    public void Labels_are_human_readable()
    {
        Assert.Equal("Focus", PomodoroEngine.Label(PomodoroPhase.Focus));
        Assert.Equal("Short break", PomodoroEngine.Label(PomodoroPhase.ShortBreak));
        Assert.Equal("Long break", PomodoroEngine.Label(PomodoroPhase.LongBreak));
    }
}

public class WorldClockTests
{
    [Fact]
    public void Defaults_to_kolkata_london_new_york()
    {
        var list = new WorldClockList();
        Assert.Equal(new[] { "Asia/Kolkata", "Europe/London", "America/New_York" }, list.Ids);
    }

    [Fact]
    public void Adds_valid_zones_and_rejects_duplicates_case_insensitively()
    {
        var list = new WorldClockList();
        Assert.True(list.TryAdd("Asia/Tokyo", out _));
        Assert.False(list.TryAdd("asia/tokyo", out string msg));
        Assert.Contains("already", msg);
    }

    [Theory]
    [InlineData("Mars/Olympus")]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_unknown_zones(string id) => Assert.False(new WorldClockList().TryAdd(id, out _));

    [Fact]
    public void Enforces_the_maximum()
    {
        var list = new WorldClockList(new[] { "Asia/Kolkata" });
        string[] more = { "Europe/London", "America/New_York", "Asia/Tokyo", "Europe/Paris", "Australia/Sydney", "America/Chicago", "Asia/Dubai" };
        foreach (var z in more) Assert.True(list.TryAdd(z, out _));
        Assert.Equal(WorldClockList.MaxClocks, list.Ids.Count);
        Assert.False(list.TryAdd("Africa/Cairo", out string msg));
        Assert.Contains("up to", msg);
    }

    [Fact]
    public void Removes_clocks()
    {
        var list = new WorldClockList();
        Assert.True(list.Remove("europe/london"));
        Assert.False(list.Remove("Europe/London"));
        Assert.Equal(2, list.Ids.Count);
    }

    [Fact]
    public void Falls_back_to_defaults_when_given_nothing_usable()
    {
        Assert.Equal(WorldClockList.DefaultIds, new WorldClockList(new[] { "bogus", "" }).Ids);
    }

    [Fact]
    public void Formats_local_times_for_each_zone()
    {
        var list = new WorldClockList(new[] { "Asia/Kolkata", "America/New_York" });
        var now = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero); // Saturday
        var lines = list.Format(now);
        Assert.Equal("Kolkata  Sat 17:30", lines[0]);
        Assert.Equal("New York  Sat 08:00", lines[1]); // EDT in force until 1 Nov 2026
    }

    [Theory]
    [InlineData("America/New_York", "New York")]
    [InlineData("Asia/Kolkata", "Kolkata")]
    [InlineData("UTC", "UTC")]
    public void Labels_use_the_city_part(string id, string label) => Assert.Equal(label, WorldClockList.Label(id));
}
