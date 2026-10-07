using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinInstagram.Models;

/// <summary>
/// One saved collection. Collections are named buckets of saved posts, and Instagram only exposes
/// them as a row of thumbnails, so each one is listed here as a named item with a count.
/// </summary>
public class SavedCollection : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int ItemCount { get; set; }

    /// <summary>True for the synthetic "All saved posts" entry that lists everything.</summary>
    public bool IsAll { get; set; }

    public string CountText => ItemCount <= 0 ? "Count unavailable" : $"{ItemCount} saved posts";

    public string AccessibleDescription =>
        IsAll
            ? $"All saved posts. {CountText}. Press Enter to list them."
            : $"Collection {Name}. {CountText}. Press Enter to list its posts.";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
