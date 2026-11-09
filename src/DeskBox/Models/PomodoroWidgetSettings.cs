namespace DeskBox.Models;

/// <summary>
/// Pomodoro widget timing and completion preferences, persisted per widget
/// kind by the dedicated settings store.
/// </summary>
public sealed class PomodoroWidgetSettings
{
    /// <summary>Number of completed focus rounds before a long break.</summary>
    public int RoundCount { get; set; } = 4;

    /// <summary>Focus phase duration in minutes.</summary>
    public int FocusMinutes { get; set; } = 25;

    /// <summary>Short break duration in minutes.</summary>
    public int ShortBreakMinutes { get; set; } = 5;

    /// <summary>Long break duration in minutes.</summary>
    public int LongBreakMinutes { get; set; } = 15;

    /// <summary>Whether completing a phase plays a sound.</summary>
    public bool CompletionSoundEnabled { get; set; } = true;

    /// <summary>Whether completing a phase shows a system notification.</summary>
    public bool CompletionNotificationEnabled { get; set; } = true;
}
