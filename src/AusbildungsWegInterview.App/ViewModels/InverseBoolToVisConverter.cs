using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AusbildungsWegInterview.App.ViewModels;

/// <summary>
/// bool -> Visibility, inverted relative to the built-in converter
/// (true => Collapsed, false => Visible). Used for empty-state hints.
/// </summary>
public sealed class InverseBoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible ? false : true;
}
