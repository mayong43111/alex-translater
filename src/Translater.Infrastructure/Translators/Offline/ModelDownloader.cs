namespace Translater.Infrastructure.Translators.Offline;

/// <summary>
/// Downloads OPUS-MT ONNX models from HuggingFace.
/// Models are stored at %LOCALAPPDATA%/Translater/models/
/// </summary>
public sealed class ModelDownloader
{
    private readonly HttpClient _httpClient;
    private readonly string _modelsDir;

    // HuggingFace ONNX model repository base URLs (Xenova quantized exports)
    // NOTE: Use decoder_model_quantized (non-merged) which doesn't require KV cache inputs.
    private const string ModelVersion = "v2";
    private static readonly Dictionary<string, (string url, string localName)[]> ModelFiles = new()
    {
        ["opus-mt-zh-en"] = new[]
        {
            ("https://huggingface.co/Xenova/opus-mt-zh-en/resolve/main/onnx/encoder_model_quantized.onnx", "encoder_model.onnx"),
            ("https://huggingface.co/Xenova/opus-mt-zh-en/resolve/main/onnx/decoder_model_quantized.onnx", "decoder_model.onnx"),
            ("https://huggingface.co/Helsinki-NLP/opus-mt-zh-en/resolve/main/vocab.json", "vocab.json")
        },
        ["opus-mt-en-zh"] = new[]
        {
            ("https://huggingface.co/Xenova/opus-mt-en-zh/resolve/main/onnx/encoder_model_quantized.onnx", "encoder_model.onnx"),
            ("https://huggingface.co/Xenova/opus-mt-en-zh/resolve/main/onnx/decoder_model_quantized.onnx", "decoder_model.onnx"),
            ("https://huggingface.co/Helsinki-NLP/opus-mt-en-zh/resolve/main/vocab.json", "vocab.json")
        }
    };

    public ModelDownloader(string modelsDir, HttpClient? httpClient = null)
    {
        _modelsDir = modelsDir;
        _httpClient = httpClient ?? new HttpClient();
    }

    public string ModelsDir => _modelsDir;

    /// <summary>
    /// Check if a model is fully downloaded.
    /// </summary>
    public bool IsModelDownloaded(string modelName)
    {
        var dir = Path.Combine(_modelsDir, modelName);
        if (!Directory.Exists(dir)) return false;

        // Check for required files and correct model version
        return File.Exists(Path.Combine(dir, "encoder_model.onnx"))
            && File.Exists(Path.Combine(dir, "decoder_model.onnx"))
            && File.Exists(Path.Combine(dir, "vocab.txt"))
            && File.Exists(Path.Combine(dir, ".version"))
            && File.ReadAllText(Path.Combine(dir, ".version")).Trim() == ModelVersion;
    }

    /// <summary>
    /// Download a model. Reports progress as (bytesDownloaded, totalBytes, currentFile).
    /// </summary>
    public async Task DownloadModelAsync(
        string modelName,
        IProgress<(long downloaded, long total, string file)>? progress = null,
        CancellationToken ct = default)
    {
        if (!ModelFiles.TryGetValue(modelName, out var files))
            throw new ArgumentException($"未知模型: {modelName}");

        var modelDir = Path.Combine(_modelsDir, modelName);
        Directory.CreateDirectory(modelDir);

        long totalDownloaded = 0;

        foreach (var (url, localName) in files)
        {
            var fileName = localName;
            var filePath = Path.Combine(modelDir, fileName);

            // Skip if already exists and non-empty
            if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
                continue;

            progress?.Report((totalDownloaded, -1, fileName));

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            long fileDownloaded = 0;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(filePath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None, 81920);

            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                fileDownloaded += read;
                totalDownloaded += read;
                progress?.Report((totalDownloaded, totalBytes, fileName));
            }

            fileStream.Close();
            File.Move(filePath + ".tmp", filePath, overwrite: true);
        }

        // Post-process: convert vocab.json to vocab.txt if needed
        await PostProcessModelFiles(modelDir, ct);

        // Write version marker
        await File.WriteAllTextAsync(Path.Combine(modelDir, ".version"), ModelVersion, ct);
    }

    /// <summary>
    /// Convert HuggingFace vocab.json to simple vocab.txt format if needed.
    /// vocab.json maps token -> id, we need a file where line number = id.
    /// </summary>
    private static async Task PostProcessModelFiles(string modelDir, CancellationToken ct)
    {
        var vocabJsonPath = Path.Combine(modelDir, "vocab.json");
        var vocabTxtPath = Path.Combine(modelDir, "vocab.txt");

        if (File.Exists(vocabJsonPath) && !File.Exists(vocabTxtPath))
        {
            var json = await File.ReadAllTextAsync(vocabJsonPath, ct);
            var vocab = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            if (vocab != null)
            {
                var sorted = new string[vocab.Count];
                foreach (var kv in vocab)
                {
                    if (kv.Value < sorted.Length)
                        sorted[kv.Value] = kv.Key;
                }

                await File.WriteAllLinesAsync(vocabTxtPath, sorted, ct);
            }
        }
    }

    /// <summary>
    /// Delete a downloaded model.
    /// </summary>
    public void DeleteModel(string modelName)
    {
        var dir = Path.Combine(_modelsDir, modelName);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    /// <summary>
    /// Get total size of a downloaded model in bytes.
    /// </summary>
    public long GetModelSize(string modelName)
    {
        var dir = Path.Combine(_modelsDir, modelName);
        if (!Directory.Exists(dir)) return 0;
        return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);
    }
}
