using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Translater_App.Pages;

public sealed partial class ScreenCapturePage : Page
{
    public ScreenCapturePage()
    {
        InitializeComponent();
    }

    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        CaptureButton.IsEnabled = false;
        try
        {
            // TODO: Implement screen capture overlay + OCR + translate
            OcrResultBox.Text = "[截屏功能待实现]";
            TranslationResultBox.Text = "[翻译待实现]";
        }
        finally
        {
            CaptureButton.IsEnabled = true;
        }
    }
}
