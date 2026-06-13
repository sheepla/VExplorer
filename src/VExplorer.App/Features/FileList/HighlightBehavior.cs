using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace VExplorer.App.Features.FileList;

/// <summary>
/// Attached behaviour that fills a <see cref="TextBlock"/> with runs, emphasising
/// the occurrences of <see cref="QueryProperty"/> within <see cref="TextProperty"/>.
/// Matching mirrors <c>CompletionMatcher</c>: substring with smartcase (an
/// all-lowercase query matches case-insensitively). An empty query renders the
/// text plainly.
/// </summary>
public static class HighlightBehavior
{
    private static readonly Brush MatchBackground = CreateFrozen(Color.FromRgb(0xFF, 0xF1, 0x76));
    private static readonly Brush MatchForeground = CreateFrozen(Color.FromRgb(0x00, 0x00, 0x00));

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(HighlightBehavior),
        new PropertyMetadata("", OnChanged)
    );

    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
        "Query",
        typeof(string),
        typeof(HighlightBehavior),
        new PropertyMetadata("", OnChanged)
    );

    public static string GetText(DependencyObject obj)
    {
        return (string)obj.GetValue(TextProperty);
    }

    public static void SetText(DependencyObject obj, string value)
    {
        obj.SetValue(TextProperty, value);
    }

    public static string GetQuery(DependencyObject obj)
    {
        return (string)obj.GetValue(QueryProperty);
    }

    public static void SetQuery(DependencyObject obj, string value)
    {
        obj.SetValue(QueryProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block)
        {
            return;
        }

        string text = GetText(block) ?? "";
        string query = GetQuery(block) ?? "";

        block.Inlines.Clear();

        if (query.Length == 0)
        {
            block.Inlines.Add(new Run(text));
            return;
        }

        StringComparison comparison = HasUpper(query)
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        int start = 0;
        while (start <= text.Length)
        {
            int idx = text.IndexOf(query, start, comparison);
            if (idx < 0)
            {
                block.Inlines.Add(new Run(text[start..]));
                break;
            }
            if (idx > start)
            {
                block.Inlines.Add(new Run(text[start..idx]));
            }
            block.Inlines.Add(
                new Run(text.Substring(idx, query.Length))
                {
                    Background = MatchBackground,
                    Foreground = MatchForeground,
                }
            );
            start = idx + query.Length;
        }
    }

    private static bool HasUpper(string value)
    {
        foreach (char c in value)
        {
            if (char.IsUpper(c))
            {
                return true;
            }
        }
        return false;
    }

    private static Brush CreateFrozen(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }
}
