using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinInstagram.Models;

public class FeedPost : INotifyPropertyChanged
{
    private bool _isLiked;
    private long _likesCount;

    public string Id { get; set; } = string.Empty;
    public string MediaCode { get; set; } = string.Empty;
    /// <summary>Numeric media pk used by Instagram's like/unlike endpoint.</summary>
    public string MediaPk { get; set; } = string.Empty;
    public string Username { get; set; } = "instagram_user";
    public string AvatarUrl { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string MediaUrl { get; set; } = string.Empty;
    public bool IsVideo { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public long CommentsCount { get; set; }

    /// <summary>Instagram's own alt text for this image, when the author supplied one.</summary>
    public string AltText { get; set; } = string.Empty;

    /// <summary>Number of slides when this post is a carousel.</summary>
    public int CarouselCount { get; set; }

    public bool IsCarousel => CarouselCount > 1;

    public bool HasAltText => !string.IsNullOrWhiteSpace(AltText);

    /// <summary>Spoken form of the alt text, or empty when there is none.</summary>
    public string AltTextLine => string.IsNullOrWhiteSpace(AltText)
        ? string.Empty
        : $"Image description: {AltText}";

    public string CarouselLine => IsCarousel ? $"Carousel with {CarouselCount} items" : string.Empty;

    public string AltTextDisplay => string.IsNullOrWhiteSpace(AltText)
        ? "No image description provided"
        : AltText;

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
                OnPropertyChanged(nameof(LikesText));
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
                OnPropertyChanged(nameof(LikeButtonText));
                OnPropertyChanged(nameof(AccessibleDescription));
            }
        }
    }

    public string LikeButtonText => IsLiked ? "❤️ Liked (L)" : "🤍 Like (L)";

    public string LikesText =>
        string.IsNullOrWhiteSpace(FormattedLikes) ? "No likes recorded" : $"❤️ {FormattedLikes} likes";

    public string CommentsText =>
        CommentsCount > 0 ? $"💬 {CommentsCount} comments" : "💬 No comments counted";

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
            var typeStr = IsCarousel ? $"Carousel of {CarouselCount}" : IsVideo ? "Video post" : "Photo post";
            var likeStatus = IsLiked ? "Liked" : "Not liked";
            var cleanCaption = string.IsNullOrWhiteSpace(Caption) ? "No caption" : Caption.Replace("\n", " ");
            var parts = new List<string>
            {
                $"{typeStr} by {Username}",
                $"Posted {Timestamp}",
                $"Caption: {cleanCaption}"
            };

            // Alt text is the only description of the image itself, so it is always read out.
            if (!string.IsNullOrWhiteSpace(AltText)) parts.Add(AltTextLine);

            parts.Add($"{FormattedLikes} likes, {CommentsCount} comments");
            parts.Add($"Status: {likeStatus}");
            return string.Join(". ", parts) + ".";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
