using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinInstagram.Models;
using WinInstagram.Services;

namespace WinInstagram.ViewModels;

/// <summary>
/// Backs the native profile viewer. Instagram shows a profile as a wall of images with the name,
/// biography and counters scattered around them; this view model turns exactly the same payload
/// into readable text and a list of recent posts.
///
/// Focus-driven reading is left to UI Automation: each list item exposes its full description as
/// an accessible name, so a screen reader speaks it while the user arrows through the list. Live
/// region announcements are reserved for asynchronous arrivals, which is why arrowing does not
/// double-speak.
/// </summary>
public class ProfileViewModel : INotifyPropertyChanged
{
    private ProfileCard? _profile;
    private FeedPost? _selectedPost;
    private bool _isActive;
    private string _status = "No profile loaded. Open a profile from search, the command palette, or a post.";

    public ObservableCollection<FeedPost> Posts { get; } = new();

    public ProfileViewModel()
    {
        InstagramBridgeService.Instance.ProfileReceived += OnProfileReceived;
    }

    /// <summary>True while the profile panel is on screen, so a hidden view never speaks.</summary>
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
    }

    public ProfileCard? Profile
    {
        get => _profile;
        private set
        {
            if (_profile == value) return;
            _profile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProfile));
            OnPropertyChanged(nameof(HasNoProfile));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(BioText));
            OnPropertyChanged(nameof(AccountTypeText));
            OnPropertyChanged(nameof(StatsText));
            OnPropertyChanged(nameof(HeaderText));
        }
    }

    public bool HasProfile => _profile != null;
    public bool HasNoProfile => _profile == null;

    public string HeaderText => _profile == null ? "👤 Profile" : $"👤 {_profile.Handle}";

    public string Title => _profile?.Title ?? "No profile loaded";

    public string BioText => _profile?.BioText ?? string.Empty;

    public string AccountTypeText => _profile?.AccountTypeText ?? string.Empty;

    public string StatsText => _profile == null
        ? string.Empty
        : $"📊 {_profile.FollowerText} • {_profile.FollowingText} • {_profile.PostCountText}";

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

    public bool HasPosts => Posts.Count > 0;
    public bool HasNoPosts => Posts.Count == 0;

    public string Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnPropertyChanged(); } }
    }

    /// <summary>Raised when the user asks to see something in the engine; the shell navigates.</summary>
    public event Action<string>? OpenInEngineRequested;

    private void OnProfileReceived(ProfileCard profile, List<FeedPost> posts)
    {
        Profile = profile;

        Posts.Clear();
        foreach (var post in posts)
        {
            var duplicate = !string.IsNullOrWhiteSpace(post.Id) && Posts.Any(p => p.Id == post.Id);
            if (!duplicate) Posts.Add(post);
        }

        OnPropertyChanged(nameof(HasPosts));
        OnPropertyChanged(nameof(HasNoPosts));
        SelectedPost = Posts.FirstOrDefault();

        Status = $"Profile for {profile.Handle}: {profile.FollowerText}, {profile.FollowingText}, {profile.PostCountText}. {Posts.Count} recent post{(Posts.Count == 1 ? "" : "s")} loaded.";

        AppLogger.Success("UI", $"ProfileViewModel loaded @{profile.Username} with {Posts.Count} posts.");

        if (_isActive)
        {
            Announcements.Detail(Status);
        }
    }

    /// <summary>
    /// Asks Instagram for a profile and reports honestly. Instagram is only asked once, because a
    /// misspelled handle silently returns nothing and a request loop would be invisible noise.
    /// </summary>
    public async Task LoadAsync(string username)
    {
        var handle = username?.Trim().TrimStart('@') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(handle))
        {
            Status = "A profile needs a username.";
            AnnounceIfActive(Status);
            return;
        }

        Profile = null;
        Posts.Clear();
        OnPropertyChanged(nameof(HasPosts));
        OnPropertyChanged(nameof(HasNoPosts));
        SelectedPost = null;

        Status = $"Loading the profile of @{handle}...";
        AnnounceIfActive($"Loading the profile of {handle}.");

        await InstagramBridgeService.Instance.RequestProfileAsync(handle);
    }

    /// <summary>
    /// Called by the shell when Instagram came back empty, so the panel never sits on "Loading..."
    /// forever pretending a request is still in flight.
    /// </summary>
    public void ReportLoadFailure(string username)
    {
        var handle = username?.Trim().TrimStart('@') ?? string.Empty;
        Status = $"Instagram did not return a profile for @{handle}. Check the spelling, or open the profile in the web view.";
        AnnounceIfActive(Status);
    }

    /// <summary>Opens the selected post's permalink in the engine.</summary>
    public void OpenSelectedPost()
    {
        var post = SelectedPost;
        if (post == null)
        {
            AnnounceIfActive("No post is selected.");
            return;
        }

        if (string.IsNullOrWhiteSpace(post.MediaCode))
        {
            AnnounceIfActive($"The post by {post.Username} has no permalink, so it cannot be opened in the web view.");
            return;
        }

        OpenInEngineRequested?.Invoke($"https://www.instagram.com/p/{post.MediaCode}/");
        AnnounceIfActive($"Opening the post by {post.Username} in the web view.");
    }

    private void AnnounceIfActive(string message)
    {
        if (_isActive) Announcements.Say(message);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
