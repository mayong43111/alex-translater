using Microsoft.UI.Xaml;

namespace Translater_App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Global\\AlexTranslater_SingleInstance";
    private const string ActivateInstanceEventName = "Global\\AlexTranslater_Activate";
    private static System.Threading.Mutex? _singleInstanceMutex;
    private static System.Threading.EventWaitHandle? _activateInstanceEvent;
    private static System.Threading.CancellationTokenSource? _activationListenerCts;
    private Window? _window;
    private static readonly string CrashLogPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlexTranslater", "crash.log");

    public App()
    {
        InitializeComponent();
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("AppDomain", e.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash("TaskScheduler", e.Exception);
            e.SetObserved();
        };
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogCrash("WinUI", e.Exception);
        e.Handled = true;
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(CrashLogPath)!;
            System.IO.Directory.CreateDirectory(dir);
            var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\n\n";
            System.IO.File.AppendAllText(CrashLogPath, msg);
        }
        catch { }
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _singleInstanceMutex = new System.Threading.Mutex(initiallyOwned: true, name: SingleInstanceMutexName, createdNew: out var createdNew);
        _activateInstanceEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, ActivateInstanceEventName);
        if (!createdNew)
        {
            _activateInstanceEvent.Set();
            Exit();
            return;
        }

        StartActivationListener();

        _window = new MainWindow();
        _window.Activate();
    }

    private void StartActivationListener()
    {
        _activationListenerCts = new System.Threading.CancellationTokenSource();
        var token = _activationListenerCts.Token;

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                _activateInstanceEvent?.WaitOne();
                if (token.IsCancellationRequested)
                    break;

                _window?.DispatcherQueue.TryEnqueue(() =>
                {
                    if (_window is MainWindow mainWindow)
                        mainWindow.BringToFront();
                    else
                        _window?.Activate();
                });
            }
        }, token);
    }
}
