using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VExplorer.App.Converters;

/// <summary>
/// Converts a <see cref="bool"/> to <see cref="Visibility"/>. When
/// <see cref="Invert"/> is true, <c>false</c> maps to Visible (used to show the
/// plain path display while the editable box is hidden, and vice versa).
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is true;
        if (Invert)
        {
            flag = !flag;
        }
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
