using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using Translater.Core.Interfaces;
using Translater.Core.Models;

namespace Translater.Infrastructure.Ocr;

/// <summary>
/// Windows built-in OCR engine using Windows.Media.Ocr.
/// Supports Chinese (zh-Hans) and English (en) without additional dependencies.
/// </summary>
public partial class WindowsOcrEngine : IOcrService
{
    private const int MinOcrDimension = 320;
    private const int Padding = 40;

    public async Task<OcrResult> RecognizeAsync(
        byte[] imageData,
        string language,
        CancellationToken ct = default)
    {
        // Preprocess: scale up small images and enhance for OCR
        var processedData = PreprocessForOcr(imageData);

        // Convert byte[] to SoftwareBitmap
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await stream.WriteAsync(processedData.AsBuffer());
        stream.Seek(0);

        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

        // Create OCR engine for the specified language
        var ocrLanguage = new Windows.Globalization.Language(language);
        var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(ocrLanguage);

        if (engine is null)
        {
            engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
        }

        if (engine is null)
        {
            return new OcrResult(string.Empty, []);
        }

        var ocrResult = await engine.RecognizeAsync(softwareBitmap);

        // Reconstruct text from words, intelligently handling CJK spacing
        var text = ReconstructText(ocrResult);

        var lines = ocrResult.Lines
            .Select(l => new OcrLine(
                ReconstructLineText(l),
                l.Words.Min(w => w.BoundingRect.X),
                l.Words.Min(w => w.BoundingRect.Y),
                l.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width) - l.Words.Min(w => w.BoundingRect.X),
                l.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height) - l.Words.Min(w => w.BoundingRect.Y)))
            .ToList();

        return new OcrResult(text, lines);
    }

    /// <summary>
    /// Preprocesses image for better OCR: scale up small images and enhance contrast.
    /// </summary>
    private static byte[] PreprocessForOcr(byte[] imageData)
    {
        using var ms = new MemoryStream(imageData);
        using var original = new Bitmap(ms);

        int w = original.Width;
        int h = original.Height;

        // Calculate scale factor - target at least MinOcrDimension px on shortest side
        float scale = 1f;
        if (h < MinOcrDimension)
            scale = (float)MinOcrDimension / h;
        if (w < MinOcrDimension)
            scale = Math.Max(scale, (float)MinOcrDimension / w);
        if (scale > 8f) scale = 8f;

        if (scale <= 1.05f)
            return imageData;

        int newW = (int)(w * scale);
        int newH = (int)(h * scale);

        // Create scaled image with white padding
        using var result = new Bitmap(newW + Padding * 2, newH + Padding * 2, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(original, Padding, Padding, newW, newH);
        }

        // Enhance contrast for better OCR
        EnhanceContrast(result, Padding, Padding, newW, newH);

        using var outMs = new MemoryStream();
        result.Save(outMs, ImageFormat.Png);
        return outMs.ToArray();
    }

    /// <summary>
    /// Simple contrast enhancement: push dark pixels darker, light pixels lighter.
    /// </summary>
    private static void EnhanceContrast(Bitmap bmp, int offsetX, int offsetY, int width, int height)
    {
        var rect = new Rectangle(offsetX, offsetY, width, height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                byte* ptr = (byte*)data.Scan0;
                int stride = data.Stride;
                for (int y = 0; y < height; y++)
                {
                    byte* row = ptr + y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        // BGRA format
                        int idx = x * 4;
                        int b = row[idx];
                        int g = row[idx + 1];
                        int r = row[idx + 2];

                        // Compute luminance
                        int lum = (r * 299 + g * 587 + b * 114) / 1000;

                        // Push toward black or white (threshold ~160)
                        if (lum < 160)
                        {
                            // Darken: reduce by 30%
                            row[idx] = (byte)(b * 70 / 100);
                            row[idx + 1] = (byte)(g * 70 / 100);
                            row[idx + 2] = (byte)(r * 70 / 100);
                        }
                        else
                        {
                            // Lighten: push toward white
                            row[idx] = (byte)Math.Min(255, b + (255 - b) * 40 / 100);
                            row[idx + 1] = (byte)Math.Min(255, g + (255 - g) * 40 / 100);
                            row[idx + 2] = (byte)Math.Min(255, r + (255 - r) * 40 / 100);
                        }
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>
    /// Reconstructs text from OCR result, removing unwanted spaces between CJK characters.
    /// </summary>
    private static string ReconstructText(Windows.Media.Ocr.OcrResult ocrResult)
    {
        var sb = new StringBuilder();
        foreach (var line in ocrResult.Lines)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(ReconstructLineText(line));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Reconstructs line text from words, only adding spaces between non-CJK tokens.
    /// </summary>
    private static string ReconstructLineText(Windows.Media.Ocr.OcrLine line)
    {
        var sb = new StringBuilder();
        foreach (var word in line.Words)
        {
            if (sb.Length > 0)
            {
                char lastChar = sb[sb.Length - 1];
                char firstChar = word.Text[0];

                // Add space only when both sides are non-CJK (Latin, digits, etc.)
                bool lastIsCjk = IsCjk(lastChar);
                bool firstIsCjk = IsCjk(firstChar);
                if (!lastIsCjk && !firstIsCjk)
                {
                    sb.Append(' ');
                }
            }
            sb.Append(word.Text);
        }
        return sb.ToString();
    }

    private static bool IsCjk(char c)
    {
        return c >= '\u2E80' && c <= '\u9FFF'
            || c >= '\uF900' && c <= '\uFAFF'
            || c >= '\uFE30' && c <= '\uFE4F'
            || c >= '\uFF00' && c <= '\uFFEF'
            || c >= '\u3000' && c <= '\u303F';
    }

    [GeneratedRegex(@"([\u2E80-\u9FFF\uF900-\uFAFF\uFE30-\uFE4F\uFF00-\uFFEF])\s+([\u2E80-\u9FFF\uF900-\uFAFF\uFE30-\uFE4F\uFF00-\uFFEF\u3000-\u303F\uFF01-\uFF60])")]
    private static partial Regex CjkSpaceRegex();
}
