using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinInstagram.Models;

/// <summary>
/// One story in the tray at the top of the Instagram home page. Stories auto-advance on a timer,
/// which is hostile to screen reader users, so each item is exposed as a real list entry that can
/// be opened, stepped and paused deliberately.
/// </summary>
public class StoryItem : INotifyPropertyChanged
{
    private bool _isSeen;

    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;

    /// <summary>Story count Instagram reports for this user's tray entry.</summary>
    public int ItemCount { get; set; }

    /// <summary>True when every story in this tray entry has already been viewed.</summary>
    public bool IsSeen
    {
        get => _isSeen;
        set
        {
            if (_isSeen == value) return;
            _isSeen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AccessibleDescription));
        }
    }

    /// <summary>Direct link to this user's stories, so the engine can open it precisely.</summary>
    public string StoryUrl => string.IsNullOrWhiteSpace(Username)
        ? string.Empty
        : $"https://www.instagram.com/stories/{Username}/";

    public string SeenText => IsSeen ? "Already viewed" : "Not yet viewed";

    public string ItemCountText => ItemCount <= 1
        ? "1 story"
        : $"{ItemCount} stories";

    public string AccessibleDescription
    {
        get
        {
            var who = string.IsNullOrWhiteSpace(UserFullName) || UserFullName == Username
                ? $"Story by {Username}"
                : $"Story by {Username}, {UserFullName}";
            return $"{who}. {ItemCountText}. {SeenText}. Press Enter to open these stories, P to pause or resume them.";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
