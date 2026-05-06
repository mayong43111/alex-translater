using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Translater.Core.Interfaces;
using Translater.Core.Models;

namespace Translater.Infrastructure.Translators.Offline;

/// <summary>
/// Offline translation using ONNX Runtime with MarianMT (OPUS-MT) models.
/// Supports Helsinki-NLP/opus-mt-zh-en and opus-mt-en-zh models exported to ONNX.
/// </summary>
public sealed class MarianOnnxTranslator : ITranslationService, IDisposable
{
    private InferenceSession? _encoderSession;
    private InferenceSession? _decoderSession;
    private SentencePieceTokenizer? _srcTokenizer;
    private SentencePieceTokenizer? _tgtTokenizer;
    private string? _loadedModelDir;
    private readonly string _modelsBaseDir;
    private readonly int _maxLength;
    private readonly object _lock = new();

    /// <summary>
    /// Fired when model loading state changes (for UI progress feedback).
    /// </summary>
    public event Action<string>? StatusChanged;

    public MarianOnnxTranslator(string modelsBaseDir, int maxLength = 512)
    {
        _modelsBaseDir = modelsBaseDir;
        _maxLength = maxLength;
    }

    /// <summary>
    /// Check if model files exist for a given language pair.
    /// </summary>
    public bool IsModelAvailable(string sourceLang, string targetLang)
    {
        var dir = GetModelDir(sourceLang, targetLang);
        return dir != null && Directory.Exists(dir)
            && File.Exists(Path.Combine(dir, "encoder_model.onnx"))
            && File.Exists(Path.Combine(dir, "decoder_model.onnx"))
            && File.Exists(Path.Combine(dir, "vocab.txt"));
    }

    /// <summary>
    /// Get model directory path for a language pair.
    /// </summary>
    public string? GetModelDir(string sourceLang, string targetLang)
    {
        var src = NormalizeLang(sourceLang);
        var tgt = NormalizeLang(targetLang);
        if (src == null || tgt == null) return null;
        return Path.Combine(_modelsBaseDir, $"opus-mt-{src}-{tgt}");
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default)
    {
        var src = NormalizeLang(sourceLanguage) ?? "zh";
        var tgt = NormalizeLang(targetLanguage) ?? "en";

        // Auto-detect: simple heuristic
        if (sourceLanguage == "auto")
        {
            bool hasChinese = text.Any(c => c >= '\u4e00' && c <= '\u9fff');
            src = hasChinese ? "zh" : "en";
            tgt = hasChinese ? "en" : "zh";
        }

        var modelDir = GetModelDir(src, tgt);
        if (modelDir == null || !Directory.Exists(modelDir))
            throw new InvalidOperationException($"离线模型未安装: {src}->{tgt}");

        if (!File.Exists(Path.Combine(modelDir, "encoder_model.onnx"))
            || !File.Exists(Path.Combine(modelDir, "decoder_model.onnx"))
            || !File.Exists(Path.Combine(modelDir, "vocab.txt")))
            throw new InvalidOperationException($"离线模型文件不完整，请重新下载: {src}->{tgt}");

        await Task.Run(() => EnsureModelLoaded(modelDir), ct);

        string translated;
        try
        {
            translated = await Task.Run(() => RunInference(text, ct), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"离线翻译推理失败: {ex.Message}", ex);
        }

        return new TranslationResult(text, translated, src, "离线");
    }

    private void EnsureModelLoaded(string modelDir)
    {
        lock (_lock)
        {
            if (_loadedModelDir == modelDir) return;

            StatusChanged?.Invoke("加载模型中...");

            // Dispose previous sessions
            _encoderSession?.Dispose();
            _decoderSession?.Dispose();
            if (!ReferenceEquals(_srcTokenizer, _tgtTokenizer))
                _tgtTokenizer?.Dispose();
            _srcTokenizer?.Dispose();

            _encoderSession = null;
            _decoderSession = null;
            _srcTokenizer = null;
            _tgtTokenizer = null;
            _loadedModelDir = null;

            var encoderPath = Path.Combine(modelDir, "encoder_model.onnx");
            var decoderPath = Path.Combine(modelDir, "decoder_model.onnx");
            var vocabPath = Path.Combine(modelDir, "vocab.txt");

            var encOptions = new SessionOptions();
            encOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            encOptions.InterOpNumThreads = Environment.ProcessorCount;
            encOptions.IntraOpNumThreads = Environment.ProcessorCount;

            var decOptions = new SessionOptions();
            decOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            decOptions.InterOpNumThreads = Environment.ProcessorCount;
            decOptions.IntraOpNumThreads = Environment.ProcessorCount;

            _encoderSession = new InferenceSession(encoderPath, encOptions);
            _decoderSession = new InferenceSession(decoderPath, decOptions);

            // MarianMT uses shared vocab for src and tgt
            _srcTokenizer = SentencePieceTokenizer.LoadFromVocab(vocabPath);
            _tgtTokenizer = _srcTokenizer;

            _loadedModelDir = modelDir;
            StatusChanged?.Invoke("模型已就绪");
        }
    }

    private string RunInference(string text, CancellationToken ct)
    {
        if (_encoderSession == null || _decoderSession == null || _srcTokenizer == null || _tgtTokenizer == null)
            throw new InvalidOperationException("模型未加载");

        // 1. Tokenize input
        var inputIds = _srcTokenizer.Encode(text);
        int seqLen = inputIds.Length;

        // 2. Create attention mask (all 1s)
        var attentionMask = new long[seqLen];
        Array.Fill(attentionMask, 1L);

        // 3. Run encoder
        var inputIdsTensor = new DenseTensor<long>(
            inputIds.Select(x => (long)x).ToArray(), new[] { 1, seqLen });
        var attentionMaskTensor = new DenseTensor<long>(
            attentionMask, new[] { 1, seqLen });

        var encoderInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
        };

        using var encoderResults = _encoderSession.Run(encoderInputs);
        var encoderHiddenStates = encoderResults.First().AsTensor<float>().Clone();

        // 4. Auto-regressive decoding (greedy)
        var decoderIds = new List<int> { _tgtTokenizer.PadId }; // Start with pad (MarianMT convention)

        for (int step = 0; step < _maxLength; step++)
        {
            ct.ThrowIfCancellationRequested();

            int decLen = decoderIds.Count;
            var decoderInputIds = new DenseTensor<long>(
                decoderIds.Select(x => (long)x).ToArray(), new[] { 1, decLen });

            var decoderInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", decoderInputIds),
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", attentionMaskTensor),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenStates)
            };

            using var decoderResults = _decoderSession.Run(decoderInputs);
            var logits = decoderResults.First().AsTensor<float>();

            // Get logits for last token position
            int vocabSize = logits.Dimensions[2];
            int lastPos = decLen - 1;

            // Greedy: pick argmax
            int bestId = 0;
            float bestScore = float.NegativeInfinity;
            for (int v = 0; v < vocabSize; v++)
            {
                float score = logits[0, lastPos, v];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestId = v;
                }
            }

            if (bestId == _tgtTokenizer.EosId)
                break;

            decoderIds.Add(bestId);
        }

        // 5. Decode tokens (skip initial pad token)
        return _tgtTokenizer.Decode(decoderIds.Skip(1));
    }

    private static string? NormalizeLang(string lang) => lang switch
    {
        "zh" or "zh-Hans" or "zh-CN" or "chinese" => "zh",
        "en" or "english" => "en",
        "auto" => null,
        _ => lang
    };

    public void Dispose()
    {
        _encoderSession?.Dispose();
        _decoderSession?.Dispose();
        if (_srcTokenizer != _tgtTokenizer)
            _tgtTokenizer?.Dispose();
        _srcTokenizer?.Dispose();
    }
}
