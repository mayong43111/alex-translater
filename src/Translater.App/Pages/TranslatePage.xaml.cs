using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Translater.Core.Interfaces;
using Translater.Core.Services;
using Translater.Infrastructure.Translators;

namespace Translater_App.Pages;

public sealed partial class TranslatePage : Page
{
    private readonly ITranslationService _translator;

    public TranslatePage()
    {
        InitializeComponent();
        _translator = new GoogleFreeTranslator(new HttpClient());
    }

    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
        var inputText = InputTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(inputText))
            return;

        TranslateButton.IsEnabled = false;
        ResultTextBox.Text = "翻译中...";
        try
        {
            var sourceLang = GetSelectedSourceLanguage();
            var targetLang = GetSelectedTargetLanguage();

            // Auto-detect: determine target based on input
            if (sourceLang == "auto")
            {
                var detected = LanguageDetector.Detect(inputText);
                targetLang = LanguageDetector.GetTargetLanguage(detected);
            }

            var result = await _translator.TranslateAsync(inputText, sourceLang, targetLang);
            ResultTextBox.Text = result.TranslatedText;
        }
        catch (Exception ex)
        {
            ResultTextBox.Text = $"翻译失败: {ex.Message}";
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

    private string GetSelectedSourceLanguage()
    {
        if (SourceLanguageCombo.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? "auto";
        return "auto";
    }

    private string GetSelectedTargetLanguage()
    {
        if (TargetLanguageCombo.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? "en";
        return "en";
    }
}
