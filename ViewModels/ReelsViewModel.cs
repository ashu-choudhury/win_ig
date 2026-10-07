using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinInstagram.Models;
using WinInstagram.Services;

namespace WinInstagram.ViewModels;

/// <summary>
/// Backs the native Reels panel: live details of the reel currently playing in the engine,
/// plus a navigable history of reels you have already watched. Playback actions themselves
/// are owned by the shell so there is exactly one place that talks to the engine and one
/// place that speaks to the user.
/// </summary>
public class ReelsViewModel : INotifyPropertyChanged
{
    private const int MaxHistory = 100;

    private ReelItem? _currentReel;
    private ReelItem? _selectedHistoryReel;
    private bool _isMuted;
    private bool _isPlaying = true;
    private double _volume = 1.0;
    private bool _isActive;
    private string _statusMessage = "Ready. Press Down Arrow or J for the next reel.";

    /// <summary>Reels seen this session, most recently watched first.</summary>
    public ObservableCollection<ReelItem> ViewedReels { get; } = new();

    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
    }

    public ReelItem? CurrentReel
    {
        get => _currentReel;
        set
        {
            if (_currentReel == value) return;
            _currentReel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCurrentReel));
            OnPropertyChanged(nameof(CurrentReelLikesText));
            OnPropertyChanged(nameof(CurrentReelCommentsText));
            OnPropertyChanged(nameof(CurrentReelCaption));
        }
    }

    public bool HasCurrentReel => _currentReel != null;

    /// <summary>
    /// The history entry the user has arrowed to. Its full description is exposed as the list
    /// item's accessible name, so UI Automation speaks it and no live region is needed here.
    /// </summary>
    public ReelItem? SelectedHistoryReel
    {
        get => _selectedHistoryReel;
        set
        {
            if (_selectedHistoryReel == value) return;
            _selectedHistoryReel = value;
            OnPropertyChanged();
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted == value) return;
            _isMuted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MuteButtonText));
        }
    }

    public string MuteButtonText => IsMuted ? "🔇 Unmute (M)" : "🔊 Mute (M)";

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlayButtonText));
            OnPropertyChanged(nameof(PlaybackStatusText));
        }
    }

    public string PlayButtonText => IsPlaying ? "⏸ Pause (Space)" : "▶ Play (Space)";
    public string PlaybackStatusText => IsPlaying ? "▶ Playing" : "⏸ Paused";

    public double Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_volume - clamped) < 0.01) return;
            _volume = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeText));
            _ = InstagramBridgeService.Instance.SetVolumeAsync(_volume);
            if (_isActive) AccessibilityHelper.Announce($"Volume {(int)(_volume * 100)} percent");
        }
    }

    public string VolumeText => $"Volume: {(int)(_volume * 100)}%";

    public string CurrentReelLikesText =>
        CurrentReel == null ? string.Empty
        : string.IsNullOrWhiteSpace(CurrentReel.FormattedLikes) ? "Likes unavailable" : $"{CurrentReel.FormattedLikes} likes";

    public string CurrentReelCommentsText =>
        CurrentReel == null ? string.Empty
        : CurrentReel.CommentsCount > 0 ? $"{CurrentReel.CommentsCount} comments" : "No comments counted";

    public string CurrentReelCaption =>
        CurrentReel == null ? "No reel playing yet."
        : string.IsNullOrWhiteSpace(CurrentReel.Caption) ? "This reel has no caption." : CurrentReel.Caption;

    public string StatusMessage
    {
        get => _statusMessage;
        set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
    }

    public ReelsViewModel()
    {
        InstagramBridgeService.Instance.ActiveReelChanged += OnActiveReelChanged;
        InstagramBridgeService.Instance.StatusMessageUpdated += (msg) => StatusMessage = msg;
    }

    private void OnActiveReelChanged(ReelItem reel, bool isPlaying, bool isMuted, double vol)
    {
        var isNew = CurrentReel == null || CurrentReel.Id != reel.Id;

        CurrentReel = reel;
        IsPlaying = isPlaying;
        IsMuted = isMuted;
        _volume = vol;
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(VolumeText));

        if (isNew)
        {
            // Keep one entry per reel, newest first, so re-reading does not repeat.
            var existing = ViewedReels.FirstOrDefault(r => r.Id == reel.Id);
            if (existing != null) ViewedReels.Remove(existing);
            ViewedReels.Insert(0, reel);
            while (ViewedReels.Count > MaxHistory) ViewedReels.RemoveAt(ViewedReels.Count - 1);

            StatusMessage = $"Reel by @{reel.Username}";
        }
        else
        {
            StatusMessage = isPlaying ? $"Playing reel by @{reel.Username}" : $"Paused reel by @{reel.Username}";
        }
    }

    public void IncreaseVolume() => Volume += 0.1;
    public void DecreaseVolume() => Volume -= 0.1;

    /// <summary>Re-opens the history entry the user is focused on.</summary>
    public string? GetSelectedHistoryUrl()
    {
        var reel = SelectedHistoryReel;
        if (reel == null || string.IsNullOrWhiteSpace(reel.Id)) return null;
        return reel.Id.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? reel.Id
            : $"https://www.instagram.com/reels/";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
