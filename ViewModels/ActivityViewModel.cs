using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinInstagram.Models;
using WinInstagram.Services;

namespace WinInstagram.ViewModels;

/// <summary>
/// Backs the native activity panel. Instagram's own activity feed groups actions into a wall of
/// avatars, which is unreadable without sight, so every entry arrives here as a sentence.
///
/// Reading is left to UI Automation: each list item exposes its full description as an accessible
/// name, so a screen reader speaks a row as the user arrows through the list. Live-region
/// announcements are reserved for arrivals that happen while the panel is open.
/// </summary>
public class ActivityViewModel : INotifyPropertyChanged
{
    private ActivityItem? _selectedItem;
    private string _status = "Press R to load your recent activity. New activity is announced as it arrives.";
    private bool _isActive;
    private int _newCount;

    /// <summary>Set by an explicit refresh so its result is spoken even while the panel is hidden.</summary>
    private bool _announceArrivalsEvenWhenHidden;

    public ObservableCollection<ActivityItem> Items { get; } = new();

    /// <summary>True while the Activity panel is on screen, so a hidden view never speaks.</summary>
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
    }

    public ActivityItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem == value) return;
            _selectedItem = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnPropertyChanged(); } }
    }

    public string HeaderText => $"📣 Activity ({Items.Count})";

    public int NewCount
    {
        get => _newCount;
        private set
        {
            if (_newCount == value) return;
            _newCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NewCountText));
        }
    }

    public string NewCountText => NewCount == 0 ? "No new activity" : $"{NewCount} new";

    public bool HasItems => Items.Count > 0;
    public bool HasNoItems => Items.Count == 0;

    /// <summary>Raised when the user asks to see an entry; the shell opens that URL in the engine.</summary>
    public event Action<string>? OpenItemRequested;

    public ActivityViewModel()
    {
        InstagramBridgeService.Instance.ActivityReceived += OnActivityReceived;
    }

    /// <summary>
    /// Loads the activity feed. <paramref name="announceNew"/> exists because the background poll
    /// should stay quiet, while an explicit refresh or a newly opened panel may speak.
    /// </summary>
    public async Task RefreshAsync(bool announceNew)
    {
        _announceArrivalsEvenWhenHidden = announceNew;
        Status = "Loading your activity...";
        await InstagramBridgeService.Instance.RequestActivityAsync();
    }

    private void OnActivityReceived(List<ActivityItem> incoming)
    {
        // Consumed once: the refresh that asked for this payload decides whether it may speak.
        var speakEvenWhenHidden = _announceArrivalsEvenWhenHidden;
        _announceArrivalsEvenWhenHidden = false;

        var arrivals = new List<ActivityItem>();

        foreach (var item in incoming)
        {
            var existing = !string.IsNullOrWhiteSpace(item.Id)
                ? Items.FirstOrDefault(x => x.Id == item.Id)
                : null;

            if (existing != null)
            {
                // Refresh the readable fields but never re-announce something already listed.
                existing.Username = item.Username;
                existing.Text = item.Text;
                existing.Timestamp = item.Timestamp;
                existing.MediaCode = item.MediaCode;
                existing.Type = item.Type;
                continue;
            }

            item.IsNew = true;
            arrivals.Add(item);
        }

        // Newest arrivals go to the top, in the order Instagram listed them.
        for (var i = arrivals.Count - 1; i >= 0; i--)
        {
            Items.Insert(0, arrivals[i]);
        }

        var added = arrivals.Count;

        NewCount = Items.Count(i => i.IsNew);

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasNoItems));
        OnPropertyChanged(nameof(HeaderText));

        if (SelectedItem == null && Items.Count > 0)
        {
            SelectedItem = Items[0];
        }

        Status = Items.Count == 0
            ? "No activity was found. Instagram returned no entries."
            : $"{Items.Count} activity entries. {NewCount} new.";

        AppLogger.Success("UI", $"ActivityViewModel updated with {Items.Count} entries ({added} new).");

        if (added > 0 && (IsActive || speakEvenWhenHidden))
        {
            Status = $"{Items.Count} activity entries. {NewCount} new. Use Up and Down arrows to read them.";
            var newest = Items[0];
            var message = added == 1
                ? $"1 new activity. {newest.Username} {newest.TypeText.ToLowerInvariant()}."
                : $"{added} new activity entries. {Items.Count} entries in total.";

            // An explicit refresh speaks even with the panel hidden; the background poll does not,
            // because it passes announceNew: false.
            if (IsActive) AnnounceIfActive(message);
            else Announcements.Detail(message);
        }
    }

    /// <summary>Clears the new markers once the user has heard them.</summary>
    public void MarkAllSeen()
    {
        foreach (var item in Items) item.IsNew = false;
        NewCount = 0;
        Status = $"{Items.Count} activity entries. All marked as read.";
        AnnounceIfActive("All activity marked as read.");
    }

    /// <summary>Opens the selected entry's target in the engine.</summary>
    public void OpenSelected()
    {
        var item = SelectedItem;
        if (item == null)
        {
            AnnounceIfActive("No activity entry selected.");
            return;
        }

        if (string.IsNullOrWhiteSpace(item.OpenUrl))
        {
            AnnounceIfActive("This activity has nothing to open.");
            return;
        }

        OpenItemRequested?.Invoke(item.OpenUrl);
    }

    private void AnnounceIfActive(string message)
    {
        if (_isActive) Announcements.Say(message);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
