using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinInstagram.Services;
using WinInstagram.ViewModels;

namespace WinInstagram.Views;

public partial class ReelsView : UserControl
{
    public ReelsViewModel ViewModel => (ReelsViewModel)DataContext;

    public ReelsView()
    {
        InitializeComponent();
        DataContext = new ReelsViewModel();
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        Focus();
        _ = InstagramBridgeService.Instance.SyncActiveReelAsync();
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        // Do not intercept hotkeys if user is currently typing a comment
        if (TxtNewComment.IsFocused)
        {
            if (e.Key == Key.Escape)
            {
                ViewModel.IsCommentsOpen = false;
                Focus();
                e.Handled = true;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.PageDown:
            case Key.Down:
            case Key.J:
                ViewModel.MoveNext();
                e.Handled = true;
                break;

            case Key.PageUp:
            case Key.Up:
            case Key.K:
                ViewModel.MovePrevious();
                e.Handled = true;
                break;

            case Key.Space:
                ViewModel.TogglePlay();
                e.Handled = true;
                break;

            case Key.M:
                ViewModel.ToggleMute();
                e.Handled = true;
                break;

            case Key.L:
                ViewModel.ToggleLike();
                e.Handled = true;
                break;

            case Key.S:
                ShareReel();
                e.Handled = true;
                break;

            case Key.C:
                ViewModel.ToggleComments();
                e.Handled = true;
                break;

            case Key.OemPlus:
            case Key.Add:
                ViewModel.Volume += 0.1;
                e.Handled = true;
                break;

            case Key.OemMinus:
            case Key.Subtract:
                ViewModel.Volume -= 0.1;
                e.Handled = true;
                break;

            case Key.Escape:
                if (ViewModel.IsCommentsOpen)
                {
                    ViewModel.IsCommentsOpen = false;
                    e.Handled = true;
                }
                break;
        }
    }

    private void PrevBtn_Click(object sender, RoutedEventArgs e) => ViewModel.MovePrevious();
    private void NextBtn_Click(object sender, RoutedEventArgs e) => ViewModel.MoveNext();
    private void PlayPauseBtn_Click(object sender, RoutedEventArgs e) => ViewModel.TogglePlay();
    private void MuteBtn_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleMute();
    private void BtnLike_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleLike();
    private void BtnComments_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleComments();
    private void BtnShare_Click(object sender, RoutedEventArgs e) => ShareReel();
    private void CloseComments_Click(object sender, RoutedEventArgs e) => ViewModel.IsCommentsOpen = false;

    private void ShareReel()
    {
        var reel = ViewModel.CurrentReel;
        if (reel == null)
        {
            AccessibilityHelper.Announce("No active reel to share.");
            return;
        }

        string shareUrl = !string.IsNullOrWhiteSpace(reel.Id) && reel.Id.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? reel.Id
            : "https://www.instagram.com/reels/";

        shareUrl = DeepLinkService.NormalizeInstagramUrl(shareUrl);
        if (string.IsNullOrWhiteSpace(shareUrl))
        {
            shareUrl = "https://www.instagram.com/reels/";
        }

        try
        {
            Clipboard.SetText(shareUrl);
            AccessibilityHelper.Announce($"Reel link copied to clipboard: {shareUrl}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("SHARE", "Failed to copy reel link", ex);
            AccessibilityHelper.Announce("Failed to copy link to clipboard.");
        }
    }

    private void PostComment_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TxtNewComment.Text) && ViewModel.CurrentReel != null)
        {
            ViewModel.CurrentReel.Comments.Add(new Models.InstagramComment
            {
                Username = "You",
                Text = TxtNewComment.Text,
                CreatedAt = "Just now"
            });
            TxtNewComment.Text = "";
            AccessibilityHelper.Announce("Comment posted successfully.");
        }
    }
}
