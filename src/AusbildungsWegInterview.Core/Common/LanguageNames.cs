namespace AusbildungsWegInterview.Core.Common;

/// <summary>Maps language codes (ISO-639-1) to human-readable names for prompts/UI.</summary>
public static class LanguageNames
{
    public static string DisplayName(string? code)
    {
        var key = (code ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "de" => "German",
            "en" => "English",
            "fr" => "French",
            "ar" => "Arabic",
            "es" => "Spanish",
            "it" => "Italian",
            "nl" => "Dutch",
            "tr" => "Turkish",
            "pt" => "Portuguese",
            "pl" => "Polish",
            "ru" => "Russian",
            "zh" => "Chinese",
            "ja" => "Japanese",
            "" => "the same language as the source",
            var c => c
        };
    }
}
