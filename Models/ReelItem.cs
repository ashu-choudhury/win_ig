using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinInstagram.Models;

public class ReelItem : INotifyPropertyChanged
{
    private bool _isLiked;
    private long _likesCount;
    private string _caption = string.Empty;
    private string _audioTitle = "Original Audio";

    public string Id { get; set; } = string.Empty;
    public string MediaCode { get; set; } = string.Empty;
    public string Username { get; set; } = "instagram_user";
    public string UserFullName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string VideoUrl { get; set; } = string.Empty;
    public long CommentsCount { get; set; }

    public string Caption
    {
        get => _caption;
        set
        {
            if (_caption != value)
            {
                _caption = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AccessibleDescription));
            }
        }
    }

    public string AudioTitle
    {
        get => _audioTitle;
        set
        {
            if (_audioTitle != value)
            {
                _audioTitle = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AccessibleDescription));
            }
        }
    }

    public long LikesCount
    {
        get => _likesCount;
        set
        {
            if (_likesCount != value)
            {
                _likesCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedLikes));
                OnPropertyChanged(nameof(AccessibleDescription));
            }
        }
    }

    public bool IsLiked
    {
        get => _isLiked;
        set
        {
            if (_isLiked != value)
            {
                _isLiked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AccessibleDescription));
            }
        }
    }

    private string _formattedLikesOverride = string.Empty;
    public string FormattedLikes
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_formattedLikesOverride))
                return _formattedLikesOverride;
            if (LikesCount >= 1_000_000)
                return $"{(LikesCount / 1_000_000.0):0.#}M";
            if (LikesCount >= 1_000)
                return $"{(LikesCount / 1_000.0):0.#}K";
            return LikesCount.ToString();
        }
        set
        {
            if (_formattedLikesOverride != value)
            {
                _formattedLikesOverride = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AccessibleDescription));
            }
        }
    }

    public ObservableCollection<InstagramComment> Comments { get; set; } = new();

    public string AccessibleDescription
    {
        get
        {
            var likeStatus = IsLiked ? "Liked" : "Not liked";
            var cleanCaption = string.IsNullOrWhiteSpace(Caption) ? "No caption" : Caption.Replace("\n", " ");
            return $"Reel by {Username}. Caption: {cleanCaption}. Audio: {AudioTitle}. {FormattedLikes} likes, {CommentsCount} comments. Status: {likeStatus}. Press Space to play or pause, M to mute, L to like, C for comments.";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class InstagramComment
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public long LikesCount { get; set; }

    public string AccessibleText => $"{Username}: {Text}. {LikesCount} likes. Posted {CreatedAt}";
}
