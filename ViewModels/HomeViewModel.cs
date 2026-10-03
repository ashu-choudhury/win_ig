using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinInstagram.Models;
using WinInstagram.Services;

namespace WinInstagram.ViewModels;

public class HomeViewModel : INotifyPropertyChanged
{
    private FeedPost? _selectedPost;
    private string _status = "Ready. Scroll posts with Up and Down arrows.";

    public ObservableCollection<FeedPost> Posts { get; set; } = new();

    public FeedPost? SelectedPost
    {
        get => _selectedPost;
        set
        {
            if (_selectedPost != value)
            {
                _selectedPost = value;
                OnPropertyChanged();
                if (_selectedPost != null)
                {
                    AccessibilityHelper.Announce(_selectedPost.AccessibleDescription);
                }
            }
        }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public HomeViewModel()
    {
        InstagramBridgeService.Instance.FeedReceived += OnFeedReceived;
    }

    private void OnFeedReceived(List<FeedPost> newPosts)
    {
        foreach (var p in newPosts)
        {
            if (!Posts.Any(x => x.Id == p.Id && !string.IsNullOrEmpty(p.Id)))
            {
                Posts.Add(p);
            }
        }

        if (SelectedPost == null && Posts.Count > 0)
        {
            SelectedPost = Posts[0];
        }

        Status = $"{Posts.Count} posts loaded. Use Up and Down arrow keys to read posts.";
        AppLogger.Success("UI", $"HomeViewModel updated with {Posts.Count} total posts.");
    }

    public void ToggleLikeCurrent()
    {
        if (SelectedPost != null)
        {
            SelectedPost.IsLiked = !SelectedPost.IsLiked;
            if (SelectedPost.IsLiked) SelectedPost.LikesCount++;
            else if (SelectedPost.LikesCount > 0) SelectedPost.LikesCount--;

            AccessibilityHelper.Announce(SelectedPost.IsLiked ? "Liked post" : "Unliked post");
            _ = InstagramBridgeService.Instance.LikeMediaAsync(SelectedPost.Id);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
