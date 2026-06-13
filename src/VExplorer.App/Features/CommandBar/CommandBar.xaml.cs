using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VExplorer.App.Features.Completion;

namespace VExplorer.App.Features.CommandBar;

/// <summary>
/// Bottom command bar control. COMMAND-mode key handling is self-contained here
/// (the window dispatcher only handles entering the mode via <c>:</c>), so the
/// completion/cycle keys never collide with the global NORMAL-mode dispatch.
/// </summary>
public partial class CommandBar : UserControl
{
    public CommandBar()
    {
        InitializeComponent();
    }

    /// <summary>Moves keyboard focus to the command input.</summary>
    public void FocusInput()
    {
        CommandInput.Focus();
        CommandInput.CaretIndex = CommandInput.Text.Length;
    }

    /// <summary>Seeds the command buffer (e.g. "! " for the <c>!</c> shortcut) and focuses it.</summary>
    public void Prefill(string text)
    {
        if (DataContext is CommandBarViewModel vm)
        {
            vm.Prefill(text);
        }
        FocusInput();
    }

    private void CandidateList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not CompletionEditorViewModel vm || sender is not ListBox list)
        {
            return;
        }
        vm.SelectCandidate(list.SelectedIndex);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusInput));
    }

    private void CommandInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CompletionEditorViewModel vm)
        {
            return;
        }

        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        switch (e.Key)
        {
            case Key.Back when ctrl:
                vm.DeleteSegment();
                MoveCaretToEnd();
                e.Handled = true;
                break;

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

            // Up/Down and Ctrl+P/Ctrl+N recall command history (Tab cycles
            // completion candidates). The address bar keeps these for cycling.
            case Key.Down:
            case Key.N when ctrl:
                (vm as CommandBarViewModel)?.HistoryNext();
                MoveCaretToEnd();
                e.Handled = true;
                break;

            case Key.Up:
            case Key.P when ctrl:
                (vm as CommandBarViewModel)?.HistoryPrev();
                MoveCaretToEnd();
                e.Handled = true;
                break;

            case Key.Return:
                vm.Confirm();
                // The bar stays open for an argument-less :cp/:mv re-prompt; put the
                // caret after the re-seeded "cp "/"mv " so typing continues there.
                if (vm.IsActive)
                {
                    MoveCaretToEnd();
                }
                e.Handled = true;
                break;

            case Key.Escape:
                vm.Cancel();
                e.Handled = true;
                break;
        }
    }

    private void MoveCaretToEnd()
    {
        CommandInput.CaretIndex = CommandInput.Text.Length;
    }
}
