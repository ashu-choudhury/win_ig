using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinInstagram.Models;

/// <summary>
/// A Direct Messages inbox thread. Populated from Instagram's own authenticated
/// direct_v2 payloads, so the accessible list never depends on scraping the DOM.
/// </summary>
public class DirectConversation : INotifyPropertyChanged
{
    private string _lastMessage = string.Empty;
    private string _lastTimestamp = string.Empty;
    private int _unreadCount;

    public string ThreadId { get; set; } = string.Empty;
    public string RecipientUsername { get; set; } = string.Empty;
    public string RecipientFullName { get; set; } = string.Empty;
    public string RecipientAvatarUrl { get; set; } = string.Empty;
    public bool IsGroup { get; set; }
    public List<string> Participants { get; set; } = new();

    public string LastMessage
    {
        get => _lastMessage;
        set { if (_lastMessage != value) { _lastMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(AccessibleDescription)); } }
    }

    public string LastTimestamp
    {
        get => _lastTimestamp;
        set { if (_lastTimestamp != value) { _lastTimestamp = value; OnPropertyChanged(); OnPropertyChanged(nameof(AccessibleDescription)); } }
    }

    public int UnreadCount
    {
        get => _unreadCount;
        set
        {
            if (_unreadCount != value)
            {
                _unreadCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UnreadText));
                OnPropertyChanged(nameof(AccessibleDescription));
            }
        }
    }

    /// <summary>Display title: the other person's handle, or a group title from the participants.</summary>
    public string DisplayName
    {
        get
        {
            if (!IsGroup) return string.IsNullOrWhiteSpace(RecipientUsername) ? "Unknown" : RecipientUsername;
            if (Participants.Count > 0) return string.Join(", ", Participants.Take(3));
            return "Group conversation";
        }
    }

    public string UnreadText => UnreadCount > 0 ? $"{UnreadCount} unread" : string.Empty;

    public ObservableCollection<DirectMessage> Messages { get; set; } = new();

    public string AccessibleDescription
    {
        get
        {
            var parts = new List<string>();
            parts.Add(IsGroup ? $"Group conversation, {Participants.Count} people" : $"Conversation with {DisplayName}");
            if (!string.IsNullOrWhiteSpace(LastMessage)) parts.Add($"last message {LastMessage}");
            if (!string.IsNullOrWhiteSpace(LastTimestamp)) parts.Add($"at {LastTimestamp}");
            if (UnreadCount > 0) parts.Add($"{UnreadCount} unread");
            return string.Join(", ", parts) + ". Press Enter to open this conversation.";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A single direct message inside a conversation thread.</summary>
public class DirectMessage
{
    public string Id { get; set; } = string.Empty;
    public string SenderUsername { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public bool IsFromMe { get; set; }
    public string ItemType { get; set; } = "text";

    public string AccessibleText
    {
        get
        {
            var body = ItemType switch
            {
                "media" => "[shared a photo or video]",
                "clip" => "[shared a reel]",
                "media_share" => "[shared a post]",
                "raven_media" => "[disappearing media]",
                "voice_media" => "[voice message]",
                "animated_media" or "sticker" => "[sticker]",
                "like" => "[liked a message]",
                "action_log" => "[conversation event]",
                _ => Text
            };

            if (string.IsNullOrWhiteSpace(body)) body = "[message]";
            var who = IsFromMe ? "You said" : $"{SenderUsername} said";
            return string.IsNullOrWhiteSpace(Timestamp) ? $"{who}: {body}" : $"{who}: {body}, at {Timestamp}";
        }
    }
}
