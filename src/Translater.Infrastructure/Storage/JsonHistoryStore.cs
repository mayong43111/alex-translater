using System.Text.Json;
using Translater.Core.Interfaces;
using Translater.Core.Models;
using Translater.Infrastructure.Serialization;

namespace Translater.Infrastructure.Storage;

/// <summary>
/// Simple JSON file-based history storage.
/// Lightweight alternative to LiteDB for initial implementation.
/// </summary>
public class JsonHistoryStore : IHistoryService
{
    private readonly string _filePath;
    private List<HistoryItem> _items = [];

    public JsonHistoryStore(string storagePath)
    {
        _filePath = Path.Combine(storagePath, "history.json");
        Load();
    }

    public Task SaveAsync(HistoryItem item)
    {
        _items.Insert(0, item);
        if (_items.Count > 500) // Keep max 500 items
            _items = _items.Take(500).ToList();
        return PersistAsync();
    }

    public Task<IReadOnlyList<HistoryItem>> GetRecentAsync(int count = 50)
    {
        var result = _items.Take(count).ToList() as IReadOnlyList<HistoryItem>;
        return Task.FromResult(result);
    }

    public Task DeleteAsync(string id)
    {
        _items.RemoveAll(x => x.Id == id);
        return PersistAsync();
    }

    public Task ClearAllAsync()
    {
        _items.Clear();
        return PersistAsync();
    }

    private void Load()
    {
        if (File.Exists(_filePath))
        {
            var json = File.ReadAllText(_filePath);
            _items = JsonSerializer.Deserialize(json, InfrastructureJsonContext.Default.ListHistoryItem) ?? [];
        }
    }

    private async Task PersistAsync()
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(_items, InfrastructureJsonContext.Default.ListHistoryItem);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
