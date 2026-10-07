using System.Windows.Input;
using WinInstagram.Models;

namespace WinInstagram.Services;

/// <summary>
/// A single remappable action. The scope names the surface the action belongs to, because two
/// different surfaces can share a key on purpose; J is the next reel and the next story, and only
/// one of those panels is ever on screen at a time.
/// </summary>
public class ShortcutDefinition
{
    public const string GlobalScope = "Global";

    public ShortcutDefinition(string id, string label, string defaultGesture, string description, string scope = GlobalScope)
    {
        Id = id;
        Label = label;
        DefaultGesture = defaultGesture;
        Description = description;
        Scope = scope;
    }

    public string Id { get; }
    public string Label { get; }
    public string DefaultGesture { get; }
    public string Description { get; }

    /// <summary>Which surface the action belongs to; see the class summary.</summary>
    public string Scope { get; }
}

/// <summary>
/// The keyboard map. Actions have defaults matching the documented shortcuts, and users can
/// override any of them; overrides live in <see cref="AppSettings.Shortcuts"/> so they persist.
///
/// Two reasons this exists rather than hard-coded keys: screen readers (NVDA, JAWS, Narrator)
/// latch onto single letters, and every user has different habits. A user can also switch to a
/// modified-key preset that leaves plain letters free for their screen reader.
/// </summary>
public class ShortcutService
{
    private static ShortcutService? _instance;
    public static ShortcutService Instance => _instance ??= new ShortcutService();

    /// <summary>True when plain letter shortcuts are active; false uses the modified-key preset.</summary>
    public bool SingleLetterShortcuts { get; private set; } = true;

    private readonly List<ShortcutDefinition> _definitions = new()
    {
        new("nextReel",    "Next reel",              "J",   "Moves to the next reel", "Reels"),
        new("prevReel",    "Previous reel",          "K",   "Moves to the previous reel", "Reels"),
        new("playPause",   "Play or pause reel",     "Space", "Toggles playback", "Reels"),
        new("mute",        "Mute or unmute",         "M",   "Toggles audio", "Reels"),
        new("like",        "Like or unlike",         "L",   "Likes the current post or reel"),
        new("comments",    "Comments",               "C",   "Opens the comments drawer"),
        new("share",       "Share",                  "S",   "Copies the link"),
        new("refresh",     "Refresh",                "R",   "Reloads the timeline or inbox"),
        new("palette",     "Command palette",        "Ctrl+K", "Opens the command palette"),
        new("togglePanel", "Show or hide the panel", "F6",  "Toggles the native panel"),
        new("saveMedia",   "Save media to disk",     "Ctrl+S", "Saves the current media and its text"),
        new("storyNext",   "Next story",             "J",   "Steps to the next story item", "Stories"),
        new("storyPrev",   "Previous story",         "K",   "Steps back to the previous story item", "Stories"),
        new("storyPause",  "Pause or resume stories", "P",  "Holds story auto-advance still", "Stories"),
        new("captions",    "Read video captions",    "T",   "Reads out the video's own caption track"),
        new("describe",    "Describe what is on screen", "D", "Reads out the image description and caption"),
    };

    /// <summary>Replacement gestures used when plain letters must stay free for a screen reader.</summary>
    private static readonly Dictionary<string, string> ModifiedPreset = new()
    {
        ["nextReel"] = "Ctrl+Down",
        ["prevReel"] = "Ctrl+Up",
        ["playPause"] = "Ctrl+Space",
        ["mute"] = "Ctrl+M",
        ["like"] = "Ctrl+Shift+L",
        ["comments"] = "Ctrl+Shift+C",
        ["share"] = "Ctrl+Shift+S",
        ["refresh"] = "Ctrl+R",
        ["storyNext"] = "Ctrl+Right",
        ["storyPrev"] = "Ctrl+Left",
        ["storyPause"] = "Ctrl+P",
        ["captions"] = "Ctrl+Shift+T",
        ["describe"] = "Ctrl+Shift+D",
    };

    private ShortcutService() { }

    public IReadOnlyList<ShortcutDefinition> Definitions => _definitions;

    /// <summary>Loads the persisted map. Call once at startup.</summary>
    public void LoadFromSettings(AppSettings settings)
    {
        SingleLetterShortcuts = !IsModifiedPresetApplied(settings);
    }

    public string GetGesture(string actionId)
    {
        var settings = AppSettingsService.Instance.Settings;
        if (settings.Shortcuts.TryGetValue(actionId, out var gesture) && !string.IsNullOrWhiteSpace(gesture))
        {
            return gesture;
        }
        return _definitions.FirstOrDefault(d => d.Id == actionId)?.DefaultGesture ?? string.Empty;
    }

    /// <summary>True when the pressed keys match the gesture currently bound to an action.</summary>
    public bool Matches(Key key, ModifierKeys modifiers, string actionId)
    {
        var gesture = GetGesture(actionId);
        if (string.IsNullOrWhiteSpace(gesture)) return false;

        var (gestureKey, gestureMods) = ParseGesture(gesture);
        if (gestureKey == Key.None || gestureKey != key) return false;

        // Compare only the modifiers we actually bind, so a stray NumLock or Shift does not block.
        return (modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == gestureMods;
    }

    /// <summary>Binds an action to whatever key combination was just pressed.</summary>
    public void SetGesture(string actionId, Key key, ModifierKeys modifiers)
    {
        var gesture = FormatGesture(key, modifiers);
        if (string.IsNullOrWhiteSpace(gesture)) return;

        AppSettingsService.Instance.Update(s => s.Shortcuts[actionId] = gesture);
        AppLogger.Info("KEYS", $"Bound {actionId} to {gesture}");
    }

    /// <summary>Switches to the modified-key preset so plain letters stay free for a screen reader.</summary>
    public void UseModifiedKeys()
    {
        AppSettingsService.Instance.Update(s =>
        {
            foreach (var (actionId, gesture) in ModifiedPreset)
            {
                s.Shortcuts[actionId] = gesture;
            }
        });
        SingleLetterShortcuts = false;
    }

    /// <summary>Returns every action back to its documented default.</summary>
    public void ResetToDefaults()
    {
        AppSettingsService.Instance.Update(s => s.Shortcuts.Clear());
        SingleLetterShortcuts = true;
        AppLogger.Info("KEYS", "Keyboard map reset to defaults.");
    }

    /// <summary>
    /// Actions that genuinely clash, so the user can be warned. Actions in different scopes may
    /// share a key without clashing, because those panels are never on screen together.
    /// </summary>
    public List<string> FindConflicts()
    {
        var conflicts = new List<string>();

        foreach (var group in _definitions
                     .GroupBy(d => GetGesture(d.Id), StringComparer.OrdinalIgnoreCase)
                     .Where(g => !string.IsNullOrWhiteSpace(g.Key)))
        {
            var members = group.ToList();
            if (members.Count < 2) continue;

            var clashing = members
                .Where(a => members.Any(b => !ReferenceEquals(a, b) && Clash(a, b)))
                .ToList();

            if (clashing.Count > 1)
            {
                conflicts.Add($"{group.Key} is bound to {string.Join(" and ", clashing.Select(d => d.Label))}");
            }
        }

        return conflicts;
    }

    /// <summary>Two actions clash when they apply to the same surface, or when either is global.</summary>
    private static bool Clash(ShortcutDefinition a, ShortcutDefinition b) =>
        string.Equals(a.Scope, b.Scope, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(a.Scope, ShortcutDefinition.GlobalScope, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(b.Scope, ShortcutDefinition.GlobalScope, StringComparison.OrdinalIgnoreCase);

    private bool IsModifiedPresetApplied(AppSettings settings)
    {
        var effective = _definitions.Select(d =>
            settings.Shortcuts.TryGetValue(d.Id, out var g) ? g : d.DefaultGesture).ToList();

        var expected = _definitions.Select(d =>
            ModifiedPreset.TryGetValue(d.Id, out var g) ? g : d.DefaultGesture).ToList();

        return effective.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase);
    }

    public static (Key key, ModifierKeys modifiers) ParseGesture(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return (Key.None, ModifierKeys.None);

        var modifiers = ModifierKeys.None;
        Key key = Key.None;

        foreach (var part in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= ModifierKeys.Control; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                default:
                    if (Enum.TryParse<Key>(part, ignoreCase: true, out var parsed)) key = parsed;
                    break;
            }
        }

        return (key, modifiers);
    }

    public static string FormatGesture(Key key, ModifierKeys modifiers)
    {
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.System or Key.LWin or Key.RWin)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>Human readable description for a gesture, for settings and announcements.</summary>
    public static string DescribeGesture(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return "unbound";
        var (key, mods) = ParseGesture(gesture);
        if (key == Key.None) return gesture;

        var parts = new List<string>();
        if ((mods & ModifierKeys.Control) != 0) parts.Add("Control");
        if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");

        var name = key switch
        {
            Key.D1 or Key.NumPad1 => "1",
            Key.D2 or Key.NumPad2 => "2",
            Key.D3 or Key.NumPad3 => "3",
            Key.D4 or Key.NumPad4 => "4",
            Key.D5 or Key.NumPad5 => "5",
            Key.OemPlus or Key.Add => "Plus",
            Key.OemMinus or Key.Subtract => "Minus",
            _ => key.ToString()
        };
        parts.Add(name);
        return string.Join(" plus ", parts);
    }
}
