using PaddleOCRSharp;
using Translater.Core.Interfaces;
using Translater.Core.Models;

namespace Translater.Infrastructure.Ocr;

/// <summary>
/// PaddleOCR-based OCR engine using PaddleOCRSharp (PP-OCRv5).
/// Bundled models, excellent Chinese recognition, no internet required.
/// </summary>
public class PaddleOcrEngine : IOcrService, IDisposable
{
    private PaddleOCREngine? _engine;
    private readonly object _lock = new();
    private Task? _warmupTask;

    public PaddleOcrEngine()
    {
        // Pre-warm the engine on a background thread
        _warmupTask = Task.Run(() => GetEngine());
    }

    private PaddleOCREngine GetEngine()
    {
        if (_engine is not null) return _engine;
        lock (_lock)
        {
            if (_engine is not null) return _engine;

            // Use default bundled PP-OCRv5 models (det + cls + rec)
            var parameter = new OCRParameter
            {
                use_gpu = false,
                cpu_math_library_num_threads = Environment.ProcessorCount > 4 ? 4 : Environment.ProcessorCount,
                enable_mkldnn = true,
                det = true,
                rec = true,
                cls = false,
                use_angle_cls = false,
                det_db_thresh = 0.3f,
                det_db_box_thresh = 0.5f,
                det_db_unclip_ratio = 1.6f,
                max_side_len = 960,
            };

            _engine = new PaddleOCREngine(OCRModelConfig.Default, parameter);
            return _engine;
        }
    }

    public Task<OcrResult> RecognizeAsync(
        byte[] imageData,
        string language,
        CancellationToken ct = default)
    {
        // PaddleOCR runs synchronously; wrap in Task.Run to avoid blocking UI
        return Task.Run(() =>
        {
            var engine = GetEngine();
            var result = engine.DetectText(imageData);

            if (result is null || result.TextBlocks is null || result.TextBlocks.Count == 0)
            {
                return new OcrResult(string.Empty, []);
            }

            var lines = result.TextBlocks
                .Select(block =>
                {
                    double minX = block.BoxPoints.Min(p => p.X);
                    double minY = block.BoxPoints.Min(p => p.Y);
                    double maxX = block.BoxPoints.Max(p => p.X);
                    double maxY = block.BoxPoints.Max(p => p.Y);
                    return new OcrLine(block.Text, minX, minY, maxX - minX, maxY - minY);
                })
                .ToList();

            return new OcrResult(result.Text, lines);
        }, ct);
    }

    public void Dispose()
    {
        _engine?.Dispose();
        _engine = null;
    }
}
