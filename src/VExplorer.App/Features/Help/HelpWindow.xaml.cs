using System.Windows;
using System.Windows.Input;

namespace VExplorer.App.Features.Help;

/// <summary>
/// Read-only reference window for <c>:help</c>: the operation × input-route table
/// from <see cref="ActionMatrix"/>, grouped by topic. Navigation is Vim-style and
/// keyboard-only (j/k, g/G, Ctrl+D/U); <c>q</c> or Esc closes.
/// </summary>
public partial class HelpWindow : Window
{
    public HelpWindow(string topic)
    {
        InitializeComponent();
        TopicList.ItemsSource = ActionMatrix.Filter(topic);
        if (!string.IsNullOrWhiteSpace(topic))
        {
            Caption.Text = $"Action × input route — matches for \"{topic}\"";
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        double half = Scroller.ViewportHeight / 2;

        switch (e.Key)
        {
            case Key.J when !ctrl:
            case Key.Down:
                Scroller.LineDown();
                break;
            case Key.K when !ctrl:
            case Key.Up:
                Scroller.LineUp();
                break;
            case Key.D when ctrl:
                Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + half);
                break;
            case Key.U when ctrl:
                Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset - half);
                break;
            case Key.B when ctrl:
            case Key.PageUp:
                Scroller.PageUp();
                break;
            case Key.F when ctrl:
            case Key.PageDown:
                Scroller.PageDown();
                break;
            case Key.G when shift:
                Scroller.ScrollToBottom();
                break;
            case Key.G:
                Scroller.ScrollToTop();
                break;
            case Key.Q:
            case Key.Escape:
                Close();
                break;
            default:
                return; // leave other keys unhandled
        }
        e.Handled = true;
    }
}
