using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Translater.Core.Interfaces;
using Translater.Core.Models;
using Translater.Core.Services;
using Translater.Infrastructure.Translators;
using Translater.Infrastructure.Ocr;
using Translater.Infrastructure.Storage;
using Translater_App.Helpers;
using WinRT.Interop;
using System.Runtime.InteropServices;

namespace Translater_App;

public sealed partial class MainWindow : Window
{
    private readonly ITranslationService _translator;
    private readonly IOcrService _ocrService;
    private readonly IHistoryService _historyService;
    private HotKeyManager? _hotKeyManager;
    private TrayIconManager? _trayIcon;
    private AppSettings _settings;
    private bool _forceClose;

    // Win32 message hook
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int GWLP_WNDPROC = -4;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

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
        _settings = AppSettings.Load();

        var storageDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Translater");
        _historyService = new JsonHistoryStore(storageDir);

        // Register hotkey and tray icon
        var hwnd = WindowNative.GetWindowHandle(this);
        RegisterGlobalHotKey(hwnd);
        SetupTrayIcon(hwnd);

        // Apply saved theme
        if (this.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = _settings.Theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        // Update hotkey hint in status bar
        HotkeyHintText.Text = $"{_settings.GetHotkeyDisplayString()} 截屏翻译";

        // Intercept close to minimize to tray
        AppWindow.Closing += AppWindow_Closing;
    }

    private void SetupTrayIcon(IntPtr hwnd)
    {
        _trayIcon = new TrayIconManager();

        // Resolve icon path relative to exe
        var exeDir = AppContext.BaseDirectory;
        var iconPath = Path.Combine(exeDir, "Assets", "AppIcon.ico");

        _trayIcon.OnShowWindow = () =>
        {
            ShowWindow(hwnd, SW_SHOW);
            var presenter = AppWindow.Presenter as OverlappedPresenter;
            presenter?.Restore();
            this.Activate();
        };
        _trayIcon.OnScreenshot = () =>
        {
            DispatcherQueue.TryEnqueue(StartScreenshotTranslation);
        };
        _trayIcon.OnExit = () =>
        {
            _forceClose = true;
            _trayIcon?.Dispose();
            _hotKeyManager?.Dispose();
            this.Close();
        };

        _trayIcon.Create(hwnd, iconPath, "Translater - 中英翻译");
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_forceClose)
        {
            args.Cancel = true;
            var hwnd = WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, SW_HIDE);
        }
    }

    private void RegisterGlobalHotKey(IntPtr hwnd)
    {
        _hotKeyManager = new HotKeyManager(hwnd);
        _hotKeyManager.Register(_settings.HotkeyModifiers, _settings.HotkeyKey, () =>
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
        if (msg == TrayIconManager.WM_TRAYICON)
        {
            _trayIcon?.HandleTrayMessage(lParam);
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

            await _historyService.SaveAsync(new HistoryItem
            {
                OriginalText = ocrResult.Text,
                TranslatedText = result.TranslatedText,
                SourceLanguage = "auto",
                TargetLanguage = targetLang,
                TranslationSource = result.SourceName,
                Timestamp = DateTime.Now
            });
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
        StatusText.Text = "翻译中...";
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
            StatusText.Text = $"{result.SourceName} · {inputText.Length} 字符";

            // Save to history
            await _historyService.SaveAsync(new HistoryItem
            {
                OriginalText = inputText,
                TranslatedText = result.TranslatedText,
                SourceLanguage = sourceLang,
                TargetLanguage = targetLang,
                TranslationSource = result.SourceName,
                Timestamp = DateTime.Now
            });
        }
        catch (TaskCanceledException)
        {
            ResultTextBox.Text = "翻译超时，请检查网络";
            StatusText.Text = "超时";
        }
        catch (HttpRequestException ex)
        {
            ResultTextBox.Text = $"网络错误: {ex.Message}";
            StatusText.Text = "网络错误";
        }
        catch (Exception ex)
        {
            ResultTextBox.Text = $"翻译失败: {ex.Message}";
            StatusText.Text = "失败";
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

    private async void History_Click(object sender, RoutedEventArgs e)
    {
        var items = await _historyService.GetRecentAsync(30);

        var listView = new ListView
        {
            MaxHeight = 350,
            Width = 340,
            SelectionMode = ListViewSelectionMode.None
        };

        if (items.Count == 0)
        {
            listView.Items.Add(new TextBlock
            {
                Text = "暂无历史记录",
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(0, 12, 0, 12)
            });
        }
        else
        {
            foreach (var item in items)
            {
                var sp = new StackPanel { Spacing = 2, Padding = new Thickness(0, 4, 0, 4) };
                sp.Children.Add(new TextBlock
                {
                    Text = item.OriginalText.Length > 50 ? item.OriginalText[..50] + "..." : item.OriginalText,
                    FontSize = 13,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                sp.Children.Add(new TextBlock
                {
                    Text = item.TranslatedText.Length > 50 ? item.TranslatedText[..50] + "..." : item.TranslatedText,
                    FontSize = 12,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                sp.Children.Add(new TextBlock
                {
                    Text = item.Timestamp.ToString("MM-dd HH:mm"),
                    FontSize = 11,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
                });

                // Tap to load into input/result
                var historyItem = item;
                sp.Tapped += (s, args) =>
                {
                    InputTextBox.Text = historyItem.OriginalText;
                    ResultTextBox.Text = historyItem.TranslatedText;
                };

                listView.Items.Add(sp);
            }
        }

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(listView);

        if (items.Count > 0)
        {
            var clearBtn = new Button
            {
                Content = "清空历史",
                HorizontalAlignment = HorizontalAlignment.Right
            };
            clearBtn.Click += async (s, args) =>
            {
                await _historyService.ClearAllAsync();
                listView.Items.Clear();
                listView.Items.Add(new TextBlock
                {
                    Text = "已清空",
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });
            };
            panel.Children.Add(clearBtn);
        }

        var dialog = new ContentDialog
        {
            Title = "历史记录",
            Content = panel,
            CloseButtonText = "关闭",
            XamlRoot = this.Content.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        // Build settings dialog content
        var modCtrl = new CheckBox { Content = "Ctrl", IsChecked = (_settings.HotkeyModifiers & HotKeyManager.MOD_CTRL) != 0 };
        var modAlt = new CheckBox { Content = "Alt", IsChecked = (_settings.HotkeyModifiers & HotKeyManager.MOD_ALT) != 0 };
        var modShift = new CheckBox { Content = "Shift", IsChecked = (_settings.HotkeyModifiers & HotKeyManager.MOD_SHIFT) != 0 };

        var keyCombo = new ComboBox { Width = 80 };
        int selectedIdx = 0;
        // A-Z keys
        for (char c = 'A'; c <= 'Z'; c++)
        {
            keyCombo.Items.Add(c.ToString());
            if ((uint)c == _settings.HotkeyKey)
                selectedIdx = c - 'A';
        }
        // F1-F12
        for (int i = 1; i <= 12; i++)
        {
            keyCombo.Items.Add($"F{i}");
            if (_settings.HotkeyKey == (uint)(0x6F + i))
                selectedIdx = 26 + i - 1;
        }
        keyCombo.SelectedIndex = selectedIdx;

        var modPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        modPanel.Children.Add(modCtrl);
        modPanel.Children.Add(modAlt);
        modPanel.Children.Add(modShift);

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "截屏翻译快捷键", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"当前: {_settings.GetHotkeyDisplayString()}", FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        panel.Children.Add(new TextBlock { Text = "修饰键:" });
        panel.Children.Add(modPanel);
        panel.Children.Add(new TextBlock { Text = "按键:" });
        panel.Children.Add(keyCombo);

        // Theme setting
        panel.Children.Add(new Border { Height = 1, Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"], Margin = new Thickness(0, 4, 0, 4) });
        panel.Children.Add(new TextBlock { Text = "主题", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var themeCombo = new ComboBox { Width = 150 };
        themeCombo.Items.Add("跟随系统");
        themeCombo.Items.Add("浅色");
        themeCombo.Items.Add("深色");
        themeCombo.SelectedIndex = (int)_settings.Theme;
        panel.Children.Add(themeCombo);

        var dialog = new ContentDialog
        {
            Title = "设置",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            XamlRoot = this.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // Calculate new modifiers
            uint newMod = 0;
            if (modCtrl.IsChecked == true) newMod |= HotKeyManager.MOD_CTRL;
            if (modAlt.IsChecked == true) newMod |= HotKeyManager.MOD_ALT;
            if (modShift.IsChecked == true) newMod |= HotKeyManager.MOD_SHIFT;

            // Calculate new key
            uint newKey = 0x44; // default D
            if (keyCombo.SelectedIndex >= 0 && keyCombo.SelectedIndex < 26)
                newKey = (uint)('A' + keyCombo.SelectedIndex);
            else if (keyCombo.SelectedIndex >= 26)
                newKey = (uint)(0x70 + (keyCombo.SelectedIndex - 26)); // VK_F1 = 0x70

            if (newMod == 0)
            {
                newMod = HotKeyManager.MOD_ALT; // Require at least one modifier
            }

            _settings.HotkeyModifiers = newMod;
            _settings.HotkeyKey = newKey;
            _settings.Theme = (AppTheme)themeCombo.SelectedIndex;
            _settings.Save();

            // Apply theme
            if (this.Content is FrameworkElement root)
            {
                root.RequestedTheme = _settings.Theme switch
                {
                    AppTheme.Light => ElementTheme.Light,
                    AppTheme.Dark => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }

            // Re-register hotkey
            var hwnd = WindowNative.GetWindowHandle(this);
            _hotKeyManager?.Dispose();
            RegisterGlobalHotKey(hwnd);

            // Update hotkey hint
            HotkeyHintText.Text = $"{_settings.GetHotkeyDisplayString()} 截屏翻译";
        }
    }
}
