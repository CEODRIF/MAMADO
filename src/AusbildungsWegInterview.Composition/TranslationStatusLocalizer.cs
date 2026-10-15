using AusbildungsWegInterview.Core.Translation;
using AusbildungsWegInterview.Localization;

namespace AusbildungsWegInterview.App;

/// <summary>
/// Maps a <see cref="TranslationResult"/> to the user-facing, LOCALIZED status
/// line (all four UI languages). A genuine translation yields an empty string
/// (the panel simply shows the translated text). The raw English
/// StatusMessage on the result is for logs/tests only, never for the UI.
/// </summary>
public static class TranslationStatusLocalizer
{
    public static string Localize(LocalizationManager loc, TranslationResult result)
    {
        if (result is null || result.IsTranslated)
            return string.Empty;

        if (result.Status is not { } status)
        {
            // Defensive: a status message without a machine code should not
            // leak raw provider text into the localized UI.
            return result.StatusMessage is null ? string.Empty : loc["TranslStatusProviderFailed"];
        }

        return status switch
        {
            TranslationStatus.PassThrough => loc["TranslStatusPassThrough"],
            TranslationStatus.Echo => loc["TranslStatusEcho"],
            TranslationStatus.ProviderUnavailable => loc["TranslStatusProviderUnavailable"],
            TranslationStatus.ProviderFailed => loc["TranslStatusProviderFailed"],
            TranslationStatus.NoOutput => loc["TranslStatusNoOutput"],
            TranslationStatus.NeedsPinnedSource => loc["TranslStatusNeedsSource"],
            TranslationStatus.PairNotSupported => loc.F("TranslStatusPairNotSupported", result.SourceLanguage, result.TargetLanguage),
            TranslationStatus.CliLaunchFailed => loc["TranslStatusCliLaunch"],
            TranslationStatus.CliTimeout => loc["TranslStatusCliTimeout"],
            TranslationStatus.CliError => loc["TranslStatusCliError"],
            TranslationStatus.WhisperEnglishOnly => loc.F("TranslStatusWhisperEnglishOnly", result.TargetLanguage),
            TranslationStatus.WhisperEngineUnsupported => loc["TranslStatusWhisperEngine"],
            _ => string.Empty
        };
    }
}
