using System.ComponentModel;
using AusbildungsWegInterview.Localization;

namespace AusbildungsWegInterview.App.ViewModels;

/// <summary>
/// One entry of the left navigation rail. <see cref="Key"/> is the stable page
/// identity ("interview", "documents", "settings", "logs", "feedback"); the
/// label is localized and re-rendered on UI-language change.
/// </summary>
public sealed class NavigationItem : INotifyPropertyChanged
{
    private readonly string _labelKey;
    private string _label = string.Empty;

    public NavigationItem(string key, string labelKey, string glyph)
    {
        Key = key;
        _labelKey = labelKey;
        Glyph = glyph;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Stable page identity (used for selection logic, never displayed).</summary>
    public string Key { get; }

    /// <summary>Segoe MDL2 Assets glyph (icon font shipped with Windows).</summary>
    public string Glyph { get; }

    public string Label => _label;

    public void ApplyLocalization(LocalizationManager loc)
    {
        _label = loc[_labelKey];
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
    }
}
