using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinInstagram.Models;
using WinInstagram.Services;

namespace WinInstagram.ViewModels;

/// <summary>
/// Backs the native accessible Direct Messages panel. Conversations and messages come from
/// Instagram's own direct_v2 payloads; sending a reply is typed into the engine's composer
/// so the session stays authoritative and the result is reported truthfully.
/// </summary>
public class MessagesViewModel : INotifyPropertyChanged
{
    private DirectConversation? _selectedConversation;
    private string _replyText = string.Empty;
    private string _status = "Waiting for your inbox. Press R to load messages.";
    private bool _isActive;
    private string _openThreadId = string.Empty;

    public ObservableCollection<DirectConversation> Conversations { get; } = new();

    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
    }

    public DirectConversation? SelectedConversation
    {
        get => _selectedConversation;
        set
        {
            if (_selectedConversation == value) return;
            _selectedConversation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedConversation));
            OnPropertyChanged(nameof(ThreadHeader));
            OnPropertyChanged(nameof(MessageCountText));

            if (_selectedConversation != null)
            {
                _openThreadId = _selectedConversation.ThreadId;
                Status = $"Loading the conversation with {_selectedConversation.DisplayName}...";
                OpenThreadRequested?.Invoke(_selectedConversation.ThreadId);
            }
        }
    }

    public bool HasSelectedConversation => _selectedConversation != null;

    public string ThreadHeader =>
        _selectedConversation == null ? "Select a conversation" : $"Chat with {_selectedConversation.DisplayName}";

    public string MessageCountText =>
        _selectedConversation == null ? string.Empty
        : $"{_selectedConversation.Messages.Count} messages loaded";

    public string ReplyText
    {
        get => _replyText;
        set { if (_replyText != value) { _replyText = value; OnPropertyChanged(); } }
    }

    public string Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnPropertyChanged(); } }
    }

    /// <summary>Raised when the shell should open a thread in the engine (navigate to /direct/t/{id}).</summary>
    public event Action<string>? OpenThreadRequested;

    /// <summary>Raised when the shell should reload the inbox in the engine.</summary>
    public event Action? InboxSyncRequested;

    public MessagesViewModel()
    {
        InstagramBridgeService.Instance.ConversationsReceived += OnConversationsReceived;
        InstagramBridgeService.Instance.DirectMessagesReceived += OnDirectMessagesReceived;
    }

    public void RequestInboxSync() => InboxSyncRequested?.Invoke();

    private void OnConversationsReceived(List<DirectConversation> conversations)
    {
        var added = 0;
        foreach (var incoming in conversations)
        {
            var existing = Conversations.FirstOrDefault(c => c.ThreadId == incoming.ThreadId);
            if (existing == null)
            {
                Conversations.Add(incoming);
                added++;
            }
            else
            {
                // Refresh the mutable summary fields without losing loaded messages.
                existing.LastMessage = incoming.LastMessage;
                existing.LastTimestamp = incoming.LastTimestamp;
                existing.UnreadCount = incoming.UnreadCount;
                if (string.IsNullOrWhiteSpace(existing.RecipientUsername)) existing.RecipientUsername = incoming.RecipientUsername;
                if (string.IsNullOrWhiteSpace(existing.RecipientFullName)) existing.RecipientFullName = incoming.RecipientFullName;
            }
        }

        Status = added > 0
            ? $"{Conversations.Count} conversations. {added} new. Use Up and Down arrows to read them."
            : $"{Conversations.Count} conversations.";

        if (SelectedConversation == null && Conversations.Count > 0)
        {
            SelectedConversation = Conversations[0];
        }

        AppLogger.Success("UI", $"MessagesViewModel updated with {Conversations.Count} conversations ({added} new).");
    }

    private void OnDirectMessagesReceived(string threadId, List<DirectMessage> messages)
    {
        var conversation = ResolveConversation(threadId);
        if (conversation == null)
        {
            AppLogger.Warn("UI", $"Received {messages.Count} messages for unknown thread '{threadId}'.");
            return;
        }

        var added = 0;
        foreach (var msg in messages)
        {
            var duplicate = !string.IsNullOrWhiteSpace(msg.Id) && conversation.Messages.Any(m => m.Id == msg.Id);
            if (!duplicate)
            {
                conversation.Messages.Add(msg);
                added++;
            }
        }

        if (conversation == SelectedConversation)
        {
            OnPropertyChanged(nameof(MessageCountText));
            var newest = conversation.Messages.LastOrDefault();
            if (added > 0 && newest != null)
            {
                AnnounceIfActive($"{conversation.Messages.Count} messages loaded. Latest message. {newest.AccessibleText}");
            }
        }
    }

    /// <summary>Finds the conversation for a thread id, creating a placeholder if the inbox has not listed it yet.</summary>
    private DirectConversation? ResolveConversation(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            // Fall back to the thread the user most recently opened.
            if (!string.IsNullOrWhiteSpace(_openThreadId))
            {
                return Conversations.FirstOrDefault(c => c.ThreadId == _openThreadId) ?? _selectedConversation;
            }
            return _selectedConversation;
        }

        var existing = Conversations.FirstOrDefault(c => c.ThreadId == threadId);
        if (existing != null) return existing;

        var placeholder = new DirectConversation { ThreadId = threadId, RecipientUsername = "conversation" };
        Conversations.Add(placeholder);
        return placeholder;
    }

    /// <summary>
    /// Sends the typed reply through the engine and reports what actually happened, so the
    /// user is never told a message was sent when the composer was not found.
    /// </summary>
    public async Task SendReplyAsync()
    {
        var text = ReplyText?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            AnnounceIfActive("Type a message before sending.");
            return;
        }

        if (SelectedConversation == null)
        {
            AnnounceIfActive("Open a conversation before sending a message.");
            return;
        }

        Status = "Sending message...";
        var sent = await InstagramBridgeService.Instance.SendDirectMessageAsync(text);

        if (!sent)
        {
            Status = "Could not send the message.";
            AnnounceIfActive("Could not find the message box. Open the conversation in the web view and try again.");
            return;
        }

        var now = DateTime.Now.ToString("t");
        SelectedConversation.Messages.Add(new DirectMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            IsFromMe = true,
            SenderUsername = "You",
            Text = text,
            Timestamp = now
        });
        SelectedConversation.LastMessage = $"You: {text}";
        SelectedConversation.LastTimestamp = now;

        ReplyText = string.Empty;
        OnPropertyChanged(nameof(MessageCountText));
        Status = "Message sent.";
        AnnounceIfActive($"Message sent to {SelectedConversation.DisplayName}.");
    }

    private void AnnounceIfActive(string message)
    {
        if (_isActive) AccessibilityHelper.Announce(message);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
