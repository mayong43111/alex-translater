using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Translater.Core.Interfaces;
using Translater.Core.Services;
using Translater.Infrastructure.Translators;
using Translater.Infrastructure.Ocr;
using Translater_App.Helpers;
using WinRT.Interop;
using System.Runtime.InteropServices;

namespace Translater_App;

public sealed partial class MainWindow : Window
{
    private readonly ITranslationService _translator;
    private readonly IOcrService _ocrService;
    private HotKeyManager? _hotKeyManager;

    // Win32 message hook
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private const int GWLP_WNDPROC = -4;

    private IntPtr _oldWndProc;
    private WndProcDelegate? _wndProcDelegate;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Compact window size like Youdao
        AppWindow.Resize(new SizeInt32(420, 520));

        var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        _translator = new BingFreeTranslator(httpClient);
        _ocrService = new PaddleOcrEngine();

        // Register hotkey after window is ready
        var hwnd = WindowNative.GetWindowHandle(this);
        RegisterGlobalHotKey(hwnd);
    }

    private void RegisterGlobalHotKey(IntPtr hwnd)
    {
        _hotKeyManager = new HotKeyManager(hwnd);
        // Alt+D for screenshot translation
        _hotKeyManager.Register(HotKeyManager.MOD_ALT, 0x44 /* VK_D */, () =>
        {
            DispatcherQueue.TryEnqueue(StartScreenshotTranslation);
        });

        // Subclass the window to receive WM_HOTKEY
        _wndProcDelegate = WndProc;
        _oldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == HotKeyManager.WM_HOTKEY)
        {
            _hotKeyManager?.HandleHotKey((int)wParam);
            return IntPtr.Zero;
        }
        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    private async void StartScreenshotTranslation()
    {
        try
        {
            // Minimize main window before capture
            var presenter = AppWindow.Presenter as OverlappedPresenter;
            presenter?.Minimize();
            await Task.Delay(300); // Wait for minimize animation

            // Capture full screen
            var screenData = ScreenCapture.CaptureFullScreen();

            // Show overlay for region selection (runs on a STA thread)
            var (regionSelected, region) = await Task.Run(() =>
            {
                (bool selected, System.Drawing.Rectangle rect) result = (false, default);

                var thread = new Thread(() =>
                {
                    result = ScreenOverlay.ShowAndSelect(screenData);
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                return result;
            });

            // Restore window
            presenter?.Restore();
            this.Activate();

            if (!regionSelected)
            {
                InputTextBox.Text = "";
                ResultTextBox.Text = "截屏取消";
                return;
            }
            if (region.Width < 3 || region.Height < 3)
            {
                InputTextBox.Text = "";
                ResultTextBox.Text = $"选区太小 ({region.Width}×{region.Height})";
                return;
            }

            // Crop the selected region
            var croppedImage = ScreenCapture.CropRegion(screenData, region.X, region.Y, region.Width, region.Height);

            // OCR (PaddleOCR handles both Chinese and English)
            InputTextBox.Text = $"识别中... (选区 {region.Width}×{region.Height})";
            ResultTextBox.Text = "";

            var ocrResult = await _ocrService.RecognizeAsync(croppedImage, "auto");

            if (string.IsNullOrWhiteSpace(ocrResult.Text))
            {
                InputTextBox.Text = "";
                ResultTextBox.Text = "未识别到文字";
                return;
            }

            InputTextBox.Text = ocrResult.Text;

            // Auto translate
            ResultTextBox.Text = "翻译中...";
            var detected = LanguageDetector.Detect(ocrResult.Text);
            var targetLang = LanguageDetector.GetTargetLanguage(detected);

            var result = await _translator.TranslateAsync(ocrResult.Text, "auto", targetLang);
            ResultTextBox.Text = result.TranslatedText;
        }
        catch (Exception ex)
        {
            ResultTextBox.Text = $"截屏翻译失败: {ex.Message}";
        }
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
            var sourceLang = GetSelectedLanguage(SourceLanguageCombo, "auto");
            var targetLang = GetSelectedLanguage(TargetLanguageCombo, "en");

            if (sourceLang == "auto")
            {
                var detected = LanguageDetector.Detect(inputText);
                targetLang = LanguageDetector.GetTargetLanguage(detected);
            }

            var result = await _translator.TranslateAsync(inputText, sourceLang, targetLang);
            ResultTextBox.Text = result.TranslatedText;
        }
        catch (TaskCanceledException)
        {
            ResultTextBox.Text = "翻译超时，请检查网络";
        }
        catch (HttpRequestException ex)
        {
            ResultTextBox.Text = $"网络错误: {ex.Message}";
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
        if (sourceIndex == 0) return;
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

    private void ScreenshotTranslate_Click(object sender, RoutedEventArgs e)
    {
        StartScreenshotTranslation();
    }

    private static string GetSelectedLanguage(ComboBox combo, string fallback)
    {
        if (combo.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? fallback;
        return fallback;
    }
}
