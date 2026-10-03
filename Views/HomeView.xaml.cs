using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinInstagram.Services;
using WinInstagram.ViewModels;

namespace WinInstagram.Views;

public partial class HomeView : UserControl
{
    public HomeViewModel ViewModel => (HomeViewModel)DataContext;

    public HomeView()
    {
        InitializeComponent();
        DataContext = new HomeViewModel();
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.L)
        {
            ViewModel.ToggleLikeCurrent();
            e.Handled = true;
        }
    }

    private void LikePost_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleLikeCurrent();
    }

    private void RefreshFeed_Click(object sender, RoutedEventArgs e)
    {
        InstagramBridgeService.Instance.Navigate("https://www.instagram.com/");
        AccessibilityHelper.Announce("Refreshing home feed...");
    }
}
