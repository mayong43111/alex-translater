namespace Translater.Core.Interfaces;

public interface IHistoryService
{
    Task SaveAsync(Models.HistoryItem item);
    Task<IReadOnlyList<Models.HistoryItem>> GetRecentAsync(int count = 50);
    Task DeleteAsync(string id);
    Task ClearAllAsync();
}
