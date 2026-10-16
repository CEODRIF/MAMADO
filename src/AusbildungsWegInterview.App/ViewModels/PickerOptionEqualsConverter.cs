using System.Globalization;
using System.Windows.Data;

namespace AusbildungsWegInterview.App.ViewModels;

/// <summary>
/// True when the bound <see cref="PickerOption"/>'s value equals the converter
/// parameter — used by the segmented controls (Short / Balanced / Long) which are
/// rendered as radio buttons bound ONE-WAY to the current selection and changed
/// through a command (so the view model stays the single source of truth).
/// </summary>
public sealed class PickerOptionEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is PickerOption option && parameter is string expected &&
           string.Equals(option.Value, expected, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
