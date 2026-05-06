using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Translater_App.Pages;

public sealed partial class HistoryPage : Page
{
    public ObservableCollection<HistoryDisplayItem> HistoryItems { get; } = new();

    public HistoryPage()
    {
        InitializeComponent();
        UpdateEmptyState();
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        HistoryItems.Clear();
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyStateText.Visibility = HistoryItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}

public class HistoryDisplayItem
{
    public string OriginalText { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
