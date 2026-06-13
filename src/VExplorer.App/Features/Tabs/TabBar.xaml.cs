using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VExplorer.App.Features.Tabs;

public partial class TabBar : UserControl
{
    private Point _dragStart;
    private TabItemViewModel? _dragItem;

    public TabBar()
    {
        InitializeComponent();
    }

    /// <summary>Click on a tab (other than its × button) activates it and arms a drag.</summary>
    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabItemViewModel tab })
        {
            tab.ActivateCommand.Execute(null);
            _dragItem = tab;
            _dragStart = e.GetPosition(null);
        }
    }

    /// <summary>Begin a drag once the pointer moves past the system threshold.</summary>
    private void Tab_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null)
        {
            return;
        }
        Point pos = e.GetPosition(null);
        if (
            Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance
        )
        {
            return;
        }
        TabItemViewModel item = _dragItem;
        _dragItem = null;
        DragDrop.DoDragDrop(
            (DependencyObject)sender,
            new DataObject(typeof(TabItemViewModel), item),
            DragDropEffects.Move
        );
    }

    private void Tab_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TabItemViewModel))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Tab_Drop(object sender, DragEventArgs e)
    {
        if (
            e.Data.GetData(typeof(TabItemViewModel)) is TabItemViewModel source
            && sender is FrameworkElement { DataContext: TabItemViewModel target }
            && DataContext is TabBarViewModel vm
        )
        {
            vm.MoveTab(source.Id, target.Id);
        }
    }
}
