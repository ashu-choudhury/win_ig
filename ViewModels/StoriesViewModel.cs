using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinInstagram.Models;
using WinInstagram.Services;

namespace WinInstagram.ViewModels;

/// <summary>
/// Drives the native stories navigator. Instagram's stories viewer advances on its own timer and
/// has no keyboard stop, which makes it unusable with a screen reader; here every tray entry is a
/// real list item that the user opens deliberately, and the stories themselves can be held still.
///
/// Focus-driven reading is left to UI Automation: each list item exposes its full description as
/// its accessible name, so arrowing through stories does not double-speak with a live region.
/// Live-region announcements are reserved for asynchronous events and the outcome of an action.
///
/// Nothing is reported as done unless the page confirmed it: if there is no story on screen, the
/// pause, step and state calls say so instead of pretending the story moved.
/// </summary>
public class StoriesViewModel : INotifyPropertyChanged
{
    private StoryItem? _selectedStory;
    private bool _isActive;
    private bool _isPaused;
    private string _status = "Waiting for your stories tray. Press R to load stories.";
    private string _nowPlayingText = "No story is open. Press Enter on a story in the list to open it.";

    /// <summary>The story tray entries, in the order Instagram returned them.</summary>
    public ObservableCollection<StoryItem> Stories { get; } = new();

    /// <summary>True while the stories panel is on screen, so a hidden view never speaks.</summary>
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
    }

    public string Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// The tray entry the user has arrowed to. Its full description is the list item's accessible
    /// name, so the screen reader reads it as the user moves.
    /// </summary>
    public StoryItem? SelectedStory
    {
        get => _selectedStory;
        set
        {
            if (_selectedStory == value) return;
            _selectedStory = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// True when the stories on screen are held still. This mirrors what the page reported, never
    /// what the app hoped for, so the button label can be trusted.
    /// </summary>
    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (_isPaused == value) return;
            _isPaused = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PauseButtonText));
        }
    }

    public string PauseButtonText => IsPaused ? "▶ Resume stories (P)" : "⏸ Pause stories (P)";

    /// <summary>A plain sentence describing what the stories viewer is showing right now.</summary>
    public string NowPlayingText
    {
        get => _nowPlayingText;
        set { if (_nowPlayingText != value) { _nowPlayingText = value; OnPropertyChanged(); } }
    }

    public bool HasStories => Stories.Count > 0;
    public bool HasNoStories => Stories.Count == 0;

    /// <summary>Raised when the user asks to open a tray entry; the shell navigates the engine.</summary>
    public event Action<string>? OpenStoryRequested;

    public StoriesViewModel()
    {
        InstagramBridgeService.Instance.StoriesReceived += OnStoriesReceived;
    }

    private void OnStoriesReceived(List<StoryItem> stories)
    {
        if (stories == null || stories.Count == 0) return;

        // Keep the user where they were if that person is still in the tray.
        var previousUsername = SelectedStory?.Username ?? string.Empty;

        Stories.Clear();
        foreach (var story in stories)
        {
            Stories.Add(story);
        }

        SelectedStory = Stories.FirstOrDefault(s => s.Username == previousUsername) ?? Stories[0];

        NotifyCounts();
        Status = $"{Stories.Count} stories are available in your tray. Use Up and Down arrows to read them.";

        AnnounceIfActive($"Stories tray updated. {Stories.Count} stories available.");
    }

    /// <summary>Asks Instagram for a fresh tray and then reports what is actually on screen.</summary>
    public async Task RefreshAsync()
    {
        Status = "Loading your stories tray...";
        AnnounceIfActive("Loading your stories tray.");

        await InstagramBridgeService.Instance.RequestStoriesAsync();
        await Task.Delay(600);
        await ReportStoryStateAsync();

        if (Stories.Count == 0)
        {
            Status = "Instagram has not returned any stories yet. Open the home page, then press R again.";
            AnnounceIfActive(Status);
        }
    }

    /// <summary>Opens the stories of the entry the user is focused on.</summary>
    public void OpenSelected()
    {
        var story = SelectedStory;
        if (story == null || string.IsNullOrWhiteSpace(story.StoryUrl))
        {
            AnnounceIfActive("No story is selected.");
            return;
        }

        Status = $"Opening stories by @{story.Username}...";
        OpenStoryRequested?.Invoke(story.StoryUrl);
    }

    /// <summary>
    /// Holds the story still, or lets it continue. Reports the real outcome, so a missing story is
    /// never announced as a pause.
    /// </summary>
    public async Task TogglePauseAsync()
    {
        if (IsPaused)
        {
            var resumed = await InstagramBridgeService.Instance.ResumeStoryAsync();
            if (!resumed)
            {
                AnnounceIfActive("No story is playing in the web view, so there is nothing to resume.");
                return;
            }

            IsPaused = false;
            Status = "Stories resumed. They will advance on their own again. Press P to hold them still.";
            AnnounceIfActive("Stories resumed.");
            return;
        }

        var paused = await InstagramBridgeService.Instance.PauseStoryAsync();
        if (!paused)
        {
            AnnounceIfActive("No story is playing in the web view. Open a story first, then pause it.");
            return;
        }

        IsPaused = true;
        Status = "Stories paused. Nothing will advance until you step to the next story or resume. Press P to resume.";
        AnnounceIfActive("Stories paused.");
    }

    /// <summary>Steps to the next story item and reports where the viewer actually landed.</summary>
    public Task NextAsync() => StepAsync(next: true);

    /// <summary>Steps back to the previous story item and reports where the viewer actually landed.</summary>
    public Task PreviousAsync() => StepAsync(next: false);

    private async Task StepAsync(bool next)
    {
        var moved = next
            ? await InstagramBridgeService.Instance.NextStoryAsync()
            : await InstagramBridgeService.Instance.PreviousStoryAsync();

        if (!moved)
        {
            AnnounceIfActive("No story is open in the web view. Press Enter on a story first, then use J and K to step through it.");
            return;
        }

        Status = next ? "Moved to the next story." : "Moved to the previous story.";

        // Give Instagram a moment to swap the media before asking what is on screen.
        await Task.Delay(400);
        await ReportStoryStateAsync();
    }

    /// <summary>
    /// Reads the live story state and turns it into a sentence. This is the app's only source of
    /// truth about whether a story is open and whether it is held still.
    /// </summary>
    public async Task ReportStoryStateAsync()
    {
        var state = await InstagramBridgeService.Instance.ReadStoryStateAsync();

        if (!state.IsStory)
        {
            IsPaused = false;
            NowPlayingText = "No story is open. Press Enter on a story in the list to open it.";
            return;
        }

        IsPaused = state.IsPaused;

        var who = string.IsNullOrWhiteSpace(state.Username) ? "this story" : $"the story by @{state.Username}";
        var stateText = state.IsPaused
            ? "It is paused."
            : state.HasVideo ? "It is playing." : "It has no video, so it is a photo story.";

        NowPlayingText = $"Now showing {who}. {stateText}";

        var altPart = string.IsNullOrWhiteSpace(state.AltText) ? string.Empty : $" {state.AltText}";
        AnnounceIfActive(NowPlayingText + altPart);
    }

    /// <summary>The spoken orientation read out when the panel is first shown.</summary>
    public void AnnounceInstructions()
    {
        var lead = Stories.Count > 0
            ? $"{Stories.Count} stories are available."
            : "No stories are loaded yet; press R to load your tray.";

        AnnounceIfActive(
            $"Stories navigator. {lead} Use Up and Down arrows to read stories, Enter to open one, " +
            "P to pause or resume it, and J and K to step to the next or previous story.");
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(HasStories));
        OnPropertyChanged(nameof(HasNoStories));
    }

    private void AnnounceIfActive(string message)
    {
        if (_isActive) Announcements.Say(message);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
