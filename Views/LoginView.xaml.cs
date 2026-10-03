using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using WinInstagram.Services;

namespace WinInstagram.Views;

public partial class LoginView : UserControl
{
    private bool _isInitialized = false;

    public event Action? LoginSucceeded;
    private bool _isEngineTab = false;

    public LoginView()
    {
        InitializeComponent();
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;

        try
        {
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WinInstagram",
                "WebView2Profile"
            );
            Directory.CreateDirectory(dataFolder);
            AppLogger.Info("INIT", $"Setting up WebView2 Profile at: {dataFolder}");

            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-backgrounding-occluded-windows --disable-renderer-backgrounding"
            };
            var env = await CoreWebView2Environment.CreateAsync(null, dataFolder, options);
            await MainWebView.EnsureCoreWebView2Async(env);

            await InstagramBridgeService.Instance.InitializeAsync(MainWebView.CoreWebView2, Dispatcher);
            InstagramBridgeService.Instance.LoginStatusChanged += OnLoginStatusChanged;

            _isInitialized = true;

            // Check if user already has an active session from a previous launch
            await InstagramBridgeService.Instance.CheckLoginStateAsync();

            if (InstagramBridgeService.Instance.IsLoggedIn)
            {
                LoginHeader.Visibility = Visibility.Collapsed;
                WebViewContainer.Margin = new Thickness(0);
                TxtEngineStatus.Text = "Status: Signed in. Transitioning to Home...";
                AccessibilityHelper.Announce("Instagram session detected. Opening application.");
                LoginSucceeded?.Invoke();
            }
            else
            {
                TxtEngineStatus.Text = "Status: Please enter your login credentials below.";
                AccessibilityHelper.Announce("Please enter your Instagram username and password below.");
                MainWebView.CoreWebView2.Navigate("https://www.instagram.com/accounts/login/");
                MainWebView.Focus();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("INIT", "WebView2 initialization failed", ex);
            TxtEngineStatus.Text = $"Engine Error: {ex.Message}";
        }
    }

    private void LoginView_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_isEngineTab)
        {
            e.Handled = true;
        }
    }

    private void MainWebView_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_isEngineTab)
        {
            e.Handled = true;
        }
    }

    private void OnLoginStatusChanged(bool isLoggedIn)
    {
        if (isLoggedIn)
        {
            LoginHeader.Visibility = Visibility.Collapsed;
            WebViewContainer.Margin = new Thickness(0);
            TxtEngineStatus.Text = "Status: Signed in successfully!";
            AccessibilityHelper.Announce("Signed into Instagram successfully. Opening Home feed.");
            LoginSucceeded?.Invoke();
        }
        else
        {
            LoginHeader.Visibility = Visibility.Visible;
            WebViewContainer.Margin = new Thickness(12, 6, 12, 12);
            TxtEngineStatus.Text = "Status: Signed out or login required.";
        }
    }

    private void ReloadLogin_Click(object sender, RoutedEventArgs e)
    {
        if (MainWebView.CoreWebView2 != null)
        {
            AppLogger.Info("NAV", "Reloading login page upon user request...");
            MainWebView.CoreWebView2.Navigate("https://www.instagram.com/accounts/login/");
            AccessibilityHelper.Announce("Reloading login page...");
            MainWebView.Focus();
        }
    }

    private async void ContinueToApp_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Info("AUTH", "User clicked 'Continue to App'. Verifying session and proceeding...");
        await InstagramBridgeService.Instance.CheckLoginStateAsync();
        MainWebView.CoreWebView2?.Navigate("https://www.instagram.com/reels/");
        LoginSucceeded?.Invoke();
    }

    public void SetAccessibleFocusable(bool isEngineTab)
    {
        _isEngineTab = isEngineTab;

        Focusable = isEngineTab;
        KeyboardNavigation.SetIsTabStop(this, isEngineTab);
        KeyboardNavigation.SetTabNavigation(this, isEngineTab ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.None);
        KeyboardNavigation.SetDirectionalNavigation(this, isEngineTab ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.None);
        KeyboardNavigation.SetControlTabNavigation(this, isEngineTab ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.None);
        IsHitTestVisible = isEngineTab;

        if (MainWebView != null)
        {
            MainWebView.Focusable = isEngineTab;
            KeyboardNavigation.SetIsTabStop(MainWebView, isEngineTab);
            KeyboardNavigation.SetTabNavigation(MainWebView, isEngineTab ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.None);
            KeyboardNavigation.SetDirectionalNavigation(MainWebView, isEngineTab ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.None);
            KeyboardNavigation.SetControlTabNavigation(MainWebView, isEngineTab ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.None);
            MainWebView.IsHitTestVisible = isEngineTab;
        }
    }
}
