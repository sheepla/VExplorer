using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using VExplorer.Core.FileSystem;

namespace VExplorer.App.Features.FileOps;

/// <summary>
/// A small frameless, click-through window that trails the cursor during a drag
/// and shows how many items are being dragged (e.g. "3 items"), echoing the
/// Explorer drag badge. Created only when two or more items are dragged.
/// </summary>
public sealed class DragCountOverlay : Window
{
    private const double CursorOffset = 16;

    private readonly IDragOverlayInterop _interop;

    private DragCountOverlay(int count, IDragOverlayInterop interop)
    {
        _interop = interop;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        TextBlock label = new() { Text = $"{count} items" };
        label.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundBrush");

        Border box = new()
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 3, 8, 3),
            Child = label,
        };
        box.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
        box.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        Content = box;
    }

    /// <summary>Shows an overlay for <paramref name="count"/> items at the current cursor.</summary>
    public static DragCountOverlay Begin(int count, IDragOverlayInterop interop)
    {
        DragCountOverlay overlay = new(count, interop);
        overlay.Show();
        if (interop.TryGetCursorPosition(out int x, out int y))
        {
            overlay.MoveToScreen(x, y);
        }
        return overlay;
    }

    /// <summary>Places the box just below-right of the cursor (physical screen pixels).</summary>
    public void MoveToScreen(int physX, int physY)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        Left = (physX / dpi.DpiScaleX) + CursorOffset;
        Top = (physY / dpi.DpiScaleY) + CursorOffset;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _interop.MakeHitTestTransparent(new WindowInteropHelper(this).Handle);
    }
}
