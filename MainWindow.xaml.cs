using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinInstagram.Services;

namespace WinInstagram;

public partial class MainWindow : Window
{
    private bool _hasTransitionedToHome = false;
    private string _currentTab = "";

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AccessibilityHelper.RegisterAnnouncementTarget(TxtFooterStatus);

        ViewLogin.LoginSucceeded += OnLoginSucceeded;
        InstagramBridgeService.Instance.LoginStatusChanged += OnLoginStatusChanged;
        InstagramBridgeService.Instance.StatusMessageUpdated += (msg) =>
        {
            Dispatcher.Invoke(() => TxtFooterStatus.Text = msg);
        };
        InstagramBridgeService.Instance.ActiveReelChanged += (reel, isPlaying, isMuted, vol) =>
        {
            Dispatcher.Invoke(() =>
            {
                var likesInfo = !string.IsNullOrWhiteSpace(reel.FormattedLikes) ? $" • ❤️ {reel.FormattedLikes}" : "";
                var likeBtnLabel = reel.IsLiked
                    ? (!string.IsNullOrWhiteSpace(reel.FormattedLikes) ? $"❤️ Liked ({reel.FormattedLikes})" : "❤️ Liked (L)")
                    : (!string.IsNullOrWhiteSpace(reel.FormattedLikes) ? $"🤍 Like ({reel.FormattedLikes})" : "🤍 Like (L)");

                var captionSnippet = !string.IsNullOrWhiteSpace(reel.Caption) ? $" • {reel.Caption}" : "";
                TxtFooterStatus.Text = $"@{reel.Username}{likesInfo}{captionSnippet}";

                BtnPlayPauseReel.Content = isPlaying ? "⏸ Pause (Space)" : "▶ Play (Space)";
                BtnMuteReel.Content = isMuted ? "🔇 Unmute (M)" : "🔊 Mute (M)";
                BtnLikeReel.Content = likeBtnLabel;

                var likeAnnounce = reel.IsLiked
                    ? $"Liked. {reel.FormattedLikes} likes. Press L to unlike."
                    : $"Like. {reel.FormattedLikes} likes. Press L to like.";
                System.Windows.Automation.AutomationProperties.SetName(BtnLikeReel, likeAnnounce);

                var announceMsg = $"Reel by @{reel.Username}. {(!string.IsNullOrWhiteSpace(reel.FormattedLikes) ? reel.FormattedLikes + " likes. " : "")}{reel.Caption}";
                AccessibilityHelper.Announce(announceMsg);
            });
        };

        // If not logged in yet, disable home/reels/messages until credentials are confirmed
        if (!InstagramBridgeService.Instance.IsLoggedIn)
        {
            NavHome.IsEnabled = false;
            NavReels.IsEnabled = false;
            NavMessages.IsEnabled = false;
            NavEngine.IsChecked = true;
            ReelControlsPanel.Visibility = Visibility.Collapsed;
            AccessibilityHelper.Announce("WinInstagram started. Please enter your credentials on the login screen below.");
        }
    }

    private void OnLoginStatusChanged(bool isLoggedIn)
    {
        Dispatcher.Invoke(() =>
        {
            if (isLoggedIn)
            {
                BadgeSession.Background = new SolidColorBrush(Color.FromRgb(30, 70, 32));
                TxtSessionBadge.Text = "🟢 Logged In";
                TxtSessionBadge.Foreground = new SolidColorBrush(Color.FromRgb(100, 220, 100));

                NavHome.IsEnabled = true;
                NavReels.IsEnabled = true;
                NavMessages.IsEnabled = true;

                if (!_hasTransitionedToHome)
                {
                    OnLoginSucceeded();
                }
            }
            else
            {
                BadgeSession.Background = new SolidColorBrush(Color.FromRgb(70, 50, 20));
                TxtSessionBadge.Text = "🟡 Login Required";
                TxtSessionBadge.Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 80));

                NavHome.IsEnabled = false;
                NavReels.IsEnabled = false;
                NavMessages.IsEnabled = false;
                NavEngine.IsChecked = true;
                ReelControlsPanel.Visibility = Visibility.Collapsed;
                _hasTransitionedToHome = false;
            }
        });
    }

    private void OnLoginSucceeded()
    {
        Dispatcher.Invoke(() =>
        {
            if (_hasTransitionedToHome) return;
            _hasTransitionedToHome = true;
            NavHome.IsEnabled = true;
            NavReels.IsEnabled = true;
            NavMessages.IsEnabled = true;

            // Automatically switch to Home Feed
            NavHome.IsChecked = true;

            AccessibilityHelper.Announce("Login successful. Full-screen Instagram is ready.");
        });
    }

    private void NavButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;
        SwitchToTab(tag);
    }

    private void SwitchToTab(string tag)
    {
        if (_currentTab == tag) return;
        _currentTab = tag;

        AppLogger.Info("TAB", $"Switched to tab: {tag}");

        if (tag == "Engine")
        {
            ReelControlsPanel.Visibility = Visibility.Collapsed;
            ViewLogin.SetAccessibleFocusable(true);
            AccessibilityHelper.Announce("Account and Login view.");
        }
        else
        {
            ViewLogin.SetAccessibleFocusable(false);
        }

        switch (tag)
        {
            case "Home":
                ReelControlsPanel.Visibility = Visibility.Collapsed;
                AccessibilityHelper.Announce("Home Feed. Showing full-screen live Instagram home feed.");
                InstagramBridgeService.Instance.Navigate("https://www.instagram.com/");
                break;

            case "Reels":
                ReelControlsPanel.Visibility = Visibility.Visible;
                BtnNextReel.Focus();
                AccessibilityHelper.Announce("Reels Player. Showing full-screen Instagram reels. Use Down Arrow or PageDown for next reel, Up Arrow or PageUp for previous, Space to play or pause, M to mute, L to like, C for comments.");
                InstagramBridgeService.Instance.NavigateToReels();
                break;

            case "Messages":
                ReelControlsPanel.Visibility = Visibility.Collapsed;
                AccessibilityHelper.Announce("Direct Messages. Showing full-screen live Instagram messages.");
                InstagramBridgeService.Instance.Navigate("https://www.instagram.com/direct/inbox/");
                break;
        }
    }

    private void ViewLogin_GotFocus(object sender, RoutedEventArgs e)
    {
        if (NavEngine.IsChecked != true)
        {
            if (NavReels.IsChecked == true) BtnNextReel.Focus();
            else NavHome.Focus();
        }
    }

    private void BtnPrevReel_Click(object sender, RoutedEventArgs e) => _ = InstagramBridgeService.Instance.PreviousReelAsync();
    private void BtnPlayPauseReel_Click(object sender, RoutedEventArgs e) => _ = InstagramBridgeService.Instance.TogglePlayAsync();
    private void BtnNextReel_Click(object sender, RoutedEventArgs e) => _ = InstagramBridgeService.Instance.NextReelAsync();
    private void BtnMuteReel_Click(object sender, RoutedEventArgs e) => _ = InstagramBridgeService.Instance.ToggleMuteAsync();
    private void BtnLikeReel_Click(object sender, RoutedEventArgs e) => _ = InstagramBridgeService.Instance.ToggleLikeAsync();
    private void BtnCommentsReel_Click(object sender, RoutedEventArgs e) => _ = InstagramBridgeService.Instance.ToggleCommentsAsync();

    private void TxtQuickComment_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitComment();
            e.Handled = true;
        }
    }

    private void BtnPostComment_Click(object sender, RoutedEventArgs e)
    {
        SubmitComment();
    }

    private void SubmitComment()
    {
        var text = TxtQuickComment.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        _ = InstagramBridgeService.Instance.PostCommentAsync(text);
        TxtQuickComment.Clear();
        AccessibilityHelper.Announce($"Comment submitted: {text}");
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (isCtrl)
        {
            switch (e.Key)
            {
                case Key.D1:
                case Key.NumPad1:
                    if (NavHome.IsEnabled) NavHome.IsChecked = true;
                    e.Handled = true;
                    return;

                case Key.D2:
                case Key.NumPad2:
                    if (NavReels.IsEnabled) NavReels.IsChecked = true;
                    e.Handled = true;
                    return;

                case Key.D3:
                case Key.NumPad3:
                    if (NavMessages.IsEnabled) NavMessages.IsChecked = true;
                    e.Handled = true;
                    return;

                case Key.L:
                case Key.D4:
                case Key.NumPad4:
                    NavEngine.IsChecked = true;
                    e.Handled = true;
                    return;
            }
        }

        // Global hotkeys when on Reels tab
        if (NavReels.IsChecked == true)
        {
            switch (e.Key)
            {
                case Key.PageDown:
                case Key.Down:
                case Key.J:
                    _ = InstagramBridgeService.Instance.NextReelAsync();
                    e.Handled = true;
                    break;

                case Key.PageUp:
                case Key.Up:
                case Key.K:
                    _ = InstagramBridgeService.Instance.PreviousReelAsync();
                    e.Handled = true;
                    break;

                case Key.Space:
                    _ = InstagramBridgeService.Instance.TogglePlayAsync();
                    e.Handled = true;
                    break;

                case Key.M:
                    _ = InstagramBridgeService.Instance.ToggleMuteAsync();
                    e.Handled = true;
                    break;

                case Key.L:
                    _ = InstagramBridgeService.Instance.ToggleLikeAsync();
                    e.Handled = true;
                    break;

                case Key.C:
                    _ = InstagramBridgeService.Instance.ToggleCommentsAsync();
                    e.Handled = true;
                    break;
            }
        }
    }
}