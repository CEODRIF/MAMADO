using System.Globalization;
using System.Windows.Data;
using AusbildungsWegInterview.Localization;

namespace AusbildungsWegInterview.App.ViewModels;

/// <summary>
/// Localizes a string key passed as ConverterParameter against the view model's
/// <c>LocalizationManager T</c> (bound value). WPF's XAML parser cannot embed
/// quotes in binding paths, so {Binding T, ConverterParameter=Key} is used
/// instead of an indexer. Re-evaluates whenever the VM raises PropertyChanged("T")
/// on UI-language change.
/// </summary>
public sealed class LocKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is LocalizationManager loc && parameter is string key ? loc[key] : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
