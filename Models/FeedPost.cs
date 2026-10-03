using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinInstagram.Models;

public class FeedPost : INotifyPropertyChanged
{
    private bool _isLiked;
    private long _likesCount;

    public string Id { get; set; } = string.Empty;
    public string MediaCode { get; set; } = string.Empty;
    public string Username { get; set; } = "instagram_user";
    public string AvatarUrl { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string MediaUrl { get; set; } = string.Empty;
    public bool IsVideo { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public long CommentsCount { get; set; }

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

    public string FormattedLikes
    {
        get
        {
            if (LikesCount >= 1_000_000)
                return $"{(LikesCount / 1_000_000.0):0.#}M";
            if (LikesCount >= 1_000)
                return $"{(LikesCount / 1_000.0):0.#}K";
            return LikesCount.ToString();
        }
    }

    public string AccessibleDescription
    {
        get
        {
            var typeStr = IsVideo ? "Video post" : "Photo post";
            var likeStatus = IsLiked ? "Liked" : "Not liked";
            var cleanCaption = string.IsNullOrWhiteSpace(Caption) ? "No caption" : Caption.Replace("\n", " ");
            return $"{typeStr} by {Username}. Posted {Timestamp}. Caption: {cleanCaption}. {FormattedLikes} likes, {CommentsCount} comments. Status: {likeStatus}.";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
