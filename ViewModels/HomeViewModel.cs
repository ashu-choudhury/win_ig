using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinInstagram.Models;
using WinInstagram.Services;

namespace WinInstagram.ViewModels;

/// <summary>
/// Drives the native accessible Home feed. Posts come from Instagram's own timeline payloads
/// (never from scraping), so every post has a real id, permalink and like count.
///
/// Focus-driven reading is left to UI Automation: each list item exposes its full description
/// as an accessible name, so screen readers speak it as the user arrows through the list.
/// Live-region announcements here are reserved for asynchronous events and action outcomes,
/// which is why arrowing through posts does not double-speak.
/// </summary>
public class HomeViewModel : INotifyPropertyChanged
{
    private FeedPost? _selectedPost;
    private string _status = "Waiting for your timeline. Press R to refresh the feed.";
    private bool _isActive;

    public ObservableCollection<FeedPost> Posts { get; } = new();

    /// <summary>True while the Home panel is on screen, so a hidden view never speaks.</summary>
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
    }

    public bool HasPosts => Posts.Count > 0;
    public bool HasNoPosts => Posts.Count == 0;

    public FeedPost? SelectedPost
    {
        get => _selectedPost;
        set
        {
            if (_selectedPost == value) return;
            _selectedPost = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Raised when the user asks to see a post in the engine; the shell navigates.</summary>
    public event Action<string>? OpenInEngineRequested;

    public string Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnPropertyChanged(); } }
    }

    public HomeViewModel()
    {
        InstagramBridgeService.Instance.FeedReceived += OnFeedReceived;
    }

    private void OnFeedReceived(List<FeedPost> newPosts)
    {
        var added = 0;
        foreach (var p in newPosts)
        {
            var duplicate = !string.IsNullOrWhiteSpace(p.Id) && Posts.Any(x => x.Id == p.Id);
            if (!duplicate)
            {
                Posts.Add(p);
                added++;
            }
        }

        OnPropertyChanged(nameof(HasPosts));
        OnPropertyChanged(nameof(HasNoPosts));

        if (SelectedPost == null && Posts.Count > 0)
        {
            SelectedPost = Posts[0];
        }

        Status = added > 0
            ? $"{Posts.Count} posts in your feed, {added} new. Use Up and Down arrows to read posts."
            : $"{Posts.Count} posts in your feed.";

        AppLogger.Success("UI", $"HomeViewModel updated with {Posts.Count} total posts ({added} new).");

        if (added > 0)
        {
            AnnounceIfActive($"Feed updated. {added} new post{(added == 1 ? "" : "s")}. {Posts.Count} posts available.");
        }
    }

    /// <summary>Opens the selected post's permalink in the engine.</summary>
    public void OpenSelectedInEngine()
    {
        var post = SelectedPost;
        if (post == null)
        {
            AnnounceIfActive("No post selected.");
            return;
        }

        if (string.IsNullOrWhiteSpace(post.MediaCode))
        {
            AnnounceIfActive($"No permalink is available for the post by {post.Username}.");
            return;
        }

        OpenInEngineRequested?.Invoke($"https://www.instagram.com/p/{post.MediaCode}/");
    }

    /// <summary>
    /// Likes or unlikes the selected post. Instagram is asked first and the model is only
    /// updated when it confirms, so the spoken state is never a guess.
    /// </summary>
    public async Task ToggleLikeCurrentAsync()
    {
        var post = SelectedPost;
        if (post == null)
        {
            AnnounceIfActive("No post selected.");
            return;
        }

        var mediaId = !string.IsNullOrWhiteSpace(post.MediaPk) ? post.MediaPk : post.Id;
        if (string.IsNullOrWhiteSpace(mediaId))
        {
            AnnounceIfActive($"The post by {post.Username} has no media id, so it cannot be liked from here.");
            return;
        }

        var target = !post.IsLiked;
        var previousLikes = post.LikesCount;

        Status = target ? $"Liking the post by {post.Username}..." : $"Removing the like from {post.Username}...";

        var confirmed = await InstagramBridgeService.Instance.SetMediaLikedAsync(mediaId, target);

        if (!confirmed)
        {
            Status = $"Could not {(target ? "like" : "unlike")} the post by {post.Username}.";
            AnnounceIfActive($"Instagram did not confirm the change, so the post by {post.Username} was left unchanged.");
            return;
        }

        post.IsLiked = target;
        post.LikesCount = target ? previousLikes + 1 : Math.Max(0, previousLikes - 1);

        var likesPart = string.IsNullOrWhiteSpace(post.FormattedLikes) ? string.Empty : $" {post.FormattedLikes} likes.";
        Status = target ? $"Liked the post by {post.Username}.{likesPart}" : $"Unliked the post by {post.Username}.";
        AnnounceIfActive(Status);
    }

    private void AnnounceIfActive(string message)
    {
        if (_isActive) AccessibilityHelper.Announce(message);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
