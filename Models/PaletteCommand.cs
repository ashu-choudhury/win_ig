namespace WinInstagram.Models;

/// <summary>
/// Stable identifiers for everything the command palette can run. The palette view model lists
/// them and the shell executes them, so the ids are the contract between the two.
/// </summary>
public static class PaletteCommandIds
{
    // Panels
    public const string OpenHomePanel = "panel.home";
    public const string OpenReelsPanel = "panel.reels";
    public const string OpenMessagesPanel = "panel.messages";
    public const string OpenStoriesPanel = "panel.stories";
    public const string OpenProfilePanel = "panel.profile";
    public const string OpenSearchPanel = "panel.search";
    public const string OpenSavedPanel = "panel.saved";
    public const string OpenActivityPanel = "panel.activity";
    public const string TogglePanel = "panel.toggle";

    // Playback and actions
    public const string Refresh = "action.refresh";
    public const string NextReel = "action.nextReel";
    public const string PreviousReel = "action.prevReel";
    public const string PlayPause = "action.playPause";
    public const string Mute = "action.mute";
    public const string Like = "action.like";
    public const string Comments = "action.comments";
    public const string Share = "action.share";
    public const string SaveMedia = "action.saveMedia";

    // Reading and diagnostics
    public const string ReadCaptions = "read.captions";
    public const string ReadAltText = "read.altText";
    public const string ReadDescription = "read.description";
    public const string CheckControls = "read.checkControls";

    // Settings
    public const string CycleVerbosity = "settings.cycleVerbosity";
    public const string Settings = "settings.open";
    public const string ShortcutHelp = "settings.shortcutHelp";
    public const string CheckUpdates = "settings.checkUpdates";
}

/// <summary>One runnable row in the Ctrl+K command palette.</summary>
public class PaletteCommand
{
    public PaletteCommand(string id, string label, string description, string gesture = "")
    {
        Id = id;
        Label = label;
        Description = description;
        Gesture = gesture;
    }

    public string Id { get; }
    public string Label { get; }
    public string Description { get; }

    /// <summary>The gesture currently bound to this command, if it has one.</summary>
    public string Gesture { get; }

    public bool HasGesture => !string.IsNullOrWhiteSpace(Gesture);

    /// <summary>Which group the row belongs to, shown so a long list stays navigable.</summary>
    public string Group => Id.StartsWith("panel.") ? "Panels"
        : Id.StartsWith("read.") ? "Reading"
        : Id.StartsWith("settings.") ? "Settings"
        : "Actions";

    public string AccessibleDescription
    {
        get
        {
            var gesturePart = HasGesture ? $" Shortcut {Services.ShortcutService.DescribeGesture(Gesture)}." : string.Empty;
            return $"{Label}. {Description}.{gesturePart} Press Enter to run.";
        }
    }
}
