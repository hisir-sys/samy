namespace Samy.Core;

public enum PomodoroPhase { Focus, ShortBreak, LongBreak }

public enum PomodoroTickResult { None, PhaseCompleted }

public sealed record PomodoroSettings(
    int FocusMinutes = 25,
    int ShortBreakMinutes = 5,
    int LongBreakMinutes = 15,
    int SessionsBeforeLongBreak = 4,
    bool AutoAdvance = true)
{
    /// <summary>Returns a copy with every value forced into a sane range.</summary>
    public PomodoroSettings Clamped() => new(
        Math.Clamp(FocusMinutes, 1, 180),
        Math.Clamp(ShortBreakMinutes, 1, 60),
        Math.Clamp(LongBreakMinutes, 1, 120),
        Math.Clamp(SessionsBeforeLongBreak, 2, 12),
        AutoAdvance);
}

/// <summary>
/// Pomodoro cycle state machine. The host calls <see cref="Tick"/> once per second.
/// Focus -> short break, repeated; after N focus sessions the break is a long one.
/// </summary>
public sealed class PomodoroEngine
{
    private PomodoroSettings _settings;

    public PomodoroEngine(PomodoroSettings? settings = null)
    {
        _settings = (settings ?? new PomodoroSettings()).Clamped();
        RemainingSeconds = DurationFor(PomodoroPhase.Focus);
    }

    public PomodoroSettings Settings => _settings;
    public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Focus;
    public int RemainingSeconds { get; private set; }
    public bool IsRunning { get; private set; }
    public int CompletedFocusSessions { get; private set; }

    /// <summary>Focus sessions completed since the last long break (0..SessionsBeforeLongBreak-1).</summary>
    public int SessionsInCurrentCycle => CompletedFocusSessions % _settings.SessionsBeforeLongBreak;

    public void Start() { if (RemainingSeconds > 0) IsRunning = true; }
    public void Pause() => IsRunning = false;

    public void Reset()
    {
        IsRunning = false;
        Phase = PomodoroPhase.Focus;
        CompletedFocusSessions = 0;
        RemainingSeconds = DurationFor(Phase);
    }

    /// <summary>Applies new settings. A phase that has not started keeps its full length under the new setting.</summary>
    public void Configure(PomodoroSettings settings)
    {
        bool untouched = RemainingSeconds == DurationFor(Phase);
        _settings = settings.Clamped();
        if (untouched || RemainingSeconds > DurationFor(Phase)) RemainingSeconds = DurationFor(Phase);
    }

    /// <summary>Moves to the next phase immediately. A skipped focus session is not counted and always leads to a short break.</summary>
    public void Skip() => Advance(natural: false);

    public PomodoroTickResult Tick()
    {
        if (!IsRunning) return PomodoroTickResult.None;
        RemainingSeconds = Math.Max(0, RemainingSeconds - 1);
        if (RemainingSeconds > 0) return PomodoroTickResult.None;

        Advance(natural: true);
        return PomodoroTickResult.PhaseCompleted;
    }

    private void Advance(bool natural)
    {
        if (Phase == PomodoroPhase.Focus)
        {
            if (natural) CompletedFocusSessions++;
            bool longBreak = natural && CompletedFocusSessions % _settings.SessionsBeforeLongBreak == 0;
            Phase = longBreak ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
        }
        else
        {
            Phase = PomodoroPhase.Focus;
        }
        RemainingSeconds = DurationFor(Phase);
        // Only a phase that ran out by itself may auto-start the next one; a manual skip stops the clock.
        IsRunning = natural && _settings.AutoAdvance;
    }

    private int DurationFor(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Focus => _settings.FocusMinutes * 60,
        PomodoroPhase.ShortBreak => _settings.ShortBreakMinutes * 60,
        _ => _settings.LongBreakMinutes * 60,
    };

    public static string Label(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Focus => "Focus",
        PomodoroPhase.ShortBreak => "Short break",
        _ => "Long break",
    };
}
