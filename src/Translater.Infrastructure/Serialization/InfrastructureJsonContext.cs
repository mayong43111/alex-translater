using System.Text.Json.Serialization;
using Translater.Core.Models;

namespace Translater.Infrastructure.Serialization;

internal sealed class BingTextRequest
{
    public string Text { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<HistoryItem>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(BingTextRequest[]))]
internal partial class InfrastructureJsonContext : JsonSerializerContext
{
}
