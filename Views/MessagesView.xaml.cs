using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinInstagram.Services;
using WinInstagram.ViewModels;

namespace WinInstagram.Views;

public partial class MessagesView : UserControl
{
    public MessagesViewModel ViewModel => (MessagesViewModel)DataContext;

    public MessagesView()
    {
        InitializeComponent();
        DataContext = new MessagesViewModel();
    }

    private void TxtReply_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.SendReply();
            e.Handled = true;
        }
    }

    private void BtnSend_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SendReply();
    }

    private void SyncMessages_Click(object sender, RoutedEventArgs e)
    {
        InstagramBridgeService.Instance.Navigate("https://www.instagram.com/direct/inbox/");
        AccessibilityHelper.Announce("Syncing messages from Instagram...");
    }
}
