using System.Runtime.InteropServices.WindowsRuntime;
using Translater.Core.Interfaces;
using Translater.Core.Models;

namespace Translater.Infrastructure.Ocr;

/// <summary>
/// Windows built-in OCR engine using Windows.Media.Ocr.
/// Supports Chinese (zh-Hans) and English (en) without additional dependencies.
/// </summary>
public class WindowsOcrEngine : IOcrService
{
    public async Task<OcrResult> RecognizeAsync(
        byte[] imageData,
        string language,
        CancellationToken ct = default)
    {
        // Convert byte[] to SoftwareBitmap
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await stream.WriteAsync(imageData.AsBuffer());
        stream.Seek(0);

        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

        // Create OCR engine for the specified language
        var ocrLanguage = new Windows.Globalization.Language(language);
        var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(ocrLanguage);

        if (engine is null)
        {
            // Fallback: try user profile languages
            engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
        }

        if (engine is null)
        {
            return new OcrResult(string.Empty, []);
        }

        var ocrResult = await engine.RecognizeAsync(softwareBitmap);

        var lines = ocrResult.Lines
            .Select(l => new OcrLine(
                l.Text,
                l.Words.Min(w => w.BoundingRect.X),
                l.Words.Min(w => w.BoundingRect.Y),
                l.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width) - l.Words.Min(w => w.BoundingRect.X),
                l.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height) - l.Words.Min(w => w.BoundingRect.Y)))
            .ToList();

        return new OcrResult(ocrResult.Text, lines);
    }
}
