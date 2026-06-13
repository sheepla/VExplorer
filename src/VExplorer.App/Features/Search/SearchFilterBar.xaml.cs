using System.Windows.Controls;
using System.Windows.Input;

namespace VExplorer.App.Features.Search;

/// <summary>
/// One-line SEARCH / FILTER input bar. Self-contained key handling (Enter / Esc);
/// live query updates flow through the two-way Text binding.
/// </summary>
public partial class SearchFilterBar : UserControl
{
    public SearchFilterBar()
    {
        InitializeComponent();
    }

    /// <summary>Moves keyboard focus to the query input.</summary>
    public void FocusInput()
    {
        QueryInput.Focus();
        QueryInput.CaretIndex = QueryInput.Text.Length;
    }

    /// <summary>
    /// Self-focus when the bar becomes visible, so paths that enter SEARCH/FILTER
    /// without going through MainWindow (e.g. <c>:search</c>/<c>:filter</c> with no
    /// keyword) still land the caret in the query input.
    /// </summary>
    private void QueryInput_IsVisibleChanged(
        object sender,
        System.Windows.DependencyPropertyChangedEventArgs e
    )
    {
        if (QueryInput.IsVisible)
        {
            FocusInput();
        }
    }

    private void QueryInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SearchFilterBarViewModel vm)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Return:
                vm.Confirm();
                e.Handled = true;
                break;
            case Key.Escape:
                vm.Cancel();
                e.Handled = true;
                break;
        }
    }
}
