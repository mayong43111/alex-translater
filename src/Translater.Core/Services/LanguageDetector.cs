namespace Translater.Core.Services;

/// <summary>
/// Simple local language detector based on character analysis.
/// Reference: STranslate LanguageDetector (Local mode)
/// </summary>
public static class LanguageDetector
{
    /// <summary>
    /// Detects whether text is primarily Chinese or English.
    /// Returns "zh" for Chinese, "en" for English.
    /// </summary>
    public static string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "en";

        int chineseCount = 0;
        int englishCount = 0;

        foreach (var ch in text)
        {
            if (IsChinese(ch))
                chineseCount++;
            else if (IsEnglishLetter(ch))
                englishCount++;
        }

        // If more than 20% characters are Chinese, treat as Chinese
        var total = chineseCount + englishCount;
        if (total == 0)
            return "en";

        return (double)chineseCount / total > 0.2 ? "zh" : "en";
    }

    /// <summary>
    /// Given detected source language, returns the opposite target language.
    /// Chinese → English, English → Chinese.
    /// </summary>
    public static string GetTargetLanguage(string detectedSource)
    {
        return detectedSource == "zh" ? "en" : "zh";
    }

    private static bool IsChinese(char c)
    {
        // CJK Unified Ideographs range
        return c >= 0x4E00 && c <= 0x9FFF
            || c >= 0x3400 && c <= 0x4DBF   // CJK Extension A
            || c >= 0xF900 && c <= 0xFAFF;  // CJK Compatibility
    }

    private static bool IsEnglishLetter(char c)
    {
        return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    }
}
