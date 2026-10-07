using System.Text.Json;
using System.Text.RegularExpressions;
using Translater.Core.Interfaces;
using Translater.Core.Models;

namespace Translater.Infrastructure.Translators;

/// <summary>
/// Bing Translator implementation using its web translation session.
/// No API key required.
/// </summary>
public class BingFreeTranslator : ITranslationService
{
    private readonly HttpClient _httpClient;
    private const string TranslatorPage = "https://cn.bing.com/translator";
    private const string TranslateUrl = "https://cn.bing.com/ttranslatev3";

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
        var sourceLang = MapLanguageCode(sourceLanguage);
        var targetLang = MapLanguageCode(targetLanguage);
        if (text.Length > 1000)
            throw new InvalidOperationException("Bing 网页翻译单次最多支持 1000 个字符，请分段翻译。");

        using var pageRequest = new HttpRequestMessage(HttpMethod.Get, TranslatorPage);
        using var pageResponse = await _httpClient.SendAsync(pageRequest, ct);
        pageResponse.EnsureSuccessStatusCode();
        var html = await pageResponse.Content.ReadAsStringAsync(ct);
        var session = Regex.Match(html, "IG:\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1));
        var parameters = Regex.Match(html, @"params_AbusePreventionHelper\s*=\s*(\[[^\]]+\])", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!session.Success || !parameters.Success)
            throw new InvalidOperationException("无法获取 Bing 翻译会话，网页接口可能已变更或需要验证。请稍后重试或使用离线翻译。");

        using var sessionData = JsonDocument.Parse(parameters.Groups[1].Value);
        var key = sessionData.RootElement[0].ToString();
        var token = sessionData.RootElement[1].GetString() ?? string.Empty;
        var url = $"{TranslateUrl}?isVertical=1&IG={Uri.EscapeDataString(session.Groups[1].Value)}&IID=translator.5028.1";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["fromLang"] = sourceLang,
                ["to"] = targetLang,
                ["text"] = text,
                ["token"] = token,
                ["key"] = key
            })
        };
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Bing 未返回译文，请稍后重试或使用离线翻译。");
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            throw new InvalidOperationException("Bing 翻译请求被拒绝或受到限流，请稍后重试。");
        var result = doc.RootElement[0];
        if (!result.TryGetProperty("translations", out var translations) || translations.GetArrayLength() == 0)
            throw new InvalidOperationException("Bing 响应中没有译文，请稍后重试。");
        var translatedText = translations[0].GetProperty("text").GetString() ?? "";

        var detectedLang = sourceLanguage;
        if (result.TryGetProperty("detectedLanguage", out var detected))
        {
            detectedLang = detected.GetProperty("language").GetString() ?? sourceLanguage;
        }

        return new TranslationResult(text, translatedText, detectedLang, "Bing");
    }

    private static string MapLanguageCode(string lang) => lang switch
    {
        "auto" => "auto-detect",
        "zh" => "zh-Hans",
        "en" => "en",
        _ => lang
    };
}
