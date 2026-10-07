using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinInstagram.ViewModels;

namespace WinInstagram.Views;

/// <summary>
/// Native accessible Direct Messages panel. Conversations and their messages are real list
/// items built from Instagram's own direct_v2 payloads, so reading and replying to messages
/// does not require navigating the web DOM.
/// </summary>
public partial class MessagesView : UserControl
{
    public MessagesViewModel ViewModel => (MessagesViewModel)DataContext;

    /// <summary>Raised when the user asks to reload the inbox; the shell reloads the engine.</summary>
    public event Action? SyncRequested;

    public MessagesView()
    {
        InitializeComponent();
        DataContext = new MessagesViewModel();
    }

    /// <summary>Puts focus on the conversation list so arrow keys read conversations immediately.</summary>
    public void FocusConversations()
    {
        ConversationsList.Focus();
        Keyboard.Focus(ConversationsList);
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        // Never steal keys while the user is typing a reply.
        if (TxtReply.IsKeyboardFocusWithin) return;

        if (e.Key == Key.R)
        {
            SyncRequested?.Invoke();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && MessagesList.IsKeyboardFocusWithin)
        {
            ConversationsList.Focus();
            Keyboard.Focus(ConversationsList);
            e.Handled = true;
        }
    }

    private void TxtReply_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = ViewModel.SendReplyAsync();
            e.Handled = true;
        }
    }

    private void Send_Click(object sender, RoutedEventArgs e) => _ = ViewModel.SendReplyAsync();

    private void Sync_Click(object sender, RoutedEventArgs e) => SyncRequested?.Invoke();
}
