using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WinInstagram.Models;
using WinInstagram.Services;

namespace WinInstagram.ViewModels;

/// <summary>
/// One rebindable action as shown in the settings list. The gesture text is mutable because the
/// user can change it in place while the list stays on screen.
/// </summary>
public class ShortcutRow : INotifyPropertyChanged
{
    private string _gestureText;
    private bool _isCapturing;

    public ShortcutRow(string id, string label, string description, string gestureText)
    {
        Id = id;
        Label = label;
        Description = description;
        _gestureText = gestureText;
    }

    public string Id { get; }
    public string Label { get; }
    public string Description { get; }

    public string GestureText
    {
        get => _gestureText;
        set
        {
            if (_gestureText == value) return;
            _gestureText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayGesture));
            OnPropertyChanged(nameof(AccessibleDescription));
        }
    }

    /// <summary>Spoken form of the bound gesture, for example "Control plus K".</summary>
    public string DisplayGesture => string.IsNullOrWhiteSpace(_gestureText)
        ? "unbound"
        : ShortcutService.DescribeGesture(_gestureText);

    /// <summary>True while this row is waiting for the user to press a new key combination.</summary>
    public bool IsCapturing
    {
        get => _isCapturing;
        set
        {
            if (_isCapturing == value) return;
            _isCapturing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AccessibleDescription));
        }
    }

    public string AccessibleDescription => IsCapturing
        ? $"{Label}. Press the new key combination now, or Escape to cancel."
        : $"Action {Label}. {Description}. Current shortcut {DisplayGesture}. Press Enter to change it.";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Backs the native settings panel: how much the app says, how stories and activity behave, and
/// the keyboard map. Every change is written straight to <see cref="AppSettingsService"/>, so a
/// preference survives a restart the moment it is chosen, and <see cref="SettingsChanged"/> lets
/// the shell apply it to live behaviour immediately.
/// </summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    private int _verbosityIndex = 1;
    private bool _pauseStoriesByDefault = true;
    private bool _notificationsEnabled = true;
    private bool _announceSelectorWarnings = true;
    private bool _singleLetterShortcuts = true;
    private string _saveFolder = string.Empty;
    private string _statusMessage = string.Empty;

    /// <summary>Guards the setters while the panel is loading, so reading never writes.</summary>
    private bool _isLoading;

    public ObservableCollection<ShortcutRow> Shortcuts { get; } = new();

    /// <summary>Raised after anything is persisted, so the shell can apply it without a restart.</summary>
    public event Action? SettingsChanged;

    /// <summary>Raised when the user asks the shell to open a folder picker.</summary>
    public event Action? ChooseSaveFolderRequested;

    public SettingsViewModel()
    {
        Refresh();
    }

    // ---- speech -----------------------------------------------------------------------------

    /// <summary>0 Terse, 1 Standard, 2 Verbose. A negative value is ignored so the ComboBox
    /// clearing its selection during startup can never write a preference.</summary>
    public int VerbosityIndex
    {
        get => _verbosityIndex;
        set
        {
            if (_isLoading || value < 0) return;
            var clamped = Math.Clamp(value, 0, 2);
            if (_verbosityIndex == clamped) return;
            _verbosityIndex = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VerbosityHelp));
            Persist(s => s.Verbosity = (VerbosityLevel)clamped, $"Announcements are now {VerbosityName(clamped)}.");
        }
    }

    public string VerbosityHelp => _verbosityIndex switch
    {
        0 => "Terse: only what you navigated to and the outcome of your own actions is spoken.",
        1 => "Standard: adds useful detail such as like counts, audio titles and how many items were found.",
        _ => "Verbose: adds read-through captions, image descriptions and control diagnostics."
    };

    // ---- behaviour --------------------------------------------------------------------------

    public bool PauseStoriesByDefault
    {
        get => _pauseStoriesByDefault;
        set
        {
            if (_isLoading || _pauseStoriesByDefault == value) return;
            _pauseStoriesByDefault = value;
            OnPropertyChanged();
            Persist(s => s.PauseStoriesByDefault = value,
                value ? "Stories will wait for you by default." : "Stories will auto-advance until you pause them.");
        }
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set
        {
            if (_isLoading || _notificationsEnabled == value) return;
            _notificationsEnabled = value;
            OnPropertyChanged();
            Persist(s => s.NotificationsEnabled = value,
                value ? "New activity will be announced." : "New activity will no longer be announced.");
        }
    }

    public bool AnnounceSelectorWarnings
    {
        get => _announceSelectorWarnings;
        set
        {
            if (_isLoading || _announceSelectorWarnings == value) return;
            _announceSelectorWarnings = value;
            OnPropertyChanged();
            Persist(s => s.AnnounceSelectorWarnings = value,
                value ? "You will be warned when a control on the page stops working." : "Control warnings are off.");
        }
    }

    // ---- keyboard ---------------------------------------------------------------------------

    public bool SingleLetterShortcuts
    {
        get => _singleLetterShortcuts;
        private set
        {
            if (_singleLetterShortcuts == value) return;
            _singleLetterShortcuts = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShortcutModeText));
        }
    }

    public string ShortcutModeText => _singleLetterShortcuts
        ? "Single letter shortcuts are active (J, K, L, M, C, S, R). A screen reader may latch onto these letters."
        : "Screen reader friendly shortcuts are active; plain letters stay free for your screen reader.";

    public string ConflictText
    {
        get
        {
            var conflicts = ShortcutService.Instance.FindConflicts();
            return conflicts.Count == 0
                ? "No two actions share a shortcut."
                : "Conflicts: " + string.Join("; ", conflicts);
        }
    }

    // ---- files ------------------------------------------------------------------------------

    public string SaveFolder => _saveFolder;

    public string SettingsFilePath => AppSettingsService.Instance.SettingsPath;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    // ---- loading ----------------------------------------------------------------------------

    /// <summary>Reloads everything from persisted settings. Reads only; it never writes.</summary>
    public void Refresh()
    {
        _isLoading = true;
        try
        {
            var settings = AppSettingsService.Instance.Settings;

            _verbosityIndex = Math.Clamp((int)settings.Verbosity, 0, 2);
            _pauseStoriesByDefault = settings.PauseStoriesByDefault;
            _notificationsEnabled = settings.NotificationsEnabled;
            _announceSelectorWarnings = settings.AnnounceSelectorWarnings;
            _singleLetterShortcuts = ShortcutService.Instance.SingleLetterShortcuts;
            _saveFolder = MediaSaveService.Instance.SaveFolder;

            Shortcuts.Clear();
            foreach (var definition in ShortcutService.Instance.Definitions)
            {
                Shortcuts.Add(new ShortcutRow(
                    definition.Id,
                    definition.Label,
                    definition.Description,
                    ShortcutService.Instance.GetGesture(definition.Id)));
            }
        }
        finally
        {
            _isLoading = false;
        }

        OnPropertyChanged(nameof(VerbosityIndex));
        OnPropertyChanged(nameof(VerbosityHelp));
        OnPropertyChanged(nameof(PauseStoriesByDefault));
        OnPropertyChanged(nameof(NotificationsEnabled));
        OnPropertyChanged(nameof(AnnounceSelectorWarnings));
        OnPropertyChanged(nameof(SingleLetterShortcuts));
        OnPropertyChanged(nameof(ShortcutModeText));
        OnPropertyChanged(nameof(ConflictText));
        OnPropertyChanged(nameof(SaveFolder));
        OnPropertyChanged(nameof(SettingsFilePath));
    }

    // ---- commands ---------------------------------------------------------------------------

    /// <summary>Switches every action to the Ctrl/Shift preset so plain letters stay free.</summary>
    public void UseModifiedKeys()
    {
        ShortcutService.Instance.UseModifiedKeys();
        Refresh();
        StatusMessage = "Screen reader friendly shortcuts applied. Plain letters are now free for your screen reader.";
        SettingsChanged?.Invoke();
    }

    /// <summary>Puts every action back to its documented default.</summary>
    public void ResetShortcuts()
    {
        ShortcutService.Instance.ResetToDefaults();
        Refresh();
        StatusMessage = "All shortcuts are back to their defaults.";
        SettingsChanged?.Invoke();
    }

    /// <summary>Puts one row into capture mode and tells the user what to do next.</summary>
    public void BeginCapture(string id)
    {
        var row = Shortcuts.FirstOrDefault(r => r.Id == id);
        if (row == null) return;

        StopCapture();
        row.IsCapturing = true;
        StatusMessage = $"Press the new key combination for {row.Label} now, or press Escape to cancel.";
    }

    /// <summary>Leaves every row as it was and says so.</summary>
    public void CancelCapture()
    {
        var row = Shortcuts.FirstOrDefault(r => r.IsCapturing);
        StopCapture();
        StatusMessage = row == null
            ? "Nothing was being rebound."
            : $"{row.Label} was left unchanged.";
    }

    /// <summary>Binds whatever was just pressed to the capturing row.</summary>
    public void CommitCapture(Key key, ModifierKeys modifiers)
    {
        var row = Shortcuts.FirstOrDefault(r => r.IsCapturing);
        if (row == null) return;

        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.System or Key.LWin or Key.RWin)
        {
            StatusMessage = "That is a modifier key on its own. Hold it together with a letter or number.";
            return;
        }

        var gesture = ShortcutService.FormatGesture(key, modifiers);
        if (string.IsNullOrWhiteSpace(gesture))
        {
            StatusMessage = "That key combination cannot be used. Try again, or press Escape to cancel.";
            return;
        }

        var label = row.Label;
        ShortcutService.Instance.SetGesture(row.Id, key, modifiers);
        Refresh();

        var conflicts = ShortcutService.Instance.FindConflicts();
        StatusMessage = conflicts.Count == 0
            ? $"{label} is now {ShortcutService.DescribeGesture(gesture)}."
            : $"{label} is now {ShortcutService.DescribeGesture(gesture)}. Warning: {string.Join("; ", conflicts)}.";
        SettingsChanged?.Invoke();
    }

    /// <summary>Persists a different folder for saved media.</summary>
    public void SetSaveFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;

        AppSettingsService.Instance.Update(s => s.MediaSaveFolder = folder);
        Refresh();
        StatusMessage = $"Media will be saved to {folder}.";
        SettingsChanged?.Invoke();
    }

    /// <summary>Asks the shell to show a folder picker for saved media.</summary>
    public void RequestChooseSaveFolder() => ChooseSaveFolderRequested?.Invoke();

    private void StopCapture()
    {
        foreach (var row in Shortcuts) row.IsCapturing = false;
    }

    private void Persist(Action<AppSettings> change, string statusMessage)
    {
        AppSettingsService.Instance.Update(change);
        StatusMessage = statusMessage;
        SettingsChanged?.Invoke();
    }

    private static string VerbosityName(int index) => index switch
    {
        0 => "Terse",
        1 => "Standard",
        _ => "Verbose"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
