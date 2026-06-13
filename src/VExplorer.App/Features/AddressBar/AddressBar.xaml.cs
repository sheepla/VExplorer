using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace VExplorer.App.Features.AddressBar;

/// <summary>
/// Address bar control. ADDRESS-mode key handling is self-contained here (the
/// window dispatcher only handles entering the mode), so completion/cycle keys
/// never collide with the global NORMAL-mode dispatch.
/// </summary>
public partial class AddressBar : UserControl
{
    public AddressBar()
    {
        InitializeComponent();
    }

    /// <summary>Moves keyboard focus to the input and selects all text.</summary>
    public void FocusInput()
    {
        AddressInput.Focus();
        AddressInput.SelectAll();
    }

    /// <summary>Click the plain path display → enter ADDRESS mode and focus the input.</summary>
    private void Display_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not AddressBarViewModel vm)
        {
            return;
        }
        vm.BeginEdit();
        // Focus after the TextBox becomes visible (IsEditing binding applied).
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusInput));
    }

    /// <summary>Click a candidate → accept it and keep editing.</summary>
    private void CandidateList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not AddressBarViewModel vm || sender is not ListBox list)
        {
            return;
        }
        vm.SelectCandidate(list.SelectedIndex);
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                AddressInput.Focus();
                MoveCaretToEnd();
            })
        );
    }

    private void AddressInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not AddressBarViewModel vm)
        {
            return;
        }

        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        switch (e.Key)
        {
            // Ctrl+Backspace — delete one path segment.
            case Key.Back when ctrl:
                vm.DeleteSegment();
                MoveCaretToEnd();
                e.Handled = true;
                break;

            // Tab / Shift+Tab — cycle candidates, replacing the buffer (zsh-style).
            case Key.Tab:
                if (shift)
                {
                    vm.CyclePrev();
                }
                else
                {
                    vm.CycleNext();
                }
                MoveCaretToEnd();
                e.Handled = true;
                break;

            // Down / Ctrl+N — next candidate.
            case Key.Down:
            case Key.N when ctrl:
                vm.CycleNext();
                MoveCaretToEnd();
                e.Handled = true;
                break;

            // Up / Ctrl+P — previous candidate.
            case Key.Up:
            case Key.P when ctrl:
                vm.CyclePrev();
                MoveCaretToEnd();
                e.Handled = true;
                break;

            // Enter — confirm (navigate or report error).
            case Key.Return:
                vm.Confirm();
                e.Handled = true;
                break;

            // Esc — cancel and return to NORMAL.
            case Key.Escape:
                vm.Cancel();
                e.Handled = true;
                break;
        }
    }

    private void MoveCaretToEnd()
    {
        AddressInput.CaretIndex = AddressInput.Text.Length;
    }
}
