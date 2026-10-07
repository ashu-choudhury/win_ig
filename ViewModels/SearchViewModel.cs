using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinInstagram.Models;
using WinInstagram.Services;

namespace WinInstagram.ViewModels;

/// <summary>
/// Drives the native accessible search panel. Queries are sent to Instagram's own search
/// endpoint from inside the authenticated page, and the response is read as text, so a blind
/// user gets accounts, hashtags and posts as list items instead of a wall of thumbnails.
///
/// Focus-driven reading is left to UI Automation: each result exposes its full description as an
/// accessible name, so nothing here speaks while the user arrows through the list.
/// </summary>
public class SearchViewModel : INotifyPropertyChanged
{
    private string _query = string.Empty;
    private SearchResult? _selectedResult;
    private bool _isSearching;
    private bool _isActive;
    private string _status = "Type a name, hashtag or keyword and press Enter. Results are read as text, so nothing depends on the web page.";

    public ObservableCollection<SearchResult> Results { get; } = new();

    /// <summary>True while the search panel is on screen, so a hidden view never speaks.</summary>
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
    }

    public bool HasResults => Results.Count > 0;
    public bool HasNoResults => Results.Count == 0;

    /// <summary>What the user typed. Bound to the text box, so it updates as they type.</summary>
    public string Query
    {
        get => _query;
        set { if (_query != value) { _query = value ?? string.Empty; OnPropertyChanged(); } }
    }

    public SearchResult? SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (_selectedResult == value) return;
            _selectedResult = value;
            OnPropertyChanged();
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        set { if (_isSearching != value) { _isSearching = value; OnPropertyChanged(); } }
    }

    public string Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnPropertyChanged(); } }
    }

    /// <summary>Raised when the user picks a result; the shell opens that URL in the engine.</summary>
    public event Action<string>? OpenResultRequested;

    public SearchViewModel()
    {
        InstagramBridgeService.Instance.SearchReceived += OnSearchReceived;
    }

    private void OnSearchReceived(List<SearchResult> results)
    {
        IsSearching = false;

        Results.Clear();
        foreach (var result in results)
        {
            Results.Add(result);
        }

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasNoResults));

        SelectedResult = Results.Count > 0 ? Results[0] : null;

        AppLogger.Success("UI", $"SearchViewModel received {Results.Count} results for '{_query}'.");

        Status = Results.Count == 0
            ? $"No accounts, hashtags or posts were found for {_query}."
            : $"{Results.Count} results for {_query}. Use Up and Down arrows to read them.";

        AnnounceIfActive(Status);
    }

    /// <summary>
    /// Asks Instagram for matches. The result arrives asynchronously through the bridge, so this
    /// only reports that the search started and never claims it found anything.
    /// </summary>
    public async Task SearchAsync()
    {
        var term = _query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(term))
        {
            Status = "Type something to search for first.";
            AnnounceIfActive(Status);
            return;
        }

        Query = term;
        IsSearching = true;
        Status = $"Searching Instagram for {term}...";
        AnnounceAndSpeakDetail(Status);

        await InstagramBridgeService.Instance.RequestSearchAsync(term);
    }

    /// <summary>Opens the highlighted result in the engine.</summary>
    public void OpenSelected()
    {
        var result = SelectedResult;
        if (result == null)
        {
            AnnounceIfActive("No result selected. Use Up and Down arrows to choose one.");
            return;
        }

        if (string.IsNullOrWhiteSpace(result.OpenUrl))
        {
            AnnounceIfActive($"This {result.KindText.ToLowerInvariant()} has no link to open.");
            return;
        }

        OpenResultRequested?.Invoke(result.OpenUrl);
    }

    private void AnnounceIfActive(string message)
    {
        if (_isActive) Announcements.Say(message);
    }

    /// <summary>Detail-level speech: "searching" is useful context but not essential.</summary>
    private void AnnounceAndSpeakDetail(string message)
    {
        if (_isActive) Announcements.Detail(message);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
