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
using Translater.Infrastructure.Translators.Offline;
using Translater.Infrastructure.Ocr;
using Translater.Infrastructure.Storage;
using Translater_App.Helpers;
using WinRT.Interop;
using System.Runtime.InteropServices;

namespace Translater_App;

public sealed partial class MainWindow : Window
{
    private ITranslationService _translator;
    private readonly BingFreeTranslator _bingTranslator;
    private readonly MarianOnnxTranslator _offlineTranslator;
    private readonly IOcrService _ocrService;
    private readonly IHistoryService _historyService;
    private HotKeyManager? _hotKeyManager;
    private TrayIconManager? _trayIcon;
    private AppSettings _settings;
    private bool _forceClose;

    // Settings fields
    private CheckBox? _settingsModCtrl, _settingsModAlt, _settingsModShift;
    private ComboBox? _settingsKeyCombo, _settingsThemeCombo, _settingsEngineCombo;

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
        _bingTranslator = new BingFreeTranslator(httpClient);
        _offlineTranslator = new MarianOnnxTranslator(AppSettings.GetModelsDir());
        _settings = AppSettings.Load();
        _translator = _settings.Engine == TranslationEngine.Offline
            ? _offlineTranslator : _bingTranslator;
        _ocrService = new PaddleOcrEngine();

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

        // Update hotkey hint
        HotkeyHintText.Text = $"{_settings.GetHotkeyDisplayString()} 截屏翻译";
        StatusText.Text = _settings.Engine == TranslationEngine.Offline ? "离线模式" : "Bing 在线";

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

        // Subclass the window to receive WM_HOTKEY (only once)
        if (_wndProcDelegate == null)
        {
            _wndProcDelegate = WndProc;
            _oldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC,
                Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
        }
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

    private string _currentPanel = "";

    private async void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (SidePanel.Visibility == Visibility.Visible && _currentPanel == "history")
        {
            CloseSidePanel();
            return;
        }
        _currentPanel = "history";
        SidePanelContent.Children.Clear();
        SidePanelTitle.Text = "历史记录";

        var items = await _historyService.GetRecentAsync(30);

        if (items.Count == 0)
        {
            SidePanelContent.Children.Add(new TextBlock
            {
                Text = "暂无历史记录",
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(0, 12, 0, 12)
            });
        }
        else
        {
            var clearBtn = new Button
            {
                Content = "清空历史",
                FontSize = 11,
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            clearBtn.Click += async (_, _) =>
            {
                await _historyService.ClearAllAsync();
                SidePanelContent.Children.Clear();
                SidePanelContent.Children.Add(new TextBlock
                {
                    Text = "已清空",
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });
            };
            SidePanelContent.Children.Add(clearBtn);

            foreach (var item in items)
            {
                var sp = new StackPanel { Spacing = 2, Padding = new Thickness(0, 6, 0, 6) };
                sp.Children.Add(new TextBlock
                {
                    Text = item.OriginalText.Length > 40 ? item.OriginalText[..40] + "..." : item.OriginalText,
                    FontSize = 13,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                sp.Children.Add(new TextBlock
                {
                    Text = item.TranslatedText.Length > 40 ? item.TranslatedText[..40] + "..." : item.TranslatedText,
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

                var historyItem = item;
                sp.Tapped += (s, args) =>
                {
                    InputTextBox.Text = historyItem.OriginalText;
                    ResultTextBox.Text = historyItem.TranslatedText;
                    CloseSidePanel();
                };

                SidePanelContent.Children.Add(sp);
            }
        }

        ShowSidePanel();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (SidePanel.Visibility == Visibility.Visible && _currentPanel == "settings")
        {
            CloseSidePanel();
            return;
        }
        _currentPanel = "settings";
        SidePanelContent.Children.Clear();
        SidePanelTitle.Text = "设置";

        BuildSettingsUI();
        ShowSidePanel();
    }

    private void CloseSidePanel_Click(object sender, RoutedEventArgs e) => CloseSidePanel();

    private const int SidePanelWidth = 300;

    private void ShowSidePanel()
    {
        if (SidePanel.Visibility == Visibility.Visible) return;
        SidePanel.Visibility = Visibility.Visible;
        SidePanelColumn.Width = new GridLength(SidePanelWidth);
        var size = AppWindow.Size;
        AppWindow.Resize(new SizeInt32(size.Width + SidePanelWidth, size.Height));
    }

    private void CloseSidePanel()
    {
        if (SidePanel.Visibility == Visibility.Collapsed) return;
        SidePanel.Visibility = Visibility.Collapsed;
        SidePanelColumn.Width = new GridLength(0);
        var size = AppWindow.Size;
        AppWindow.Resize(new SizeInt32(size.Width - SidePanelWidth, size.Height));
    }

    private void BuildSettingsUI()
    {

        // --- Hotkey ---
        _settingsModCtrl = new CheckBox { Content = "Ctrl", IsChecked = (_settings.HotkeyModifiers & HotKeyManager.MOD_CTRL) != 0, MinWidth = 0, MinHeight = 0, Padding = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        _settingsModAlt = new CheckBox { Content = "Alt", IsChecked = (_settings.HotkeyModifiers & HotKeyManager.MOD_ALT) != 0, MinWidth = 0, MinHeight = 0, Padding = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        _settingsModShift = new CheckBox { Content = "Shift", IsChecked = (_settings.HotkeyModifiers & HotKeyManager.MOD_SHIFT) != 0, MinWidth = 0, MinHeight = 0, Padding = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };

        _settingsKeyCombo = new ComboBox { Width = 60, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        int selectedIdx = 0;
        for (char c = 'A'; c <= 'Z'; c++)
        {
            _settingsKeyCombo.Items.Add(c.ToString());
            if ((uint)c == _settings.HotkeyKey) selectedIdx = c - 'A';
        }
        for (int i = 1; i <= 12; i++)
        {
            _settingsKeyCombo.Items.Add($"F{i}");
            if (_settings.HotkeyKey == (uint)(0x6F + i)) selectedIdx = 26 + i - 1;
        }
        _settingsKeyCombo.SelectedIndex = selectedIdx;

        var hotkeyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        hotkeyRow.Children.Add(_settingsModCtrl);
        hotkeyRow.Children.Add(_settingsModAlt);
        hotkeyRow.Children.Add(_settingsModShift);
        hotkeyRow.Children.Add(_settingsKeyCombo);

        // --- Theme ---
        _settingsThemeCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
        _settingsThemeCombo.Items.Add("跟随系统");
        _settingsThemeCombo.Items.Add("浅色");
        _settingsThemeCombo.Items.Add("深色");
        _settingsThemeCombo.SelectedIndex = (int)_settings.Theme;

        // --- Engine ---
        _settingsEngineCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
        _settingsEngineCombo.Items.Add("在线 (Bing)");
        _settingsEngineCombo.Items.Add("离线 (本地模型)");
        _settingsEngineCombo.SelectedIndex = (int)_settings.Engine;

        var modelDownloader = new ModelDownloader(AppSettings.GetModelsDir());
        var zhEnReady = modelDownloader.IsModelDownloaded("opus-mt-zh-en");
        var enZhReady = modelDownloader.IsModelDownloaded("opus-mt-en-zh");

        var downloadBtn = new Button
        {
            Content = zhEnReady && enZhReady ? "模型已就绪 ✓" : "下载离线模型 (~115MB)",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !zhEnReady || !enZhReady,
            Padding = new Thickness(0, 6, 0, 6)
        };

        var modelProgress = new TextBlock
        {
            FontSize = 11,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };

        downloadBtn.Click += async (_, _) =>
        {
            downloadBtn.IsEnabled = false;
            downloadBtn.Content = "下载中...";
            try
            {
                var progress = new Progress<(long downloaded, long total, string file)>(p =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        var mb = p.downloaded / 1024.0 / 1024.0;
                        modelProgress.Text = $"{p.file} ({mb:F1} MB)";
                    });
                });
                if (!zhEnReady) await modelDownloader.DownloadModelAsync("opus-mt-zh-en", progress);
                if (!enZhReady) await modelDownloader.DownloadModelAsync("opus-mt-en-zh", progress);
                modelProgress.Text = "";
                downloadBtn.Content = "模型已就绪 ✓";
            }
            catch (Exception ex)
            {
                modelProgress.Text = ex.Message;
                downloadBtn.Content = "重试下载";
                downloadBtn.IsEnabled = true;
            }
        };

        // --- Build layout ---
        var hotkeySection = new StackPanel { Spacing = 6 };
        hotkeySection.Children.Add(new TextBlock { Text = "截屏翻译快捷键", FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        hotkeySection.Children.Add(hotkeyRow);
        SidePanelContent.Children.Add(hotkeySection);

        var themeSection = new StackPanel { Spacing = 6 };
        themeSection.Children.Add(new TextBlock { Text = "主题", FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        themeSection.Children.Add(_settingsThemeCombo);
        SidePanelContent.Children.Add(themeSection);

        var engineSection = new StackPanel { Spacing = 6 };
        engineSection.Children.Add(new TextBlock { Text = "翻译引擎", FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        engineSection.Children.Add(_settingsEngineCombo);
        engineSection.Children.Add(downloadBtn);
        engineSection.Children.Add(modelProgress);
        SidePanelContent.Children.Add(engineSection);

        var saveBtn = new Button
        {
            Content = "保存",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 6, 0, 6),
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0)
        };
        saveBtn.Click += SaveSettings_Click;
        SidePanelContent.Children.Add(saveBtn);
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        uint newMod = 0;
        if (_settingsModCtrl?.IsChecked == true) newMod |= HotKeyManager.MOD_CTRL;
        if (_settingsModAlt?.IsChecked == true) newMod |= HotKeyManager.MOD_ALT;
        if (_settingsModShift?.IsChecked == true) newMod |= HotKeyManager.MOD_SHIFT;

        uint newKey = 0x44;
        if (_settingsKeyCombo != null)
        {
            if (_settingsKeyCombo.SelectedIndex >= 0 && _settingsKeyCombo.SelectedIndex < 26)
                newKey = (uint)('A' + _settingsKeyCombo.SelectedIndex);
            else if (_settingsKeyCombo.SelectedIndex >= 26)
                newKey = (uint)(0x70 + (_settingsKeyCombo.SelectedIndex - 26));
        }

        if (newMod == 0) newMod = HotKeyManager.MOD_ALT;

        _settings.HotkeyModifiers = newMod;
        _settings.HotkeyKey = newKey;
        _settings.Theme = (AppTheme)(_settingsThemeCombo?.SelectedIndex ?? 0);
        _settings.Engine = (TranslationEngine)(_settingsEngineCombo?.SelectedIndex ?? 0);
        _settings.Save();

        _translator = _settings.Engine == TranslationEngine.Offline
            ? _offlineTranslator : _bingTranslator;

        if (this.Content is FrameworkElement root)
        {
            root.RequestedTheme = _settings.Theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        _hotKeyManager?.Dispose();
        RegisterGlobalHotKey(hwnd);
        HotkeyHintText.Text = $"{_settings.GetHotkeyDisplayString()} 截屏翻译";
        StatusText.Text = _settings.Engine == TranslationEngine.Offline ? "离线模式" : "Bing 在线";

        CloseSidePanel();
    }
}
