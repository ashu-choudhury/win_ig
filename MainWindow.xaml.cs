using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinInstagram.Models;
using WinInstagram.Services;
using WinInstagram.ViewModels;

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

    /// <summary>The native panel currently on screen. It is not always a navigation tab, because
    /// the discovery panels (Stories, Profile, Saved, Activity) are opened on demand.</summary>
    private string _currentPanel = string.Empty;

    private DispatcherTimer? _activityPollTimer;
    private bool _activityPollAttached;
    private bool _restoredReadingPosition;
    private bool _restoredThread;
    private bool _resumedReel;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AccessibilityHelper.RegisterAnnouncementTarget(TxtFooterStatus);
        ListComments.ItemsSource = _activeComments;

        // Preferences are applied before anything can speak, so the first announcement already
        // respects the chosen verbosity and the saved keyboard map.
        var prefs = AppSettingsService.Instance.Settings;
        Announcements.Verbosity = prefs.Verbosity;
        ShortcutService.Instance.LoadFromSettings(prefs);
        AppSettingsService.Instance.Changed += _ => ApplyPreferences();

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
        ViewHome.ShareRequested += ShareCurrent;
        ViewHome.DescribeRequested += () => _ = DescribeCurrentAsync(altOnly: false);
        ViewReels.PreviousRequested += () => _ = InstagramBridgeService.Instance.PreviousReelAsync();
        ViewReels.NextRequested += () => _ = InstagramBridgeService.Instance.NextReelAsync();
        ViewReels.PlayPauseRequested += () => _ = InstagramBridgeService.Instance.TogglePlayAsync();
        ViewReels.MuteRequested += () => _ = InstagramBridgeService.Instance.ToggleMuteAsync();
        ViewReels.LikeRequested += ToggleActiveReelLike;
        ViewReels.ShareRequested += ShareActiveReel;
        ViewReels.CommentsRequested += ToggleCommentsDrawer;
        ViewReels.HistoryOpenRequested += NavigateToDeepLink;
        ViewReels.CaptionsRequested += () => _ = ReadCaptionsAsync();
        ViewReels.DescribeRequested += () => _ = DescribeCurrentAsync(altOnly: false);
        ViewReels.SaveRequested += () => _ = SaveCurrentMediaAsync();
        ViewMessages.ViewModel.OpenThreadRequested += OpenDirectThread;
        ViewMessages.SyncRequested += ReloadInbox;

        // Discovery panels. They own their own engine calls and raise an open request for anything
        // that needs real navigation, so the shell stays the single navigator.
        ViewStories.ViewModel.OpenStoryRequested += NavigateToDeepLink;
        ViewProfile.ViewModel.OpenInEngineRequested += NavigateToDeepLink;
        ViewSearch.ViewModel.OpenResultRequested += NavigateToDeepLink;
        ViewSaved.OpenPostRequested += NavigateToDeepLink;
        ViewActivity.ViewModel.OpenItemRequested += NavigateToDeepLink;

        // Preferences and the command palette.
        ViewSettings.ViewModel.SettingsChanged += ApplyPreferences;
        ViewSettings.ViewModel.ChooseSaveFolderRequested += ChooseMediaFolder;
        ViewPalette.CommandInvoked += RunPaletteCommand;
        ViewPalette.DismissRequested += ClosePalette;

        // Reading-position memory: remember quietly on every move, persist when the app closes.
        ViewHome.ViewModel.PropertyChanged += OnHomePropertyChanged;
        ViewMessages.ViewModel.PropertyChanged += OnMessagesPropertyChanged;
        InstagramBridgeService.Instance.FeedReceived += _ => RestoreReadingPosition();
        InstagramBridgeService.Instance.ConversationsReceived += RestoreLastThread;

        StartActivityPoll();

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

                // Reading-position memory for reels: the page URL is the reel identity.
                if (isNew && !string.IsNullOrWhiteSpace(reel.Id))
                {
                    AppSettingsService.Instance.Settings.LastReadReelUrl = reel.Id;
                }

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
                    // The creator is essential; likes and caption are detail; captions are extra.
                    var likesPart = !string.IsNullOrWhiteSpace(reel.FormattedLikes) ? $", {reel.FormattedLikes} likes" : string.Empty;
                    Announcements.Say($"Reel by @{reel.Username}{likesPart}.");
                    Announcements.Detail(!string.IsNullOrWhiteSpace(reel.Caption) ? $"Caption: {reel.Caption}" : "This reel has no caption.");
                    Announcements.Extra(!string.IsNullOrWhiteSpace(reel.AltText) ? $"Visual description: {reel.AltText}" : string.Empty);
                }
                else if (playStateChanged)
                {
                    Announcements.Detail(isPlaying ? "Playing" : "Paused");
                }
                else if (muteStateChanged)
                {
                    Announcements.Detail(isMuted ? "Audio muted" : "Audio unmuted");
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
            _currentPanel = string.Empty;
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
                ResumeLastReelIfAny();
                break;

            case "Search":
                ReelControlsPanel.Visibility = Visibility.Collapsed;
                // Both search surfaces stay available: native text results on the right, and the link
                // bar at the top for opening one specific reel or post.
                SearchBarPanel.Visibility = Visibility.Visible;
                ShowNativePanel("Search");
                ViewSearch.FocusSearch();
                Announcements.Say("Instagram search. Type a name, hashtag or keyword in the search box and press Enter. Use the link bar at the top to open a specific reel or post link.");
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
                _currentPanel = string.Empty;
                OpenSettingsDialog();
                break;
        }
    }

    /// <summary>
    /// Shows one native panel, or the comments drawer's slot if it is open. Exactly one view model
    /// is marked active, which is what keeps a hidden panel from speaking.
    /// </summary>
    private void ShowNativePanel(string tag, string? announcement = null)
    {
        _panelManuallyHidden = false;
        _currentPanel = tag;

        ViewHome.ViewModel.IsActive = tag == "Home";
        ViewReels.ViewModel.IsActive = tag == "Reels";
        ViewMessages.ViewModel.IsActive = tag == "Messages";
        ViewStories.ViewModel.IsActive = tag == "Stories";
        ViewProfile.ViewModel.IsActive = tag == "Profile";
        ViewSearch.ViewModel.IsActive = tag == "Search";
        ViewSaved.ViewModel.IsActive = tag == "Saved";
        ViewActivity.ViewModel.IsActive = tag == "Activity";

        ViewHome.Visibility = VisibleFor(tag, "Home");
        ViewReels.Visibility = VisibleFor(tag, "Reels");
        ViewMessages.Visibility = VisibleFor(tag, "Messages");
        ViewStories.Visibility = VisibleFor(tag, "Stories");
        ViewProfile.Visibility = VisibleFor(tag, "Profile");
        ViewSearch.Visibility = VisibleFor(tag, "Search");
        ViewSaved.Visibility = VisibleFor(tag, "Saved");
        ViewActivity.Visibility = VisibleFor(tag, "Activity");

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

    private static Visibility VisibleFor(string tag, string wanted) =>
        tag == wanted ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Hides the native panel and stops every view model from speaking.</summary>
    private void HideNativePanel()
    {
        NativePanelHost.Visibility = Visibility.Collapsed;
        ViewHome.Visibility = Visibility.Collapsed;
        ViewReels.Visibility = Visibility.Collapsed;
        ViewMessages.Visibility = Visibility.Collapsed;
        ViewStories.Visibility = Visibility.Collapsed;
        ViewProfile.Visibility = Visibility.Collapsed;
        ViewSearch.Visibility = Visibility.Collapsed;
        ViewSaved.Visibility = Visibility.Collapsed;
        ViewActivity.Visibility = Visibility.Collapsed;
        ViewHome.ViewModel.IsActive = false;
        ViewReels.ViewModel.IsActive = false;
        ViewMessages.ViewModel.IsActive = false;
        ViewStories.ViewModel.IsActive = false;
        ViewProfile.ViewModel.IsActive = false;
        ViewSearch.ViewModel.IsActive = false;
        ViewSaved.ViewModel.IsActive = false;
        ViewActivity.ViewModel.IsActive = false;
    }

    /// <summary>F6: hide the panel for a full width web view, or bring the same panel back.</summary>
    private void ToggleNativePanel()
    {
        if (string.IsNullOrWhiteSpace(_currentPanel))
        {
            Announcements.Say("The native panel is available on the Home, Reels and Messages tabs, and on the Stories, Profile, Search, Saved and Activity panels.");
            return;
        }

        if (NativePanelHost.Visibility == Visibility.Visible)
        {
            _panelManuallyHidden = true;
            HideNativePanel();
            Announcements.Say("Panel hidden. The web view is now full width. Press F6 to show the panel again.");
        }
        else
        {
            var tag = _currentPanel;
            ShowNativePanel(tag);
            Announcements.Say("Panel shown.");
            PlaceFocusInPanel(tag);
        }
    }

    private void PlaceFocusInPanel(string tag)
    {
        switch (tag)
        {
            case "Home": ViewHome.FocusPosts(); break;
            case "Messages": ViewMessages.FocusConversations(); break;
            case "Stories": ViewStories.FocusStories(); break;
            case "Profile": ViewProfile.FocusPosts(); break;
            case "Search": ViewSearch.FocusSearch(); break;
            case "Saved": ViewSaved.FocusCollections(); break;
            case "Activity": ViewActivity.FocusItems(); break;
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
            Announcements.Say("No active reel to share.");
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

        // A bare link is useless without context, so the clipboard gets caption, creator and link.
        CopyShareText(ShareText.Build(_activeReel.Caption, _activeReel.Username, shareUrl));
    }

    // ---- Command palette ----------------------------------------------------------------------

    private void BtnPalette_Click(object sender, RoutedEventArgs e) => OpenPalette();

    /// <summary>Ctrl+K: the one place where every native feature is listed and runnable.</summary>
    private void OpenPalette()
    {
        ViewPalette.Visibility = Visibility.Visible;
        ViewPalette.FocusFilter();
        Announcements.Say("Command palette. Type to filter, press Enter to run the highlighted command, Escape to close.");
    }

    private void ClosePalette()
    {
        ViewPalette.Visibility = Visibility.Collapsed;
    }

    /// <summary>Runs one palette command. Every id in <see cref="PaletteCommandIds"/> is handled.</summary>
    private void RunPaletteCommand(string id)
    {
        ClosePalette();

        switch (id)
        {
            case PaletteCommandIds.OpenHomePanel: if (NavHome.IsEnabled) NavHome.IsChecked = true; break;
            case PaletteCommandIds.OpenReelsPanel: if (NavReels.IsEnabled) NavReels.IsChecked = true; break;
            case PaletteCommandIds.OpenMessagesPanel: if (NavMessages.IsEnabled) NavMessages.IsChecked = true; break;
            case PaletteCommandIds.OpenSearchPanel: NavSearch.IsChecked = true; break;
            case PaletteCommandIds.OpenStoriesPanel: OpenDiscoveryPanel("Stories"); break;
            case PaletteCommandIds.OpenSavedPanel: OpenDiscoveryPanel("Saved"); break;
            case PaletteCommandIds.OpenActivityPanel: OpenDiscoveryPanel("Activity"); break;
            case PaletteCommandIds.OpenProfilePanel: OpenCreatorProfile(); break;
            case PaletteCommandIds.TogglePanel: ToggleNativePanel(); break;

            case PaletteCommandIds.Refresh: RefreshCurrentPanel(); break;
            case PaletteCommandIds.NextReel: _ = InstagramBridgeService.Instance.NextReelAsync(); break;
            case PaletteCommandIds.PreviousReel: _ = InstagramBridgeService.Instance.PreviousReelAsync(); break;
            case PaletteCommandIds.PlayPause: _ = InstagramBridgeService.Instance.TogglePlayAsync(); break;
            case PaletteCommandIds.Mute: _ = InstagramBridgeService.Instance.ToggleMuteAsync(); break;
            case PaletteCommandIds.Like: ToggleLikeCurrent(); break;
            case PaletteCommandIds.Comments: ToggleCommentsDrawer(); break;
            case PaletteCommandIds.Share: ShareCurrent(); break;
            case PaletteCommandIds.SaveMedia: _ = SaveCurrentMediaAsync(); break;

            case PaletteCommandIds.ReadCaptions: _ = ReadCaptionsAsync(); break;
            case PaletteCommandIds.ReadAltText: _ = DescribeCurrentAsync(altOnly: true); break;
            case PaletteCommandIds.ReadDescription: _ = DescribeCurrentAsync(altOnly: false); break;
            case PaletteCommandIds.CheckControls: _ = SpeakControlHealthAsync(); break;

            case PaletteCommandIds.CycleVerbosity: CycleVerbosity(); break;
            case PaletteCommandIds.Settings: if (NavSettings.IsChecked != true) NavSettings.IsChecked = true; break;
            case PaletteCommandIds.ShortcutHelp: AnnounceShortcutHelp(); break;
            case PaletteCommandIds.CheckUpdates: CheckForUpdatesManual(); break;

            default:
                AppLogger.Warn("PALETTE", $"Unknown palette command '{id}'.");
                break;
        }
    }

    /// <summary>
    /// Shows one of the discovery panels. They load from Instagram's own API, so nothing has to be
    /// navigated first and the web view keeps playing whatever it was playing.
    /// </summary>
    private void OpenDiscoveryPanel(string tag)
    {
        if (!InstagramBridgeService.Instance.IsLoggedIn)
        {
            Announcements.Say("Sign in to Instagram first, then open this panel.");
            return;
        }

        ShowNativePanel(tag);

        switch (tag)
        {
            case "Stories":
                _ = ViewStories.ViewModel.RefreshAsync();
                PlaceFocusInPanel(tag);
                break;
            case "Saved":
                _ = ViewSaved.ViewModel.RefreshAsync();
                PlaceFocusInPanel(tag);
                break;
            case "Activity":
                _ = ViewActivity.ViewModel.RefreshAsync(announceNew: true);
                PlaceFocusInPanel(tag);
                break;
        }
    }

    /// <summary>Opens the profile of whoever owns the media in front of the user.</summary>
    private void OpenCreatorProfile()
    {
        var username = _activeReel != null && !string.IsNullOrWhiteSpace(_activeReel.Username) &&
                       !_activeReel.Username.Equals("Instagram User", StringComparison.OrdinalIgnoreCase)
            ? _activeReel.Username
            : ViewHome.ViewModel.SelectedPost?.Username ?? string.Empty;

        if (string.IsNullOrWhiteSpace(username))
        {
            Announcements.Say("No creator is known yet. Open a post or a reel first, then ask for the profile.");
            return;
        }

        ShowNativePanel("Profile");
        _ = ViewProfile.ViewModel.LoadAsync(username);
    }

    private void RefreshCurrentPanel()
    {
        switch (_currentPanel)
        {
            case "Messages": ReloadInbox(); break;
            case "Stories": _ = ViewStories.ViewModel.RefreshAsync(); break;
            case "Saved": _ = ViewSaved.ViewModel.RefreshAsync(); break;
            case "Activity": _ = ViewActivity.ViewModel.RefreshAsync(announceNew: true); break;
            case "Reels":
                InstagramBridgeService.Instance.NavigateToReels();
                Announcements.Say("Refreshing reels.");
                break;
            default: ReloadHomeFeed(); break;
        }
    }

    // ---- Media actions -------------------------------------------------------------------------

    /// <summary>Like targets whatever the user is actually reading, not always the reel.</summary>
    private void ToggleLikeCurrent()
    {
        if (_currentPanel == "Home")
        {
            _ = ViewHome.ViewModel.ToggleLikeCurrentAsync();
            return;
        }
        ToggleActiveReelLike();
    }

    private void ShareCurrent()
    {
        if (_currentPanel == "Home" && ViewHome.ViewModel.SelectedPost is FeedPost post)
        {
            var url = string.IsNullOrWhiteSpace(post.MediaCode) ? string.Empty : $"https://www.instagram.com/p/{post.MediaCode}/";
            CopyShareText(ShareText.Build(post.Caption, post.Username, url));
            return;
        }

        ShareActiveReel();
    }

    private void CopyShareText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Announcements.Say("There is nothing here to share.");
            return;
        }

        try
        {
            Clipboard.SetText(text);
            TxtFooterStatus.Text = $"Copied: {text}";
            Announcements.Say($"Copied to the clipboard: {text}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("SHARE", "Failed to copy share text to clipboard", ex);
            Announcements.Say("Could not copy to the clipboard.");
        }
    }

    private void BtnSaveMediaReel_Click(object sender, RoutedEventArgs e) => _ = SaveCurrentMediaAsync();
    private void BtnCaptionsReel_Click(object sender, RoutedEventArgs e) => _ = ReadCaptionsAsync();
    private void BtnDescribeReel_Click(object sender, RoutedEventArgs e) => _ = DescribeCurrentAsync(altOnly: false);

    /// <summary>
    /// Saves the media the user is looking at, plus a text file with its caption, alt text and link,
    /// so the saved copy stays usable without sight.
    /// </summary>
    private async Task SaveCurrentMediaAsync()
    {
        string? mediaUrl = null;
        var caption = string.Empty;
        var alt = string.Empty;
        var username = string.Empty;
        var pageUrl = string.Empty;

        if (_currentPanel == "Home" && ViewHome.ViewModel.SelectedPost is FeedPost post)
        {
            mediaUrl = post.MediaUrl;
            caption = post.Caption;
            alt = post.AltText;
            username = post.Username;
            pageUrl = string.IsNullOrWhiteSpace(post.MediaCode) ? string.Empty : $"https://www.instagram.com/p/{post.MediaCode}/";
        }

        // Fall back to whatever the page itself is showing, which is how reels and stories are saved.
        if (string.IsNullOrWhiteSpace(mediaUrl) || string.IsNullOrWhiteSpace(caption))
        {
            var info = await InstagramBridgeService.Instance.ReadActiveMediaAsync();
            if (info != null)
            {
                if (string.IsNullOrWhiteSpace(mediaUrl))
                {
                    mediaUrl = string.IsNullOrWhiteSpace(info.VideoUrl) ? info.ImageUrl : info.VideoUrl;
                }
                if (string.IsNullOrWhiteSpace(caption)) caption = info.Caption;
                if (string.IsNullOrWhiteSpace(alt)) alt = info.AltText;
                if (string.IsNullOrWhiteSpace(username)) username = info.Username;
                if (string.IsNullOrWhiteSpace(pageUrl)) pageUrl = info.PageUrl;
            }
        }

        if (_activeReel != null)
        {
            if (string.IsNullOrWhiteSpace(caption)) caption = _activeReel.Caption;
            if (string.IsNullOrWhiteSpace(alt)) alt = _activeReel.AltText;
            if (string.IsNullOrWhiteSpace(username)) username = _activeReel.Username;
        }

        Announcements.Say("Saving the current media...");
        var result = await MediaSaveService.Instance.SaveAsync(mediaUrl, caption, alt, username, pageUrl);
        TxtFooterStatus.Text = result.Message;
        Announcements.Say(result.Message);

        // When a save fails, the useful thing to say is which control is missing from the page.
        if (!result.Ok && AppSettingsService.Instance.Settings.AnnounceSelectorWarnings)
        {
            await SpeakControlHealthAsync();
        }
    }

    /// <summary>Reads the video's own caption track, which is the only text equivalent a video has.</summary>
    private async Task ReadCaptionsAsync()
    {
        TxtFooterStatus.Text = "Looking for captions on this video...";
        var text = await InstagramBridgeService.Instance.ReadActiveCaptionsAsync();

        if (string.IsNullOrWhiteSpace(text))
        {
            var message = "This video has no caption track. Instagram only publishes captions when the creator uploaded them.";
            TxtFooterStatus.Text = message;
            Announcements.Say(message);
            return;
        }

        // The panel shows the transcript too, so a user who can see both gets the same text.
        ViewReels.ViewModel.ShowCaptions(text);

        TxtFooterStatus.Text = text;
        Announcements.Say($"Captions: {text}");
    }

    /// <summary>Reads out what is on screen: the image description, and optionally the caption and stats.</summary>
    private async Task DescribeCurrentAsync(bool altOnly)
    {
        var parts = new List<string>();

        if (_currentPanel == "Home" && ViewHome.ViewModel.SelectedPost is FeedPost post)
        {
            if (!string.IsNullOrWhiteSpace(post.AltText)) parts.Add($"Image description: {post.AltText}");
            if (!altOnly)
            {
                if (!string.IsNullOrWhiteSpace(post.Caption)) parts.Add($"Caption: {post.Caption}");
                parts.Add($"By @{post.Username}. {post.FormattedLikes} likes, {post.CommentsCount} comments.");
            }
        }
        else
        {
            var alt = await InstagramBridgeService.Instance.ReadActiveAltTextAsync();
            if (!string.IsNullOrWhiteSpace(alt)) parts.Add($"Image description: {alt}");
            if (!altOnly && _activeReel != null)
            {
                if (!string.IsNullOrWhiteSpace(_activeReel.Caption)) parts.Add($"Caption: {_activeReel.Caption}");
                parts.Add($"By @{_activeReel.Username}. Audio: {_activeReel.AudioTitle}.");
            }
        }

        if (parts.Count == 0)
        {
            var message = altOnly
                ? "Instagram provided no image description for this media."
                : "There is nothing on screen to describe yet. Open a post or a reel first.";
            TxtFooterStatus.Text = message;
            Announcements.Say(message);
            if (AppSettingsService.Instance.Settings.AnnounceSelectorWarnings) await SpeakControlHealthAsync();
            return;
        }

        var described = string.Join(" ", parts);
        TxtFooterStatus.Text = described;
        Announcements.Say(described);
    }

    /// <summary>
    /// Verify-before-announce: checks the page for every control the native panels drive and says
    /// exactly which ones are missing, instead of letting a key press silently do nothing.
    /// </summary>
    private async Task SpeakControlHealthAsync()
    {
        var report = await InstagramBridgeService.Instance.ProbeSelectorsAsync();
        var description = report.Describe();
        TxtFooterStatus.Text = description;
        Announcements.Say(description);
    }

    // ---- Preferences --------------------------------------------------------------------------

    private void ApplyPreferences()
    {
        var settings = AppSettingsService.Instance.Settings;
        Announcements.Verbosity = settings.Verbosity;

        if (settings.NotificationsEnabled) StartActivityPoll();
        else StopActivityPoll();
    }

    private void CycleVerbosity()
    {
        var next = AppSettingsService.Instance.Settings.Verbosity switch
        {
            VerbosityLevel.Terse => VerbosityLevel.Standard,
            VerbosityLevel.Standard => VerbosityLevel.Verbose,
            _ => VerbosityLevel.Terse
        };

        AppSettingsService.Instance.Update(s => s.Verbosity = next);
        // Applied here as well as through the settings event, so the change is live immediately.
        Announcements.Verbosity = next;

        var spoken = next switch
        {
            VerbosityLevel.Terse => "Terse. WinInstagram now only says what you did and what happened.",
            VerbosityLevel.Standard => "Standard. WinInstagram now adds detail such as like counts and audio titles.",
            _ => "Verbose. WinInstagram now reads captions and descriptions as well."
        };
        TxtFooterStatus.Text = spoken;
        Announcements.Say(spoken);
    }

    private void AnnounceShortcutHelp()
    {
        var shortcutService = ShortcutService.Instance;
        var lines = shortcutService.Definitions
            .Select(d => $"{d.Label}: {ShortcutService.DescribeGesture(shortcutService.GetGesture(d.Id))}");

        var text = "Current shortcuts. " + string.Join(". ", lines) + ".";
        TxtFooterStatus.Text = text;
        Announcements.Say(text);
    }

    private void ChooseMediaFolder()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Choose where WinInstagram saves media",
                InitialDirectory = MediaSaveService.Instance.SaveFolder
            };

            if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                ViewSettings.ViewModel.SetSaveFolder(dialog.FolderName);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("SAVE", "Folder picker failed", ex);
            Announcements.Say("Could not open the folder picker.");
        }
    }

    // ---- Activity notifications ----------------------------------------------------------------

    private void StartActivityPoll()
    {
        _activityPollTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };

        if (!_activityPollAttached)
        {
            _activityPollTimer.Tick += OnActivityPollTick;
            _activityPollAttached = true;
        }

        if (!_activityPollTimer.IsEnabled) _activityPollTimer.Start();
    }

    private void StopActivityPoll() => _activityPollTimer?.Stop();

    private async void OnActivityPollTick(object? sender, EventArgs e)
    {
        if (!InstagramBridgeService.Instance.IsLoggedIn) return;
        if (!AppSettingsService.Instance.Settings.NotificationsEnabled) return;

        // The view model is the single owner of activity announcements: it speaks only when the
        // panel is open or when this was an explicit/polled check, and it never repeats an entry.
        await ViewActivity.ViewModel.RefreshAsync(announceNew: true);
    }

    // ---- Reading-position memory ---------------------------------------------------------------

    private void OnHomePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HomeViewModel.SelectedPost)) return;

        var post = ViewHome.ViewModel.SelectedPost;
        if (post == null) return;

        // Held in memory on every move, written to disk when the app closes.
        var settings = AppSettingsService.Instance.Settings;
        settings.LastReadPostId = post.Id;
        settings.LastReadPostCode = post.MediaCode;
    }

    private void OnMessagesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MessagesViewModel.SelectedConversation)) return;

        var conversation = ViewMessages.ViewModel.SelectedConversation;
        if (conversation == null || string.IsNullOrWhiteSpace(conversation.ThreadId)) return;

        AppSettingsService.Instance.Settings.LastThreadId = conversation.ThreadId;
    }

    private void RestoreReadingPosition()
    {
        if (_restoredReadingPosition) return;

        var settings = AppSettingsService.Instance.Settings;
        if (string.IsNullOrWhiteSpace(settings.LastReadPostId) && string.IsNullOrWhiteSpace(settings.LastReadPostCode))
        {
            _restoredReadingPosition = true;
            return;
        }

        // The feed fills in batches, so keep trying until the remembered post has arrived.
        if (!ViewHome.ViewModel.RestoreTo(settings.LastReadPostId, settings.LastReadPostCode)) return;

        _restoredReadingPosition = true;
        ViewHome.ScrollToSelected();
        Announcements.Detail("Restored your place in the feed.");
    }

    private void RestoreLastThread(List<DirectConversation> conversations)
    {
        if (_restoredThread) return;

        var last = AppSettingsService.Instance.Settings.LastThreadId;
        if (string.IsNullOrWhiteSpace(last))
        {
            _restoredThread = true;
            return;
        }

        var match = conversations.FirstOrDefault(c => c.ThreadId == last);
        if (match == null) return;

        _restoredThread = true;

        // Only reopen it when the user is actually looking at messages.
        if (_currentPanel != "Messages") return;

        ViewMessages.ViewModel.SelectedConversation = match;
        Announcements.Detail($"Reopened your last conversation with {match.DisplayName}.");
    }

    /// <summary>Resumes the reel from the previous session, once per launch.</summary>
    private void ResumeLastReelIfAny()
    {
        if (_resumedReel) return;
        _resumedReel = true;

        var url = AppSettingsService.Instance.Settings.LastReadReelUrl;
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
        if (!url.Contains("/reel", StringComparison.OrdinalIgnoreCase)) return;

        InstagramBridgeService.Instance.Navigate(url);
        Announcements.Say("Resumed the reel you were watching last time.");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Reading position and everything else gathered during the session is written out here.
        AppSettingsService.Instance.Save();
        StopActivityPoll();
        base.OnClosing(e);
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

        // 0.1. A focused panel may already have dealt with this key.
        if (e.Handled) return;

        // 0.2. While the command palette is open it owns the keyboard; Escape closes it.
        if (ViewPalette.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) ClosePalette();
            return;
        }

        if (ShortcutService.Instance.Matches(e.Key, Keyboard.Modifiers, "palette"))
        {
            OpenPalette();
            e.Handled = true;
            return;
        }

        // 0.3. F6, or whatever the user rebound that action to, shows or hides the native panel.
        if (ShortcutService.Instance.Matches(e.Key, Keyboard.Modifiers, "togglePanel"))
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

        // 2.5. Saving media is available from anywhere, text box included, because it is bound to a
        // Control combination that never types a character into a field.
        if (ShortcutService.Instance.Matches(e.Key, Keyboard.Modifiers, "saveMedia"))
        {
            _ = SaveCurrentMediaAsync();
            e.Handled = true;
            return;
        }

        // 3. If user is currently typing in any text box, DO NOT capture single-character hotkeys
        if (Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        // 3.5. Refresh belongs to whichever panel is on screen. Panels handle it themselves when they
        // have focus, so this covers the Reels tab and any rebound key the panels do not know about.
        if (ShortcutService.Instance.Matches(e.Key, Keyboard.Modifiers, "refresh"))
        {
            RefreshCurrentPanel();
            e.Handled = true;
            return;
        }

        // 4. Playback shortcuts. They are only live while the Reels panel is the one on screen, so
        // pressing J on the feed cannot jump the web view to a different reel. Which keys those are
        // comes from the user's map, so the screen-reader-friendly preset works here too.
        if (_currentPanel == "Reels")
        {
            var shortcuts = ShortcutService.Instance;
            var modifiers = Keyboard.Modifiers;

            if (shortcuts.Matches(e.Key, modifiers, "nextReel"))
            {
                _ = InstagramBridgeService.Instance.NextReelAsync();
                e.Handled = true;
                return;
            }

            if (shortcuts.Matches(e.Key, modifiers, "prevReel"))
            {
                _ = InstagramBridgeService.Instance.PreviousReelAsync();
                e.Handled = true;
                return;
            }

            if (shortcuts.Matches(e.Key, modifiers, "playPause"))
            {
                _ = InstagramBridgeService.Instance.TogglePlayAsync();
                e.Handled = true;
                return;
            }

            if (shortcuts.Matches(e.Key, modifiers, "mute"))
            {
                _ = InstagramBridgeService.Instance.ToggleMuteAsync();
                e.Handled = true;
                return;
            }

            if (shortcuts.Matches(e.Key, modifiers, "like"))
            {
                ToggleActiveReelLike();
                e.Handled = true;
                return;
            }

            if (shortcuts.Matches(e.Key, modifiers, "comments"))
            {
                ToggleCommentsDrawer();
                e.Handled = true;
                return;
            }

            if (shortcuts.Matches(e.Key, modifiers, "share"))
            {
                ShareActiveReel();
                e.Handled = true;
                return;
            }

            // Captions and descriptions only mean anything while a video is on screen.
            if (shortcuts.Matches(e.Key, modifiers, "captions"))
            {
                _ = ReadCaptionsAsync();
                e.Handled = true;
                return;
            }

            if (shortcuts.Matches(e.Key, modifiers, "describe"))
            {
                _ = DescribeCurrentAsync(altOnly: false);
                e.Handled = true;
                return;
            }

            // Arrow keys belong to the list focused inside the native panel, if any.
            if (e.Key is Key.PageDown or Key.Down)
            {
                if (IsFocusInNativePanel()) return;
                _ = InstagramBridgeService.Instance.NextReelAsync();
                e.Handled = true;
                return;
            }

            if (e.Key is Key.PageUp or Key.Up)
            {
                if (IsFocusInNativePanel()) return;
                _ = InstagramBridgeService.Instance.PreviousReelAsync();
                e.Handled = true;
                return;
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