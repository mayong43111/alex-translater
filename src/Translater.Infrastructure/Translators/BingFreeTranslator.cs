using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Translater.Core.Interfaces;
using Translater.Core.Models;
using Translater.Infrastructure.Serialization;

namespace Translater.Infrastructure.Translators;

/// <summary>
/// Bing Translator free implementation using Edge translate API.
/// No API key required.
/// </summary>
public class BingFreeTranslator : ITranslationService
{
    private readonly HttpClient _httpClient;
    private string? _authToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private const string AuthUrl = "https://edge.microsoft.com/translate/auth";
    private const string TranslateUrl = "https://api-edge.cognitive.microsofttranslator.com/translate";

    public BingFreeTranslator(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default)
    {
        var token = await GetAuthTokenAsync(ct);

        var sourceLang = MapLanguageCode(sourceLanguage);
        var targetLang = MapLanguageCode(targetLanguage);

        var url = $"{TranslateUrl}?api-version=3.0&to={targetLang}";
        if (sourceLang != "auto-detect")
            url += $"&from={sourceLang}";

        var body = JsonSerializer.Serialize(
            new[] { new BingTextRequest { Text = text } },
            InfrastructureJsonContext.Default.BingTextRequestArray);
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var result = doc.RootElement[0];
        var translations = result.GetProperty("translations");
        var translatedText = translations[0].GetProperty("text").GetString() ?? "";

        var detectedLang = sourceLanguage;
        if (result.TryGetProperty("detectedLanguage", out var detected))
        {
            detectedLang = detected.GetProperty("language").GetString() ?? sourceLanguage;
        }

        return new TranslationResult(text, translatedText, detectedLang, "Bing");
    }

    private async Task<string> GetAuthTokenAsync(CancellationToken ct)
    {
        if (_authToken != null && DateTime.UtcNow < _tokenExpiry)
            return _authToken;

        var request = new HttpRequestMessage(HttpMethod.Get, AuthUrl);
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        _authToken = await response.Content.ReadAsStringAsync(ct);
        // Token is valid for ~10 minutes, refresh at 8 min
        _tokenExpiry = DateTime.UtcNow.AddMinutes(8);

        return _authToken;
    }

    private static string MapLanguageCode(string lang) => lang switch
    {
        "auto" => "auto-detect",
        "zh" => "zh-Hans",
        "en" => "en",
        _ => lang
    };
}
