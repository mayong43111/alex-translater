namespace Translater.Core.Interfaces;

public interface ITranslationService
{
    Task<Models.TranslationResult> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default);
}
