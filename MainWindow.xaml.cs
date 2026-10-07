using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WinInstagram.Models;
using WinInstagram.Services;

namespace WinInstagram;

public partial class MainWindow : Window
{
    private bool _hasTransitionedToHome = false;
    private string _currentTab = "";
    private ReelItem? _activeReel;
    private bool _isPlaying = true;
    private bool _isMuted = false;
    private bool _panelManuallyHidden = false;
    private readonly ObservableCollection<InstagramComment> _activeComments = new();
    private UpdateInfo? _pendingUpdate;
    private bool _isDownloadingUpdate = false;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AccessibilityHelper.RegisterAnnouncementTarget(TxtFooterStatus);
        ListComments.ItemsSource = _activeComments;

        UpdateService.Instance.UpdateAvailable += OnUpdateAvailable;
        UpdateService.Instance.UpdateCheckStatusUpdated += OnUpdateCheckStatusUpdated;

        // Background update check after app startup
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            await UpdateService.Instance.CheckForUpdatesAsync(isManual: false);
        });

        DeepLinkService.Instance.LinkActivated += (url) =>
        {
            Dispatcher.Invoke(() => NavigateToDeepLink(url));
        };
        RefreshAppUriStatus();

        if (!string.IsNullOrWhiteSpace(App.InitialLaunchUrl))
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                Dispatcher.Invoke(() => NavigateToDeepLink(App.InitialLaunchUrl));
            });
        }

        // Native accessible views. Every action they raise is executed here, so the shell stays
        // the single owner of engine navigation and the single owner of anything spoken.
        ViewHome.ViewModel.OpenInEngineRequested += NavigateToDeepLink;
        ViewHome.RefreshRequested += ReloadHomeFeed;
        ViewReels.PreviousRequested += () => _ = InstagramBridgeService.Instance.PreviousReelAsync();
        ViewReels.NextRequested += () => _ = InstagramBridgeService.Instance.NextReelAsync();
        ViewReels.PlayPauseRequested += () => _ = InstagramBridgeService.Instance.TogglePlayAsync();
        ViewReels.MuteRequested += () => _ = InstagramBridgeService.Instance.ToggleMuteAsync();
        ViewReels.LikeRequested += ToggleActiveReelLike;
        ViewReels.ShareRequested += ShareActiveReel;
        ViewReels.CommentsRequested += ToggleCommentsDrawer;
        ViewReels.HistoryOpenRequested += NavigateToDeepLink;
        ViewMessages.ViewModel.OpenThreadRequested += OpenDirectThread;
        ViewMessages.SyncRequested += ReloadInbox;

        ViewLogin.LoginSucceeded += OnLoginSucceeded;
        InstagramBridgeService.Instance.LoginStatusChanged += OnLoginStatusChanged;
        InstagramBridgeService.Instance.CommentsReceived += OnCommentsReceived;
        InstagramBridgeService.Instance.StatusMessageUpdated += (msg) =>
        {
            Dispatcher.Invoke(() => TxtFooterStatus.Text = msg);
        };
        InstagramBridgeService.Instance.ActiveReelChanged += (reel, isPlaying, isMuted, vol) =>
        {
            Dispatcher.Invoke(() =>
            {
                bool isNew = _activeReel == null || _activeReel.Id != reel.Id;
                bool playStateChanged = _isPlaying != isPlaying;
                bool muteStateChanged = _isMuted != isMuted;
                _isPlaying = isPlaying;
                _isMuted = isMuted;

                if (isNew)
                {
                    _activeComments.Clear();
                }
                _activeReel = reel;

                var likesInfo = !string.IsNullOrWhiteSpace(reel.FormattedLikes) ? $" • ❤️ {reel.FormattedLikes} likes" : "";
                var likeBtnLabel = reel.ReelLikeButtonText;

                var captionSnippet = !string.IsNullOrWhiteSpace(reel.Caption) ? $" • {reel.Caption}" : "";
                TxtFooterStatus.Text = $"@{reel.Username}{likesInfo}{captionSnippet}";

                BtnPlayPauseReel.Content = isPlaying ? "⏸ Pause (Space)" : "▶ Play (Space)";
                System.Windows.Automation.AutomationProperties.SetName(BtnPlayPauseReel, isPlaying ? "Pause reel. Press Spacebar." : "Play reel. Press Spacebar.");
                BtnMuteReel.Content = isMuted ? "🔇 Unmute (M)" : "🔊 Mute (M)";
                BtnLikeReel.Content = likeBtnLabel;

                var likePart = !string.IsNullOrWhiteSpace(reel.FormattedLikes) ? $"{reel.FormattedLikes} likes. " : "";
                var likeAnnounce = reel.IsLiked
                    ? $"Liked reel by {reel.Username}. {likePart}Press L to unlike."
                    : $"Like reel by {reel.Username}. {likePart}Press L to like.";
                System.Windows.Automation.AutomationProperties.SetName(BtnLikeReel, likeAnnounce);

                if (isNew)
                {
                    var announceMsg = $"Reel by @{reel.Username}. {(!string.IsNullOrWhiteSpace(reel.FormattedLikes) ? reel.FormattedLikes + " likes. " : "")}{reel.Caption}";
                    AccessibilityHelper.Announce(announceMsg);
                }
                else if (playStateChanged)
                {
                    AccessibilityHelper.Announce(isPlaying ? "Playing" : "Paused");
                }
                else if (muteStateChanged)
                {
                    AccessibilityHelper.Announce(isMuted ? "Audio muted" : "Audio unmuted");
                }
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
            NavSearch.IsEnabled = true;
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
            // Login and two-factor flows need the full window, so hand the whole area to the engine.
            HideNativePanel();
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
                ShowNativePanel("Home", "Home feed. Native post list on the right. Use Up and Down arrows to read posts, L to like, Enter to open in the web view, F6 to hide the panel.");
                InstagramBridgeService.Instance.Navigate("https://www.instagram.com/");
                break;

            case "Reels":
                ReelControlsPanel.Visibility = Visibility.Visible;
                ShowNativePanel("Reels", "Reels. The video plays in the web view on the left. The native panel on the right shows reel details and your watch history. Press F6 to hide the panel for full screen video.");
                BtnNextReel.Focus();
                InstagramBridgeService.Instance.NavigateToReels();
                break;

            case "Search":
                ReelControlsPanel.Visibility = Visibility.Collapsed;
                HideNativePanel();
                OpenSearchBar();
                break;

            case "Messages":
                ReelControlsPanel.Visibility = Visibility.Collapsed;
                CloseSearchBar();
                ShowNativePanel("Messages", "Direct messages. Native conversation list on the right. Use Up and Down arrows to read conversations, Enter to open one, F6 to hide the panel.");
                InstagramBridgeService.Instance.Navigate("https://www.instagram.com/direct/inbox/");
                break;

            case "Settings":
                ReelControlsPanel.Visibility = Visibility.Collapsed;
                CloseSearchBar();
                HideNativePanel();
                OpenSettingsDialog();
                break;
        }
    }

    /// <summary>
    /// Shows the native panel for a tab, unless the user has hidden it or the comments drawer
    /// currently occupies the same slot. Also marks exactly one view model active so a hidden
    /// view never speaks.
    /// </summary>
    private void ShowNativePanel(string tag, string? announcement = null)
    {
        _panelManuallyHidden = false;

        ViewHome.ViewModel.IsActive = tag == "Home";
        ViewReels.ViewModel.IsActive = tag == "Reels";
        ViewMessages.ViewModel.IsActive = tag == "Messages";

        ViewHome.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        ViewReels.Visibility = tag == "Reels" ? Visibility.Visible : Visibility.Collapsed;
        ViewMessages.Visibility = tag == "Messages" ? Visibility.Visible : Visibility.Collapsed;

        // The comments drawer shares this slot, so do not cover it.
        if (CommentsDrawer.Visibility != Visibility.Visible)
        {
            NativePanelHost.Width = tag == "Reels" ? 430 : 520;
            NativePanelHost.Visibility = Visibility.Visible;
        }

        if (!string.IsNullOrWhiteSpace(announcement))
        {
            AccessibilityHelper.Announce(announcement);
        }
    }

    /// <summary>Hides the native panel and stops its view model from speaking.</summary>
    private void HideNativePanel()
    {
        NativePanelHost.Visibility = Visibility.Collapsed;
        ViewHome.ViewModel.IsActive = false;
        ViewReels.ViewModel.IsActive = false;
        ViewMessages.ViewModel.IsActive = false;
    }

    /// <summary>F6: hide the panel for a full width web view, or bring it back.</summary>
    private void ToggleNativePanel()
    {
        var panelTab = _currentTab is "Home" or "Reels" or "Messages";
        if (!panelTab)
        {
            AccessibilityHelper.Announce("The native panel is available on the Home, Reels and Messages tabs.");
            return;
        }

        if (NativePanelHost.Visibility == Visibility.Visible)
        {
            _panelManuallyHidden = true;
            HideNativePanel();
            AccessibilityHelper.Announce("Native panel hidden. The web view is now full width. Press F6 to show the panel again.");
        }
        else
        {
            ShowNativePanel(_currentTab);
            AccessibilityHelper.Announce("Native panel shown.");
            PlaceFocusInPanel(_currentTab);
        }
    }

    private void PlaceFocusInPanel(string tag)
    {
        switch (tag)
        {
            case "Home": ViewHome.FocusPosts(); break;
            case "Messages": ViewMessages.FocusConversations(); break;
        }
    }

    /// <summary>
    /// True when keyboard focus sits inside the native panel. Arrow keys then belong to the
    /// list being read rather than to reel scrolling, which is what a keyboard user expects.
    /// </summary>
    private bool IsFocusInNativePanel()
    {
        DependencyObject? element = Keyboard.FocusedElement as DependencyObject;
        while (element != null)
        {
            if (ReferenceEquals(element, NativePanelHost)) return true;
            element = element switch
            {
                Visual => VisualTreeHelper.GetParent(element),
                FrameworkContentElement fce => fce.Parent,
                _ => null
            };
        }
        return false;
    }

    private void OpenSearchBar()
    {
        SearchBarPanel.Visibility = Visibility.Visible;
        TxtSearchInput.Focus();
        TxtSearchInput.SelectAll();
        AccessibilityHelper.Announce("Search and open link bar opened. Paste or type an Instagram reel or post link and press Enter, or press Escape to close.");
    }

    private void CloseSearchBar()
    {
        SearchBarPanel.Visibility = Visibility.Collapsed;
        if (_currentTab == "Search")
        {
            if (NavHome.IsEnabled) NavHome.IsChecked = true;
            else NavEngine.IsChecked = true;
        }
    }

    private void BtnCloseSearch_Click(object sender, RoutedEventArgs e) => CloseSearchBar();

    private void TxtSearchInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenEnteredSearchLink();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseSearchBar();
            e.Handled = true;
        }
    }

    private void BtnOpenSearchLink_Click(object sender, RoutedEventArgs e) => OpenEnteredSearchLink();

    private void BtnPasteAndOpen_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    TxtSearchInput.Text = text;
                    OpenEnteredSearchLink();
                    return;
                }
            }
            AccessibilityHelper.Announce("Clipboard is empty or does not contain text.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("SEARCH", "Failed reading clipboard", ex);
            AccessibilityHelper.Announce("Could not read clipboard.");
        }
    }

    private void OpenEnteredSearchLink()
    {
        var raw = TxtSearchInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            AccessibilityHelper.Announce("Please enter an Instagram reel or post URL.");
            TxtSearchInput.Focus();
            return;
        }

        var normalized = DeepLinkService.NormalizeInstagramUrl(raw);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase)) normalized = raw;
            else normalized = "https://www.instagram.com/" + raw.TrimStart('/');
        }

        SearchBarPanel.Visibility = Visibility.Collapsed;
        NavigateToDeepLink(normalized);
    }

    private void OpenSettingsDialog()
    {
        SettingsOverlay.Visibility = Visibility.Visible;
        RefreshAppUriStatus();
        ChkRegisterAppUri.Focus();
        AccessibilityHelper.Announce("Settings dialog opened. Use Tab to navigate options or Escape to close.");
    }

    private void CloseSettingsDialog()
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
        if (_currentTab == "Settings")
        {
            if (NavHome.IsEnabled) NavHome.IsChecked = true;
            else NavEngine.IsChecked = true;
        }
        else
        {
            if (NavReels.IsChecked == true) BtnNextReel.Focus();
            else NavHome.Focus();
        }
        AccessibilityHelper.Announce("Settings dialog closed.");
    }

    private void BtnCloseSettings_Click(object sender, RoutedEventArgs e) => CloseSettingsDialog();

    private void RefreshAppUriStatus()
    {
        bool isRegistered = AppUriHandlerService.Instance.IsRegistered();
        ChkRegisterAppUri.IsChecked = isRegistered;
        TxtAppUriStatus.Text = isRegistered 
            ? "Active (Links configured to open in WinInstagram)" 
            : "Inactive (Not registered)";
        TxtAppUriStatus.Foreground = isRegistered 
            ? new SolidColorBrush(Color.FromRgb(78, 201, 176)) 
            : new SolidColorBrush(Color.FromRgb(200, 200, 200));
    }

    private async void ChkRegisterAppUri_Checked(object sender, RoutedEventArgs e)
    {
        TxtAppUriStatus.Text = "Registering...";
        AccessibilityHelper.Announce("Registering WinInstagram for instagram.com web and protocol links...");
        var (success, message) = await AppUriHandlerService.Instance.RegisterAsync();
        RefreshAppUriStatus();
        AccessibilityHelper.Announce(message);
    }

    private async void ChkRegisterAppUri_Unchecked(object sender, RoutedEventArgs e)
    {
        TxtAppUriStatus.Text = "Unregistering...";
        AccessibilityHelper.Announce("Unregistering WinInstagram links...");
        var (success, message) = await AppUriHandlerService.Instance.UnregisterAsync();
        RefreshAppUriStatus();
        AccessibilityHelper.Announce(message);
    }

    private void BtnShareReel_Click(object sender, RoutedEventArgs e) => ShareActiveReel();

    private void ShareActiveReel()
    {
        if (_activeReel == null)
        {
            AccessibilityHelper.Announce("No active reel to share.");
            return;
        }

        string shareUrl = !string.IsNullOrWhiteSpace(_activeReel.Id) && _activeReel.Id.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? _activeReel.Id
            : "https://www.instagram.com/reels/";

        shareUrl = DeepLinkService.NormalizeInstagramUrl(shareUrl);
        if (string.IsNullOrWhiteSpace(shareUrl))
        {
            shareUrl = "https://www.instagram.com/reels/";
        }

        try
        {
            Clipboard.SetText(shareUrl);
            var announce = $"Reel link copied to clipboard: {shareUrl}. You can now share it with friends.";
            TxtFooterStatus.Text = announce;
            AccessibilityHelper.Announce(announce);
        }
        catch (Exception ex)
        {
            AppLogger.Error("SHARE", "Failed to copy reel link to clipboard", ex);
            AccessibilityHelper.Announce("Failed to copy link to clipboard.");
        }
    }

    private void NavigateToDeepLink(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        AppLogger.Info("DEEP_LINK", $"Handling deep link: {url}");

        // Bring window to foreground
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        Focus();

        var normalized = DeepLinkService.NormalizeInstagramUrl(url);
        if (string.IsNullOrWhiteSpace(normalized)) normalized = url;

        // If it's a reel link, switch to reels tab and navigate
        if (normalized.Contains("/reel/") || normalized.Contains("/reels/"))
        {
            NavReels.IsChecked = true;
            InstagramBridgeService.Instance.Navigate(normalized);
            AccessibilityHelper.Announce($"Opening shared reel: {normalized}");
        }
        else
        {
            NavHome.IsChecked = true;
            InstagramBridgeService.Instance.Navigate(normalized);
            AccessibilityHelper.Announce($"Opening shared link: {normalized}");
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

    private void OnCommentsReceived(List<InstagramComment> comments)
    {
        Dispatcher.Invoke(() =>
        {
            if (comments == null || comments.Count == 0) return;

            // Merge comments
            foreach (var c in comments)
            {
                var existing = _activeComments.FirstOrDefault(x => (!string.IsNullOrEmpty(x.Id) && x.Id == c.Id) || (x.Username == c.Username && x.Text == c.Text));
                if (existing == null)
                {
                    _activeComments.Add(c);
                }
                else
                {
                    existing.LikesCount = c.LikesCount;
                    existing.IsLiked = c.IsLiked;
                }
            }

            TxtCommentsHeader.Text = $"💬 Comments ({_activeComments.Count})";
            if (CommentsDrawer.Visibility == Visibility.Visible)
            {
                AccessibilityHelper.Announce($"{_activeComments.Count} comments available. Use Up and Down arrow keys to explore comments.");
            }
        });
    }

    private void BtnPrevReel_Click(object sender, RoutedEventArgs e) => _ = InstagramBridgeService.Instance.PreviousReelAsync();
    private void BtnPlayPauseReel_Click(object sender, RoutedEventArgs e) => _ = InstagramBridgeService.Instance.TogglePlayAsync();
    private void BtnNextReel_Click(object sender, RoutedEventArgs e) => _ = InstagramBridgeService.Instance.NextReelAsync();
    private void BtnMuteReel_Click(object sender, RoutedEventArgs e) => _ = InstagramBridgeService.Instance.ToggleMuteAsync();
    private void BtnLikeReel_Click(object sender, RoutedEventArgs e) => ToggleActiveReelLike();
    private void BtnCommentsReel_Click(object sender, RoutedEventArgs e) => ToggleCommentsDrawer();

    private void ToggleActiveReelLike()
    {
        if (_activeReel != null)
        {
            _activeReel.IsLiked = !_activeReel.IsLiked;
            if (_activeReel.IsLiked) _activeReel.LikesCount++;
            else if (_activeReel.LikesCount > 0) _activeReel.LikesCount--;

            BtnLikeReel.Content = _activeReel.ReelLikeButtonText;
            var likePart = !string.IsNullOrWhiteSpace(_activeReel.FormattedLikes) ? $"{_activeReel.FormattedLikes} likes. " : "";
            var likeAnnounce = _activeReel.IsLiked
                ? $"Liked reel by {_activeReel.Username}. {likePart}Press L to unlike."
                : $"Like reel by {_activeReel.Username}. {likePart}Press L to like.";
            System.Windows.Automation.AutomationProperties.SetName(BtnLikeReel, likeAnnounce);
            AccessibilityHelper.Announce(_activeReel.IsLiked ? "Liked reel" : "Unliked reel");
        }
        _ = InstagramBridgeService.Instance.ToggleLikeAsync();
    }

    private void ToggleCommentsDrawer()
    {
        if (CommentsDrawer.Visibility == Visibility.Visible)
        {
            CloseCommentsDrawer();
        }
        else
        {
            OpenCommentsDrawer();
        }
    }

    private void OpenCommentsDrawer()
    {
        CommentsDrawer.Visibility = Visibility.Visible;
        // The drawer takes over the right-hand slot normally used by the native panel.
        NativePanelHost.Visibility = Visibility.Collapsed;
        TxtCommentsHeader.Text = _activeReel != null && !string.IsNullOrWhiteSpace(_activeReel.Username)
            ? $"💬 Comments (@{_activeReel.Username})"
            : "💬 Comments";

        _ = InstagramBridgeService.Instance.ToggleCommentsAsync();
        _ = InstagramBridgeService.Instance.ScrapeCommentsAsync();

        TxtDrawerComment.Focus();
        AccessibilityHelper.Announce($"Comments opened for @{_activeReel?.Username ?? "reel"}. Press Tab to explore comments list, or type a comment and press Enter. Press Escape to close comments.");
    }

    private void CloseCommentsDrawer()
    {
        CommentsDrawer.Visibility = Visibility.Collapsed;
        _ = InstagramBridgeService.Instance.CloseCommentsAsync();

        // Hand the slot back to the native panel unless the user hid it deliberately.
        if (!_panelManuallyHidden && _currentTab is "Home" or "Reels" or "Messages")
        {
            NativePanelHost.Visibility = Visibility.Visible;
        }

        BtnCommentsReel.Focus();
        AccessibilityHelper.Announce("Comments closed. Returned to reel playback.");
    }

    private void BtnCloseComments_Click(object sender, RoutedEventArgs e) => CloseCommentsDrawer();

    private void BtnLikeCommentItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is InstagramComment comment)
        {
            ToggleCommentLike(comment);
        }
    }

    private void ListComments_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Space)
        {
            if (ListComments.SelectedItem is InstagramComment comment)
            {
                ToggleCommentLike(comment);
                e.Handled = true;
            }
        }
    }

    private void ToggleCommentLike(InstagramComment comment)
    {
        _ = InstagramBridgeService.Instance.LikeCommentAsync(comment.Index, comment.Username, comment.Text);
        comment.IsLiked = !comment.IsLiked;
        if (comment.IsLiked) comment.LikesCount++;
        else if (comment.LikesCount > 0) comment.LikesCount--;

        var msg = comment.IsLiked
            ? $"Liked comment by @{comment.Username}."
            : $"Unliked comment by @{comment.Username}.";
        AccessibilityHelper.Announce(msg);
    }

    private void TxtDrawerComment_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitDrawerComment();
            e.Handled = true;
        }
    }

    private void BtnDrawerPostComment_Click(object sender, RoutedEventArgs e) => SubmitDrawerComment();

    private void SubmitDrawerComment()
    {
        var text = TxtDrawerComment.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        _ = InstagramBridgeService.Instance.PostCommentAsync(text);

        var newComment = new InstagramComment
        {
            Id = Guid.NewGuid().ToString("N"),
            Index = _activeComments.Count,
            Username = "You",
            Text = text,
            CreatedAt = "Just now",
            LikesCount = 0,
            IsLiked = false
        };
        _activeComments.Insert(0, newComment);
        TxtDrawerComment.Clear();
        AccessibilityHelper.Announce($"Comment posted: {text}");
    }

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
        // 0. If update dialog is visible, handle Enter and Escape
        if (UpdateDialog.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                DismissUpdate();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && !_isDownloadingUpdate)
            {
                StartInstallUpdate();
                e.Handled = true;
                return;
            }
        }

        // 0.2. F6 shows or hides the native accessible panel
        if (e.Key == Key.F6)
        {
            ToggleNativePanel();
            e.Handled = true;
            return;
        }

        // 0.4. If search bar is visible, Escape key closes it
        if (SearchBarPanel.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            CloseSearchBar();
            e.Handled = true;
            return;
        }

        // 0.5. If settings overlay is visible, Escape key closes it
        if (SettingsOverlay.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            CloseSettingsDialog();
            e.Handled = true;
            return;
        }

        // 1. If comments drawer is visible, Escape key closes it
        if (e.Key == Key.Escape && CommentsDrawer.Visibility == Visibility.Visible)
        {
            CloseCommentsDrawer();
            e.Handled = true;
            return;
        }

        // 2. Global Tab Navigation & Update hotkeys (Ctrl+1: Home, Ctrl+2: Reels, Ctrl+3: Search, Ctrl+4: Messages, Ctrl+5: Settings, Ctrl+L: Account, Ctrl+U: Updates)
        var isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (isCtrl)
        {
            switch (e.Key)
            {
                case Key.U:
                    CheckForUpdatesManual();
                    e.Handled = true;
                    return;

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
                    NavSearch.IsChecked = true;
                    e.Handled = true;
                    return;

                case Key.D4:
                case Key.NumPad4:
                    if (NavMessages.IsEnabled) NavMessages.IsChecked = true;
                    e.Handled = true;
                    return;

                case Key.L:
                    NavEngine.IsChecked = true;
                    e.Handled = true;
                    return;

                case Key.D5:
                case Key.NumPad5:
                case Key.OemComma:
                    NavSettings.IsChecked = true;
                    e.Handled = true;
                    return;
            }
        }

        // 3. If user is currently typing in any text box, DO NOT capture single-character hotkeys
        if (Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        // 4. Hotkeys when on Reels tab
        if (NavReels.IsChecked == true)
        {
            switch (e.Key)
            {
                case Key.J:
                    _ = InstagramBridgeService.Instance.NextReelAsync();
                    e.Handled = true;
                    break;

                case Key.K:
                    _ = InstagramBridgeService.Instance.PreviousReelAsync();
                    e.Handled = true;
                    break;

                // Arrow keys belong to the list focused inside the native panel, if any.
                case Key.PageDown:
                case Key.Down:
                    if (IsFocusInNativePanel()) return;
                    _ = InstagramBridgeService.Instance.NextReelAsync();
                    e.Handled = true;
                    break;

                case Key.PageUp:
                case Key.Up:
                    if (IsFocusInNativePanel()) return;
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
                    ToggleActiveReelLike();
                    e.Handled = true;
                    break;

                case Key.S:
                    ShareActiveReel();
                    e.Handled = true;
                    break;

                case Key.C:
                    ToggleCommentsDrawer();
                    e.Handled = true;
                    break;
            }
        }
    }

    private void BtnTogglePanel_Click(object sender, RoutedEventArgs e) => ToggleNativePanel();

    private void ReloadHomeFeed()
    {
        ViewHome.ViewModel.Status = "Refreshing your timeline...";
        InstagramBridgeService.Instance.Navigate("https://www.instagram.com/");
        AccessibilityHelper.Announce("Refreshing your timeline.");
    }

    private void ReloadInbox()
    {
        ViewMessages.ViewModel.Status = "Reloading your inbox...";
        InstagramBridgeService.Instance.Navigate("https://www.instagram.com/direct/inbox/");
        AccessibilityHelper.Announce("Reloading your inbox.");
    }

    /// <summary>Opens a DM thread in the engine; the parsed messages arrive asynchronously.</summary>
    private void OpenDirectThread(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            AccessibilityHelper.Announce("That conversation has no thread id, so it cannot be opened in the web view.");
            return;
        }

        InstagramBridgeService.Instance.Navigate($"https://www.instagram.com/direct/t/{threadId}/");
    }

    private void BtnCheckUpdate_Click(object sender, RoutedEventArgs e) => CheckForUpdatesManual();

    private void CheckForUpdatesManual()
    {
        AccessibilityHelper.Announce("Checking for updates...");
        TxtFooterStatus.Text = "Checking GitHub for updates...";
        _ = Task.Run(() => UpdateService.Instance.CheckForUpdatesAsync(isManual: true));
    }

    private void OnUpdateAvailable(UpdateInfo info)
    {
        Dispatcher.Invoke(() =>
        {
            _pendingUpdate = info;
            TxtUpdateTag.Text = info.TagName;
            TxtUpdateVersions.Text = $"Current: v{info.CurrentVersion} → Latest: {info.TagName}";
            TxtUpdateNotes.Text = string.IsNullOrWhiteSpace(info.ReleaseNotes) 
                ? "New bug fixes and performance improvements." 
                : info.ReleaseNotes;
            UpdateProgressBar.Value = 0;
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
            UpdateButtonsPanel.IsEnabled = true;
            UpdateDialog.Visibility = Visibility.Visible;
            BtnInstallUpdate.Focus();

            AccessibilityHelper.Announce($"Update available: {info.TagName}. Press Enter to download and install, or Escape to dismiss.");
        });
    }

    private void OnUpdateCheckStatusUpdated(string message)
    {
        Dispatcher.Invoke(() =>
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                TxtFooterStatus.Text = message;
                AccessibilityHelper.Announce(message);
            }
        });
    }

    private void BtnInstallUpdate_Click(object sender, RoutedEventArgs e) => StartInstallUpdate();

    private void StartInstallUpdate()
    {
        if (_pendingUpdate == null || _isDownloadingUpdate) return;
        _isDownloadingUpdate = true;
        UpdateButtonsPanel.IsEnabled = false;
        UpdateProgressPanel.Visibility = Visibility.Visible;
        UpdateProgressBar.Value = 0;
        TxtUpdateProgress.Text = "Starting download...";
        AccessibilityHelper.Announce("Starting update download...");

        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<double>(pct =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateProgressBar.Value = pct;
                        TxtUpdateProgress.Text = $"Downloading update: {(int)pct}%...";
                        if ((int)pct % 25 == 0 && (int)pct > 0 && (int)pct < 100)
                        {
                            AccessibilityHelper.Announce($"Download {(int)pct} percent");
                        }
                    });
                });

                var newExe = await UpdateService.Instance.DownloadUpdateAsync(_pendingUpdate, progress);

                Dispatcher.Invoke(() =>
                {
                    TxtUpdateProgress.Text = "Download complete. Relaunching WinInstagram...";
                    AccessibilityHelper.Announce("Download complete. Relaunching WinInstagram now.");
                });

                await Task.Delay(800);
                UpdateService.Instance.ApplyUpdateAndRelaunch(newExe);
            }
            catch (Exception ex)
            {
                AppLogger.Error("UPDATER", "Failed to download/apply update", ex);
                Dispatcher.Invoke(() =>
                {
                    _isDownloadingUpdate = false;
                    UpdateButtonsPanel.IsEnabled = true;
                    UpdateProgressPanel.Visibility = Visibility.Collapsed;
                    TxtFooterStatus.Text = $"Update failed: {ex.Message}";
                    AccessibilityHelper.Announce($"Update failed: {ex.Message}");
                });
            }
        });
    }

    private void BtnDismissUpdate_Click(object sender, RoutedEventArgs e) => DismissUpdate();

    private void DismissUpdate()
    {
        UpdateDialog.Visibility = Visibility.Collapsed;
        _pendingUpdate = null;
        _isDownloadingUpdate = false;
        if (NavReels.IsChecked == true) BtnNextReel.Focus();
        else NavHome.Focus();
        AccessibilityHelper.Announce("Update dismissed.");
    }
}