namespace Translater.Core.Interfaces;

public interface IOcrService
{
    Task<Models.OcrResult> RecognizeAsync(
        byte[] imageData,
        string language,
        CancellationToken ct = default);
}
