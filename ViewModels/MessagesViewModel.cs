using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinInstagram.Models;
using WinInstagram.Services;

namespace WinInstagram.ViewModels;

public class MessagesViewModel : INotifyPropertyChanged
{
    private DirectConversation? _selectedConversation;
    private string _replyText = string.Empty;

    public ObservableCollection<DirectConversation> Conversations { get; set; } = new();

    public DirectConversation? SelectedConversation
    {
        get => _selectedConversation;
        set
        {
            if (_selectedConversation != value)
            {
                _selectedConversation = value;
                OnPropertyChanged();
                if (_selectedConversation != null)
                {
                    AccessibilityHelper.Announce($"Conversation with {_selectedConversation.RecipientUsername}. Press Tab to read messages.");
                }
            }
        }
    }

    public string ReplyText
    {
        get => _replyText;
        set { _replyText = value; OnPropertyChanged(); }
    }

    public void SendReply()
    {
        if (SelectedConversation == null || string.IsNullOrWhiteSpace(ReplyText))
            return;

        var newMsg = new DirectMessage
        {
            Id = Guid.NewGuid().ToString(),
            IsFromMe = true,
            Text = ReplyText,
            Timestamp = DateTime.Now.ToString("t"),
            SenderUsername = "You"
        };

        SelectedConversation.Messages.Add(newMsg);
        SelectedConversation.LastMessage = ReplyText;
        SelectedConversation.LastTimestamp = newMsg.Timestamp;

        AccessibilityHelper.Announce("Message sent.");
        ReplyText = string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
