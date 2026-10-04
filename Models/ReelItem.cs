using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

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
                OnPropertyChanged(nameof(LikesDisplayText));
                OnPropertyChanged(nameof(FormattedLikesBadge));
                OnPropertyChanged(nameof(ReelLikeButtonText));
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
                OnPropertyChanged(nameof(ReelLikeButtonText));
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
            if (LikesCount > 0)
                return LikesCount.ToString();
            return string.Empty;
        }
        set
        {
            if (_formattedLikesOverride != value)
            {
                _formattedLikesOverride = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AccessibleDescription));
                OnPropertyChanged(nameof(LikesDisplayText));
                OnPropertyChanged(nameof(FormattedLikesBadge));
                OnPropertyChanged(nameof(ReelLikeButtonText));
            }
        }
    }

    public string LikesDisplayText =>
        !string.IsNullOrWhiteSpace(FormattedLikes) ? $"❤️ {FormattedLikes} Likes" : "❤️ Likes";

    public string FormattedLikesBadge =>
        !string.IsNullOrWhiteSpace(FormattedLikes) ? $": {FormattedLikes}" : string.Empty;

    public string ReelLikeButtonText => IsLiked
        ? (!string.IsNullOrWhiteSpace(FormattedLikes) ? $"❤️ Liked ({FormattedLikes})" : "❤️ Liked (L)")
        : (!string.IsNullOrWhiteSpace(FormattedLikes) ? $"🤍 Like ({FormattedLikes})" : "🤍 Like (L)");

    public ObservableCollection<InstagramComment> Comments { get; set; } = new();

    public string AccessibleDescription
    {
        get
        {
            var likeStatus = IsLiked ? "Liked" : "Not liked";
            var cleanCaption = string.IsNullOrWhiteSpace(Caption) ? "No caption" : Caption.Replace("\n", " ");
            var likesPart = !string.IsNullOrWhiteSpace(FormattedLikes) ? $"{FormattedLikes} likes, " : "";
            return $"Reel by {Username}. Caption: {cleanCaption}. Audio: {AudioTitle}. {likesPart}{CommentsCount} comments. Status: {likeStatus}. Press Space to play or pause, M to mute, L to like, C for comments.";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class InstagramComment : INotifyPropertyChanged
{
    private bool _isLiked;
    private long _likesCount;
    private string _text = string.Empty;

    public string Id { get; set; } = string.Empty;
    public int Index { get; set; }
    public string Username { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;

    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AccessibleText));
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
                OnPropertyChanged(nameof(AccessibleText));
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
                OnPropertyChanged(nameof(LikeButtonText));
                OnPropertyChanged(nameof(AccessibleText));
            }
        }
    }

    public string LikeButtonText => IsLiked ? "❤️ Liked" : "🤍 Like";

    public string FormattedLikes
    {
        get
        {
            if (LikesCount <= 0) return "";
            if (LikesCount >= 1_000_000) return $"{(LikesCount / 1_000_000.0):0.#}M";
            if (LikesCount >= 1_000) return $"{(LikesCount / 1_000.0):0.#}K";
            return LikesCount.ToString();
        }
    }

    public Visibility LikesVisibility => LikesCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string AccessibleText
    {
        get
        {
            var likePart = LikesCount > 0 ? $"{FormattedLikes} likes. " : "";
            var statusPart = IsLiked ? "Liked by you. " : "";
            var datePart = !string.IsNullOrWhiteSpace(CreatedAt) ? $"Posted {CreatedAt}. " : "";
            return $"Comment by @{Username}: {Text}. {likePart}{statusPart}{datePart}Press Enter to toggle like.";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
