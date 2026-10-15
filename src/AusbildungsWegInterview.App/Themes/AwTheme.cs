using System.Windows;

namespace AusbildungsWegInterview.App;

/// <summary>
/// Applies the brand theme at runtime. The theme dictionary is always slot 0 of
/// Application.Resources.MergedDictionaries; every control/brush reference in the
/// UI uses DynamicResource, so replacing the slot re-skins the whole window live
/// (dropdown popups, focus borders, buttons, status colors, error banner included).
/// </summary>
public static class ThemeApplier
{
    public const string Light = "light";
    public const string Dark = "dark";

    public static void Apply(string mode)
    {
        var resources = Application.Current?.Resources
            ?? throw new InvalidOperationException("Application.Resources is not available yet.");
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/AwTheme.{(mode == Dark ? "Dark" : "Light")}.xaml", UriKind.Absolute)
        };

        if (resources.MergedDictionaries.Count == 0)
            resources.MergedDictionaries.Add(dict);
        else
            resources.MergedDictionaries[0] = dict;
    }
}
