using System.Net.Http.Json;
using System.Text.Json;
using Translater.Core.Interfaces;
using Translater.Core.Models;

namespace Translater.Infrastructure.Translators;

/// <summary>
/// Google Translate free implementation using public translate API.
/// No API key required.
/// Reference: STranslate.Plugin.Translate.GoogleBuiltIn
/// </summary>
public class GoogleFreeTranslator : ITranslationService
{
    private readonly HttpClient _httpClient;

    // Google Translate web API (free, no key)
    private const string TranslateUrl =
        "https://translate.googleapis.com/translate_a/single";

    public GoogleFreeTranslator(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default)
    {
        var sourceLang = MapLanguageCode(sourceLanguage);
        var targetLang = MapLanguageCode(targetLanguage);

        // Use Google's free translate_a/single endpoint
        var url = $"{TranslateUrl}?client=gtx&sl={sourceLang}&tl={targetLang}" +
                  $"&dt=t&dt=bd&dt=at&dj=1&ie=UTF-8&oe=UTF-8" +
                  $"&q={Uri.EscapeDataString(text)}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        // Parse translation from response
        var translatedText = ParseTranslation(doc.RootElement);
        var detectedLang = ParseDetectedLanguage(doc.RootElement, sourceLanguage);

        return new TranslationResult(text, translatedText, detectedLang, "Google");
    }

    private static string ParseTranslation(JsonElement root)
    {
        // Response format with dj=1: { "sentences": [{"trans": "...", "orig": "..."}], "src": "en" }
        if (root.TryGetProperty("sentences", out var sentences))
        {
            var result = string.Empty;
            foreach (var sentence in sentences.EnumerateArray())
            {
                if (sentence.TryGetProperty("trans", out var trans))
                {
                    result += trans.GetString();
                }
            }
            return result;
        }

        return string.Empty;
    }

    private static string ParseDetectedLanguage(JsonElement root, string fallback)
    {
        if (root.TryGetProperty("src", out var src))
        {
            return src.GetString() ?? fallback;
        }
        return fallback;
    }

    private static string MapLanguageCode(string lang) => lang switch
    {
        "auto" => "auto",
        "zh" => "zh-CN",
        "en" => "en",
        _ => lang
    };
}
