using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinInstagram.ViewModels;

namespace WinInstagram.Views;

/// <summary>
/// Native settings panel. It owns no behaviour of its own beyond the rebind capture: every change
/// is written through its view model, which persists it and tells the shell to apply it.
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    public SettingsView()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel();
    }

    /// <summary>Puts focus on the first control so a keyboard user can start immediately.</summary>
    public void FocusFirst()
    {
        CmbVerbosity.Focus();
        Keyboard.Focus(CmbVerbosity);
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        // While a rebind is in progress every key belongs to the capture, including Escape.
        if (ViewModel.Shortcuts.Any(r => r.IsCapturing))
        {
            if (e.Key == Key.Escape) ViewModel.CancelCapture();
            else ViewModel.CommitCapture(e.Key, Keyboard.Modifiers);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && ShortcutsList.IsKeyboardFocusWithin &&
            ShortcutsList.SelectedItem is ShortcutRow row)
        {
            ViewModel.BeginCapture(row.Id);
            e.Handled = true;
        }
    }

    private void ShortcutsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ShortcutsList.SelectedItem is ShortcutRow row)
        {
            ViewModel.BeginCapture(row.Id);
            e.Handled = true;
        }
    }

    private void UseModifiedKeys_Click(object sender, RoutedEventArgs e) => ViewModel.UseModifiedKeys();

    private void ResetShortcuts_Click(object sender, RoutedEventArgs e) => ViewModel.ResetShortcuts();

    private void ChangeFolder_Click(object sender, RoutedEventArgs e) => ViewModel.RequestChooseSaveFolder();
}
