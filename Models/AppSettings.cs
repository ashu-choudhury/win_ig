namespace WinInstagram.Models;

/// <summary>How much WinInstagram volunteers when it speaks.</summary>
public enum VerbosityLevel
{
    /// <summary>Only the essentials: what you navigated to, and the outcome of your actions.</summary>
    Terse = 0,

    /// <summary>Essentials plus useful detail such as like counts and audio titles.</summary>
    Standard = 1,

    /// <summary>Everything available, including read-through captions and selector diagnostics.</summary>
    Verbose = 2
}

/// <summary>
/// User preferences, persisted to %AppData%\WinInstagram\settings.json so that volume, panel
/// layout, reading position and shortcut choices survive a restart.
/// </summary>
public class AppSettings
{
    public double Volume { get; set; } = 1.0;
    public bool IsMuted { get; set; }
    public bool Autoplay { get; set; } = true;

    /// <summary>How much is spoken. See <see cref="VerbosityLevel"/>.</summary>
    public VerbosityLevel Verbosity { get; set; } = VerbosityLevel.Standard;

    public double PanelWidth { get; set; } = 520;

    /// <summary>Tab to reopen on launch (Home, Reels, Messages, Engine, Search, Settings).</summary>
    public string LastTab { get; set; } = string.Empty;

    // ---- reading position ----
    public string LastReadPostId { get; set; } = string.Empty;
    public string LastReadPostCode { get; set; } = string.Empty;
    public string LastReadReelUrl { get; set; } = string.Empty;
    public string LastThreadId { get; set; } = string.Empty;

    // ---- behaviour ----
    /// <summary>Pause story auto-advance by default, which is what screen reader users want.</summary>
    public bool PauseStoriesByDefault { get; set; } = true;

    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Warn out loud when Instagram's page changes enough to break a control.</summary>
    public bool AnnounceSelectorWarnings { get; set; } = true;

    /// <summary>Folder for "Save media to disk"; empty means Pictures\WinInstagram.</summary>
    public string MediaSaveFolder { get; set; } = string.Empty;

    /// <summary>
    /// Overrides for the keyboard map: action id (for example "nextReel") to a gesture string
    /// such as "J" or "Ctrl+J". Only overridden actions are stored.
    /// </summary>
    public Dictionary<string, string> Shortcuts { get; set; } = new();
}
