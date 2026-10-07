using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinInstagram.Models;
using WinInstagram.Services;

namespace WinInstagram.ViewModels;

/// <summary>
/// Backs the Ctrl+K command palette. Every native command is listed here with the gesture it is
/// currently bound to, so a user who has remapped their keys still sees the truth, and a user who
/// has forgotten a shortcut can find it by typing what they want to do.
/// </summary>
public class CommandPaletteViewModel : INotifyPropertyChanged
{
    private string _filter = string.Empty;
    private PaletteCommand? _selectedCommand;
    private string _statusText = string.Empty;

    public IReadOnlyList<PaletteCommand> AllCommands { get; }

    /// <summary>The rows currently shown after filtering.</summary>
    public ObservableCollection<PaletteCommand> Matches { get; } = new();

    public CommandPaletteViewModel()
    {
        AllCommands = BuildCommands();
        Reset();
    }

    public bool HasMatches => Matches.Count > 0;

    /// <summary>The row the user has arrowed to, or the first match before they move.</summary>
    public PaletteCommand? SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            if (ReferenceEquals(_selectedCommand, value)) return;
            _selectedCommand = value;
            OnPropertyChanged();
        }
    }

    public PaletteCommand? Selected => SelectedCommand;

    public string Filter
    {
        get => _filter;
        set
        {
            if (_filter == value) return;
            _filter = value ?? string.Empty;
            OnPropertyChanged();
            Refilter();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMatches));
        }
    }

    /// <summary>Clears the filter and puts the palette back at its opening state.</summary>
    public void Reset()
    {
        _filter = string.Empty;
        OnPropertyChanged(nameof(Filter));
        Refilter();
        SelectedCommand = Matches.FirstOrDefault();
    }

    /// <summary>
    /// Filters on label, description, group and id. Commands whose label starts with what has been
    /// typed are ranked first, so "sto" finds the stories navigator before anything that merely
    /// mentions the word.
    /// </summary>
    public void Refilter()
    {
        var query = (_filter ?? string.Empty).Trim();

        if (query.Length == 0)
        {
            Matches.Clear();
            foreach (var command in AllCommands) Matches.Add(command);
        }
        else
        {
            var prefixHits = new List<PaletteCommand>();
            var otherHits = new List<PaletteCommand>();

            foreach (var command in AllCommands)
            {
                if (StartsWithQuery(command, query)) prefixHits.Add(command);
                else if (ContainsQuery(command, query)) otherHits.Add(command);
            }

            Matches.Clear();
            foreach (var command in prefixHits) Matches.Add(command);
            foreach (var command in otherHits) Matches.Add(command);
        }

        if (SelectedCommand == null || !Matches.Contains(SelectedCommand))
        {
            SelectedCommand = Matches.FirstOrDefault();
        }

        StatusText = Matches.Count == 0
            ? "No command matches that filter."
            : $"{Matches.Count} of {AllCommands.Count} commands.";
    }

    /// <summary>Moves the selection within the visible rows, for arrow keys handled by the view.</summary>
    public void MoveSelection(int delta)
    {
        if (Matches.Count == 0)
        {
            SelectedCommand = null;
            return;
        }

        var current = SelectedCommand == null ? -1 : Matches.IndexOf(SelectedCommand);
        var next = Math.Clamp(current + delta, 0, Matches.Count - 1);
        SelectedCommand = Matches[next];
    }

    private static bool StartsWithQuery(PaletteCommand command, string query) =>
        command.Label.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
        command.Group.StartsWith(query, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsQuery(PaletteCommand command, string query) =>
        command.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        command.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        command.Group.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        command.Id.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>The gesture the user is actually using for an action, so the palette never lies about it.</summary>
    private static string Gesture(string actionId)
    {
        try
        {
            return ShortcutService.Instance.GetGesture(actionId);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<PaletteCommand> BuildCommands() => new List<PaletteCommand>
    {
        new(PaletteCommandIds.OpenHomePanel, "Home feed panel", "Show the native home feed list."),
        new(PaletteCommandIds.OpenReelsPanel, "Reels panel", "Show the native reels panel with reel details and your watch history."),
        new(PaletteCommandIds.OpenMessagesPanel, "Messages panel", "Show the native direct messages panel."),
        new(PaletteCommandIds.OpenStoriesPanel, "Stories navigator", "Show the pausable stories navigator so stories never auto-advance on you."),
        new(PaletteCommandIds.OpenProfilePanel, "Profile of the current creator", "Load and read the profile of whoever created the media on screen."),
        new(PaletteCommandIds.OpenSearchPanel, "Search panel", "Search Instagram natively for accounts, hashtags and posts."),
        new(PaletteCommandIds.OpenSavedPanel, "Saved posts panel", "Show your saved posts and your collections."),
        new(PaletteCommandIds.OpenActivityPanel, "Activity panel", "Show your recent likes, comments and follows as readable sentences."),
        new(PaletteCommandIds.TogglePanel, "Show or hide the panel", "Toggle the native panel so the web view fills the window.", Gesture("togglePanel")),

        new(PaletteCommandIds.Refresh, "Refresh", "Reload the current timeline or inbox.", Gesture("refresh")),
        new(PaletteCommandIds.NextReel, "Next reel", "Move to the next reel.", Gesture("nextReel")),
        new(PaletteCommandIds.PreviousReel, "Previous reel", "Move back to the previous reel.", Gesture("prevReel")),
        new(PaletteCommandIds.PlayPause, "Play or pause", "Toggle playback of the reel on screen.", Gesture("playPause")),
        new(PaletteCommandIds.Mute, "Mute or unmute", "Toggle the audio of the reel on screen.", Gesture("mute")),
        new(PaletteCommandIds.Like, "Like or unlike", "Toggle your like on the current post or reel.", Gesture("like")),
        new(PaletteCommandIds.Comments, "Comments", "Open or close the accessible comments drawer.", Gesture("comments")),
        new(PaletteCommandIds.Share, "Share", "Copy the caption, the creator and the link to the clipboard.", Gesture("share")),
        new(PaletteCommandIds.SaveMedia, "Save media to disk", "Save the media on screen and a text description of it to your Pictures folder.", Gesture("saveMedia")),

        new(PaletteCommandIds.ReadCaptions, "Read video captions", "Read the caption transcript of the video on screen."),
        new(PaletteCommandIds.ReadAltText, "Read image description", "Read Instagram's own alt text for the media on screen."),
        new(PaletteCommandIds.ReadDescription, "Re-read current media", "Speak the creator, caption, likes and like status of the current media again."),
        new(PaletteCommandIds.CheckControls, "Check controls", "Test whether the buttons and boxes this app drives are still present on the page."),

        new(PaletteCommandIds.CycleVerbosity, "Change how much is spoken", "Cycle between terse, standard and verbose announcements."),
        new(PaletteCommandIds.Settings, "Settings", "Open the settings panel."),
        new(PaletteCommandIds.ShortcutHelp, "Read your shortcuts", "Speak the keyboard shortcuts that are currently in use."),
        new(PaletteCommandIds.CheckUpdates, "Check for updates", "Look for a newer WinInstagram release on GitHub.")
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
