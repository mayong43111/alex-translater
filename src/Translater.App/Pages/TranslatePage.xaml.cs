using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Translater_App.Pages;

public sealed partial class TranslatePage : Page
{
    public TranslatePage()
    {
        InitializeComponent();
    }

    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
        var inputText = InputTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(inputText))
            return;

        TranslateButton.IsEnabled = false;
        try
        {
            // TODO: Call ITranslationService from Translater.Core
            ResultTextBox.Text = $"[翻译服务待接入] {inputText}";
        }
        finally
        {
            TranslateButton.IsEnabled = true;
        }
    }

    private void SwapLanguage_Click(object sender, RoutedEventArgs e)
    {
        var sourceIndex = SourceLanguageCombo.SelectedIndex;
        var targetIndex = TargetLanguageCombo.SelectedIndex;

        // Skip swap if source is "auto"
        if (sourceIndex == 0)
            return;

        // Adjust for offset (source has "auto" as index 0)
        TargetLanguageCombo.SelectedIndex = sourceIndex - 1;
        SourceLanguageCombo.SelectedIndex = targetIndex + 1;
    }

    private void PasteFromClipboard_Click(object sender, RoutedEventArgs e)
    {
        var package = Clipboard.GetContent();
        if (package.Contains(StandardDataFormats.Text))
        {
            var task = package.GetTextAsync();
            task.Completed = (info, status) =>
            {
                if (status == Windows.Foundation.AsyncStatus.Completed)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        InputTextBox.Text = info.GetResults();
                    });
                }
            };
        }
    }

    private void CopyResult_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ResultTextBox.Text))
        {
            var package = new DataPackage();
            package.SetText(ResultTextBox.Text);
            Clipboard.SetContent(package);
        }
    }
}
