using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinInstagram.Models;
using WinInstagram.Services;

namespace WinInstagram.ViewModels;

/// <summary>
/// Drives the native saved posts panel. Posts and collections come from Instagram's own saved
/// endpoints, never from scraping the page, so every entry has a real permalink.
///
/// Focus-driven reading is left to UI Automation: each list item exposes its full description as
/// an accessible name, so arrowing through the lists speaks on its own. Live-region announcements
/// here are reserved for load results and action outcomes.
/// </summary>
public class SavedViewModel : INotifyPropertyChanged
{
    private const string AllSavedName = "All saved posts";

    private SavedCollection? _selectedCollection;
    private FeedPost? _selectedPost;
    private string _status = "Press R to load your saved posts.";
    private bool _isActive;

    public ObservableCollection<SavedCollection> Collections { get; } = new();
    public ObservableCollection<FeedPost> Posts { get; } = new();

    /// <summary>True while this panel is on screen, so a hidden view never speaks.</summary>
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
    }

    public bool HasCollections => Collections.Count > 0;
    public bool HasPosts => Posts.Count > 0;
    public bool HasNoPosts => Posts.Count == 0;

    public SavedCollection? SelectedCollection
    {
        get => _selectedCollection;
        set
        {
            if (_selectedCollection == value) return;
            _selectedCollection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PersonalisedStatus));
            OnPropertyChanged(nameof(DescriptionLabel));
            // Selecting an entry only changes what we can honestly say about the list; Instagram's
            // saved endpoint returns every saved post at once, so nothing is re-fetched here.
            Status = PersonalisedStatus;
            AnnounceIfActive(Status);
        }
    }

    public FeedPost? SelectedPost
    {
        get => _selectedPost;
        set { if (_selectedPost == value) return; _selectedPost = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnPropertyChanged(); } }
    }

    /// <summary>The heading over the posts list, so the grouping is never a mystery.</summary>
    public string DescriptionLabel =>
        _selectedCollection == null || _selectedCollection.IsAll
            ? "Saved posts"
            : $"Saved posts in {_selectedCollection.Name}";

    /// <summary>
    /// The honest summary of what is currently in the list, including the fact that Instagram does
    /// not tell us which collection each saved post belongs to.
    /// </summary>
    public string PersonalisedStatus
    {
        get
        {
            if (Posts.Count == 0) return "No saved posts are loaded yet. Press R to load them.";
            if (_selectedCollection == null || _selectedCollection.IsAll)
                return $"Showing all {Posts.Count} saved post{(Posts.Count == 1 ? "" : "s")}. Use Up and Down arrows to read them.";

            return $"Showing all {Posts.Count} saved post{(Posts.Count == 1 ? "" : "s")}. Instagram does not report which collection each saved post belongs to here, so the {_selectedCollection.Name} collection cannot be filtered yet.";
        }
    }

    /// <summary>Raised when the user asks to open a saved post; the shell navigates the engine.</summary>
    public event Action<string>? OpenPostRequested;

    public SavedViewModel()
    {
        InstagramBridgeService.Instance.SavedPostsReceived += OnSavedPostsReceived;
        InstagramBridgeService.Instance.CollectionsReceived += OnCollectionsReceived;
        EnsureAllSavedEntry();
    }

    /// <summary>
    /// Asks Instagram for the collections and the saved posts. The two requests are independent,
    /// so they are awaited in sequence only to keep the status message truthful about what is
    /// still loading.
    /// </summary>
    public async Task RefreshAsync()
    {
        Status = "Loading your saved posts...";

        await InstagramBridgeService.Instance.RequestCollectionsAsync();
        await InstagramBridgeService.Instance.RequestSavedPostsAsync();
    }

    private void OnSavedPostsReceived(List<FeedPost> posts)
    {
        var incoming = posts ?? new List<FeedPost>();
        var added = 0;

        foreach (var post in incoming)
        {
            var duplicate = !string.IsNullOrWhiteSpace(post.Id) && Posts.Any(p => p.Id == post.Id);
            if (!duplicate)
            {
                Posts.Add(post);
                added++;
            }
        }

        OnPropertyChanged(nameof(HasPosts));
        OnPropertyChanged(nameof(HasNoPosts));

        if (SelectedPost == null && Posts.Count > 0) SelectedPost = Posts[0];

        EnsureAllSavedEntry();

        Status = Posts.Count == 0
            ? "Instagram returned no saved posts for this account."
            : $"{Posts.Count} saved post{(Posts.Count == 1 ? "" : "s")} loaded, {added} new.";

        AppLogger.Success("UI", $"SavedViewModel updated with {Posts.Count} saved posts ({added} new).");

        if (added > 0 || Posts.Count > 0)
        {
            AnnounceIfActive(Status);
        }
    }

    private void OnCollectionsReceived(List<SavedCollection> collections)
    {
        var incoming = collections ?? new List<SavedCollection>();

        foreach (var collection in incoming)
        {
            if (collection == null || string.IsNullOrWhiteSpace(collection.Name)) continue;
            if (collection.Name.Equals(AllSavedName, StringComparison.OrdinalIgnoreCase)) continue;

            var existing = Collections.FirstOrDefault(c => c.Name.Equals(collection.Name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                Collections.Add(collection);
            }
            else
            {
                // Refresh the mutable count without losing the entry the user may have focused.
                existing.ItemCount = collection.ItemCount > 0 ? collection.ItemCount : existing.ItemCount;
                if (string.IsNullOrWhiteSpace(existing.Id)) existing.Id = collection.Id;
            }
        }

        EnsureAllSavedEntry();
        OnPropertyChanged(nameof(HasCollections));

        if (SelectedCollection == null && Collections.Count > 0) SelectedCollection = Collections[0];

        Status = incoming.Count == 0
            ? "Instagram reported no saved collections. All saved posts are still listed below."
            : $"{Collections.Count - 1} collection{(Collections.Count - 1 == 1 ? "" : "s")} plus all saved posts.";

        AppLogger.Success("UI", $"SavedViewModel updated with {Collections.Count} collection entries.");
        AnnounceIfActive(Status);
    }

    /// <summary>
    /// Keeps one synthetic "All saved posts" entry at the top of the collection list, both so the
    /// list is never empty and so the user always has a way back to the complete list.
    /// </summary>
    private void EnsureAllSavedEntry()
    {
        var existing = Collections.FirstOrDefault(c => c.IsAll || c.Name.Equals(AllSavedName, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new SavedCollection { Name = AllSavedName, IsAll = true, ItemCount = Posts.Count };
            Collections.Insert(0, existing);
        }
        else
        {
            existing.ItemCount = Posts.Count;
            var index = Collections.IndexOf(existing);
            if (index > 0) Collections.Move(index, 0);
        }
    }

    /// <summary>Opens the selected saved post's permalink in the engine.</summary>
    public void OpenSelectedPost()
    {
        var post = SelectedPost;
        if (post == null)
        {
            AnnounceIfActive("No saved post is selected.");
            return;
        }

        if (string.IsNullOrWhiteSpace(post.MediaCode))
        {
            AnnounceIfActive($"The saved post by {post.Username} has no permalink, so it cannot be opened from here.");
            return;
        }

        OpenPostRequested?.Invoke($"https://www.instagram.com/p/{post.MediaCode}/");
    }

    private void AnnounceIfActive(string message)
    {
        if (_isActive) Announcements.Say(message);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
