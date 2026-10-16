using System.Globalization;
using System.Windows.Data;

namespace AusbildungsWegInterview.App.ViewModels;

/// <summary>
/// Maps a REAL capture level (mean amplitude 0..1) to a waveform bar height.
/// Quiet frames keep a small visible baseline so the panel still shows a line
/// while the audio is silent — nothing here is simulated.
/// </summary>
public sealed class LevelToHeightConverter : IValueConverter
{
    /// <summary>Bar height in device-independent pixels (mockup waveform height).</summary>
    public double MaxHeight { get; set; } = 34;

    public double MinHeight { get; set; } = 2;

    /// <summary>Levels are small for speech; this scales them into the visible band.</summary>
    public double Gain { get; set; } = 7.5;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double level = value is double d ? d : 0d;
        double scaled = Math.Clamp(level * Gain, 0d, 1d);
        return MinHeight + scaled * (MaxHeight - MinHeight);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
