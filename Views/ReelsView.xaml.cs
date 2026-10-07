using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinInstagram.Services;
using WinInstagram.ViewModels;

namespace WinInstagram.Views;

/// <summary>
/// Native accessible Reels panel. It shows live details of the reel playing in the engine and
/// a navigable list of reels already watched, but it intentionally owns no playback logic:
/// every action is raised as an event so the shell remains the single place that drives the
/// engine and the single place that speaks.
/// </summary>
public partial class ReelsView : UserControl
{
    public ReelsViewModel ViewModel => (ReelsViewModel)DataContext;

    public event Action? PreviousRequested;
    public event Action? NextRequested;
    public event Action? PlayPauseRequested;
    public event Action? MuteRequested;
    public event Action? LikeRequested;
    public event Action? ShareRequested;
    public event Action? CommentsRequested;

    /// <summary>Raised when the user asks for the video's own caption track to be read out.</summary>
    public event Action? CaptionsRequested;

    /// <summary>Raised when the user asks for a spoken description of what is on screen.</summary>
    public event Action? DescribeRequested;

    /// <summary>Raised when the user asks to save the current media and its text to disk.</summary>
    public event Action? SaveRequested;

    /// <summary>Raised with a URL when the user re-opens a reel from their history.</summary>
    public event Action<string>? HistoryOpenRequested;

    public ReelsView()
    {
        InitializeComponent();
        DataContext = new ReelsViewModel();
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        // Captions and descriptions use the user's map, so the screen-reader preset keeps the
        // plain letters T and D free here as well.
        var shortcuts = ShortcutService.Instance;

        if (shortcuts.Matches(e.Key, Keyboard.Modifiers, "captions"))
        {
            CaptionsRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (shortcuts.Matches(e.Key, Keyboard.Modifiers, "describe"))
        {
            DescribeRequested?.Invoke();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.OemPlus:
            case Key.Add:
                ViewModel.IncreaseVolume();
                e.Handled = true;
                break;

            case Key.OemMinus:
            case Key.Subtract:
                ViewModel.DecreaseVolume();
                e.Handled = true;
                break;

            case Key.Enter:
                OpenSelectedHistoryReel();
                e.Handled = true;
                break;
        }
    }

    private void OpenSelectedHistoryReel()
    {
        var url = ViewModel.GetSelectedHistoryUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            AccessibilityHelper.Announce("No previously watched reel is selected.");
            return;
        }
        HistoryOpenRequested?.Invoke(url);
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => PreviousRequested?.Invoke();
    private void Next_Click(object sender, RoutedEventArgs e) => NextRequested?.Invoke();
    private void PlayPause_Click(object sender, RoutedEventArgs e) => PlayPauseRequested?.Invoke();
    private void Mute_Click(object sender, RoutedEventArgs e) => MuteRequested?.Invoke();
    private void Like_Click(object sender, RoutedEventArgs e) => LikeRequested?.Invoke();
    private void Share_Click(object sender, RoutedEventArgs e) => ShareRequested?.Invoke();
    private void Comments_Click(object sender, RoutedEventArgs e) => CommentsRequested?.Invoke();
    private void Captions_Click(object sender, RoutedEventArgs e) => CaptionsRequested?.Invoke();
    private void Describe_Click(object sender, RoutedEventArgs e) => DescribeRequested?.Invoke();
    private void Save_Click(object sender, RoutedEventArgs e) => SaveRequested?.Invoke();
}
