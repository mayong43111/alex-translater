namespace Translater.Core.Models;

public record TranslationResult(
    string OriginalText,
    string TranslatedText,
    string DetectedLanguage,
    string SourceName);
