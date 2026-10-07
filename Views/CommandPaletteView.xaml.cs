using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinInstagram.ViewModels;

namespace WinInstagram.Views;

/// <summary>
/// The Ctrl+K command palette. It owns no commands of its own: it lists them, and raises the id of
/// the one the user chose so the shell stays the single place that knows how anything is done.
/// </summary>
public partial class CommandPaletteView : UserControl
{
    public CommandPaletteViewModel ViewModel => (CommandPaletteViewModel)DataContext;

    /// <summary>Raised with a <c>PaletteCommandIds</c> value when the user runs a command.</summary>
    public event Action<string>? CommandInvoked;

    /// <summary>Raised when the user asks to close the palette with Escape.</summary>
    public event Action? DismissRequested;

    public CommandPaletteView()
    {
        InitializeComponent();
        DataContext = new CommandPaletteViewModel();
    }

    /// <summary>Resets the filter and puts keyboard focus in the filter box.</summary>
    public void FocusFilter()
    {
        ViewModel.Reset();
        TxtPaletteFilter.Focus();
        Keyboard.Focus(TxtPaletteFilter);
        TxtPaletteFilter.SelectAll();
    }

    /// <summary>Runs the selected command, or the first match when nothing is selected yet.</summary>
    private void InvokeSelected()
    {
        var command = ViewModel.SelectedCommand ?? ViewModel.Matches.FirstOrDefault();
        if (command == null)
        {
            // Nothing to run, so closing is the least surprising thing to do.
            DismissRequested?.Invoke();
            return;
        }

        CommandInvoked?.Invoke(command.Id);
    }

    private void TxtPaletteFilter_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                InvokeSelected();
                e.Handled = true;
                break;

            case Key.Escape:
                DismissRequested?.Invoke();
                e.Handled = true;
                break;

            case Key.Down:
                if (ViewModel.Matches.Count > 0)
                {
                    MatchesList.SelectedIndex = Math.Max(0, MatchesList.SelectedIndex);
                    MatchesList.Focus();
                    Keyboard.Focus(MatchesList);
                }
                e.Handled = true;
                break;
        }
    }

    private void MatchesList_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                InvokeSelected();
                e.Handled = true;
                break;

            case Key.Escape:
                DismissRequested?.Invoke();
                e.Handled = true;
                break;

            case Key.Up:
                // Stepping up off the first row returns to the filter box, so the list is never a trap.
                if (MatchesList.SelectedIndex <= 0)
                {
                    TxtPaletteFilter.Focus();
                    Keyboard.Focus(TxtPaletteFilter);
                }
                break;
        }
    }
}
