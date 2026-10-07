using WinInstagram.Models;

namespace WinInstagram.Services;

/// <summary>
/// Verbosity-aware speech. Everything the app says goes through here so that a user can choose
/// how chatty it is, without each feature re-implementing that decision.
///
/// The three levels map onto the three reasons WinInstagram speaks:
/// <list type="bullet">
/// <item><see cref="Say"/> — essentials and the outcome of your own actions. Always spoken.</item>
/// <item><see cref="Detail"/> — useful context such as like counts and audio. Standard and up.</item>
/// <item><see cref="Extra"/> — read-through captions and diagnostics. Verbose only.</item>
/// </list>
/// </summary>
public static class Announcements
{
    /// <summary>
    /// Starts at the saved preference rather than a hard-coded default, so nothing can speak at the
    /// wrong level because a start-up step happened to run in a different order.
    /// </summary>
    private static VerbosityLevel _verbosity = AppSettingsService.Instance.Settings.Verbosity;

    public static VerbosityLevel Verbosity
    {
        get => _verbosity;
        set => _verbosity = value;
    }

    /// <summary>Always spoken: navigation results, action outcomes and errors.</summary>
    public static void Say(string message) => AccessibilityHelper.Announce(message);

    /// <summary>Spoken at Standard and Verbose. Use for context that is often wanted but not vital.</summary>
    public static void Detail(string message)
    {
        if (Verbosity >= VerbosityLevel.Standard) AccessibilityHelper.Announce(message);
    }

    /// <summary>Spoken only at Verbose. Use for long or situational text such as full captions.</summary>
    public static void Extra(string message)
    {
        if (Verbosity >= VerbosityLevel.Verbose) AccessibilityHelper.Announce(message);
    }
}
