using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinInstagram.Models;

public class DirectConversation : INotifyPropertyChanged
{
    private string _lastMessage = string.Empty;
    private string _lastTimestamp = string.Empty;

    public string ThreadId { get; set; } = string.Empty;
    public string RecipientUsername { get; set; } = string.Empty;
    public string RecipientFullName { get; set; } = string.Empty;
    public string RecipientAvatarUrl { get; set; } = string.Empty;

    public string LastMessage
    {
        get => _lastMessage;
        set { _lastMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(AccessibleDescription)); }
    }

    public string LastTimestamp
    {
        get => _lastTimestamp;
        set { _lastTimestamp = value; OnPropertyChanged(); OnPropertyChanged(nameof(AccessibleDescription)); }
    }

    public ObservableCollection<DirectMessage> Messages { get; set; } = new();

    public string AccessibleDescription =>
        $"Conversation with {RecipientUsername} ({RecipientFullName}). Last message: {LastMessage} at {LastTimestamp}.";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class DirectMessage
{
    public string Id { get; set; } = string.Empty;
    public string SenderUsername { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public bool IsFromMe { get; set; }

    public string AccessibleText =>
        IsFromMe ? $"You said: {Text}, sent at {Timestamp}" : $"{SenderUsername} said: {Text}, sent at {Timestamp}";
}
