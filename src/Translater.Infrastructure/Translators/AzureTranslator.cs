using System.Text;
using System.Text.Json;
using Translater.Core.Interfaces;
using Translater.Core.Models;
using Translater.Infrastructure.Serialization;

namespace Translater.Infrastructure.Translators;

/// <summary>
/// Azure Cognitive Services Translator implementation.
/// Free tier: 2M characters/month.
/// </summary>
public class AzureTranslator : ITranslationService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _region;
    private const string Endpoint = "https://api.cognitive.microsofttranslator.com";

    public AzureTranslator(HttpClient httpClient, string apiKey, string region = "global")
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _region = region;
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default)
    {
        var from = sourceLanguage == "auto" ? "" : $"&from={sourceLanguage}";
        var url = $"{Endpoint}/translate?api-version=3.0&to={targetLanguage}{from}";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);
        request.Headers.Add("Ocp-Apim-Subscription-Region", _region);

        var body = JsonSerializer.Serialize(
            new[] { new BingTextRequest { Text = text } },
            InfrastructureJsonContext.Default.BingTextRequestArray);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement[0];

        var detectedLang = root.TryGetProperty("detectedLanguage", out var detected)
            ? detected.GetProperty("language").GetString() ?? sourceLanguage
            : sourceLanguage;

        var translatedText = root
            .GetProperty("translations")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;

        return new TranslationResult(text, translatedText, detectedLang, "Azure");
    }
}
