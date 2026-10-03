using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinInstagram.Models;
using WinInstagram.Services;

namespace WinInstagram.ViewModels;

public class ReelsViewModel : INotifyPropertyChanged
{
    private ReelItem? _currentReel;
    private bool _isMuted = false;
    private double _volume = 1.0;
    private bool _isPlaying = true;
    private bool _isCommentsOpen = false;
    private string _statusMessage = "Ready. Press Down Arrow or J for next reel.";
    private string _lastAnnouncedReelId = string.Empty;

    public ReelItem? CurrentReel
    {
        get => _currentReel;
        set
        {
            if (_currentReel != value)
            {
                _currentReel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCurrentReel));
            }
        }
    }

    public bool HasCurrentReel => _currentReel != null;

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted != value)
            {
                _isMuted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MuteButtonText));
                AccessibilityHelper.Announce(_isMuted ? "Audio muted" : "Audio unmuted");
            }
        }
    }

    public string MuteButtonText => IsMuted ? "Unmute (M)" : "Mute (M)";

    public double Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_volume - clamped) > 0.01)
            {
                _volume = clamped;
                OnPropertyChanged();
                AccessibilityHelper.Announce($"Volume {(int)(_volume * 100)} percent");
                _ = InstagramBridgeService.Instance.SetVolumeAsync(_volume);
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayButtonText));
                OnPropertyChanged(nameof(PlayButtonAutomationText));
                OnPropertyChanged(nameof(PlaybackStatusText));
                AccessibilityHelper.Announce(_isPlaying ? "Playing" : "Paused");
            }
        }
    }

    public string PlayButtonText => IsPlaying ? "Pause (Space)" : "Play (Space)";
    public string PlayButtonAutomationText => IsPlaying ? "Pause reel. Press Spacebar." : "Play reel. Press Spacebar.";
    public string PlaybackStatusText => IsPlaying ? "▶ Playing in Web Engine" : "⏸ Paused";

    public bool IsCommentsOpen
    {
        get => _isCommentsOpen;
        set
        {
            if (_isCommentsOpen != value)
            {
                _isCommentsOpen = value;
                OnPropertyChanged();
                AccessibilityHelper.Announce(_isCommentsOpen ? "Comments panel opened" : "Comments panel closed");
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public ReelsViewModel()
    {
        InstagramBridgeService.Instance.ActiveReelChanged += OnActiveReelChanged;
        InstagramBridgeService.Instance.StatusMessageUpdated += (msg) => StatusMessage = msg;
    }

    private void OnActiveReelChanged(ReelItem reel, bool isPlaying, bool isMuted, double vol)
    {
        bool isNew = CurrentReel == null || CurrentReel.Username != reel.Username || CurrentReel.Caption != reel.Caption;
        bool playChanged = _isPlaying != isPlaying;

        CurrentReel = reel;
        _isPlaying = isPlaying;
        _isMuted = isMuted;
        _volume = vol;

        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayButtonText));
        OnPropertyChanged(nameof(PlayButtonAutomationText));
        OnPropertyChanged(nameof(PlaybackStatusText));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(MuteButtonText));
        OnPropertyChanged(nameof(Volume));

        StatusMessage = isPlaying ? $"Playing reel by @{reel.Username}" : $"Paused reel by @{reel.Username}";

        if (isNew)
        {
            _lastAnnouncedReelId = reel.Id;
            AccessibilityHelper.Announce(reel.AccessibleDescription);
        }
        else if (playChanged)
        {
            AccessibilityHelper.Announce(isPlaying ? "Playing" : "Paused");
        }
    }

    public void MoveNext()
    {
        StatusMessage = "Scrolling down to next reel in Instagram Web...";
        _ = InstagramBridgeService.Instance.NextReelAsync();
    }

    public void MovePrevious()
    {
        StatusMessage = "Scrolling up to previous reel in Instagram Web...";
        _ = InstagramBridgeService.Instance.PreviousReelAsync();
    }

    public void TogglePlay()
    {
        _ = InstagramBridgeService.Instance.TogglePlayAsync();
    }

    public void ToggleMute()
    {
        _ = InstagramBridgeService.Instance.ToggleMuteAsync();
    }

    public void ToggleLike()
    {
        if (CurrentReel != null)
        {
            CurrentReel.IsLiked = !CurrentReel.IsLiked;
            AccessibilityHelper.Announce(CurrentReel.IsLiked ? "Liked reel" : "Unliked reel");
        }
        _ = InstagramBridgeService.Instance.ToggleLikeAsync();
    }

    public void ToggleComments()
    {
        IsCommentsOpen = !IsCommentsOpen;
        _ = InstagramBridgeService.Instance.ToggleCommentsAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
