using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AusbildungsWegInterview.App.ViewModels;

/// <summary>
/// bool -> FlowDirection: right-to-left layout (Arabic) vs left-to-right.
/// FlowDirection=RightToLeft flips alignment, reading order, form flow and
/// dropdown alignment automatically, per the WPF layout system.
/// </summary>
public sealed class BoolToFlowDirectionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is FlowDirection.RightToLeft;
}
