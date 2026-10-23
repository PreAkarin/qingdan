using System.IO;
using System.Windows;
using System.Windows.Threading;
using Qingdan.Services;
using Qingdan.Views;

namespace Qingdan;

public partial class App : Application
{
    private readonly StartupService _startupService = new();
    private SingleInstanceService? _singleInstance;
    private TrayService? _tray;
    private DesktopLayerService? _desktopLayer;
    private MainWindow? _mainWindow;
    private WidgetWindow? _widgetWindow;
    private SettingsWindow? _settingsWindow;
    private bool _syncingAutoStart;
    private bool _trayTipShown;
    private bool _loadWarningReported;
    private bool _disposed;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                TryLog("unhandled", exception);
            }
        };
    }

    public static App Instance => (App)Current;

    public AppState State { get; private set; } = null!;

    public TrayService? Tray => _tray;

    public bool IsExiting { get; internal set; }

    public MainWindow? MainWindowInstance => _mainWindow;

    private bool IsPreview { get; set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            if (TryGetPreviewDirectory(e.Args, out var previewDirectory))
            {
                IsPreview = true;
                await PreviewRunner.RunAsync(this, previewDirectory);
                IsExiting = true;
                Shutdown();
                return;
            }

            var isAutoStart = e.Args.Any(arg => string.Equals(arg, "--autostart", StringComparison.OrdinalIgnoreCase));
            var dataDirectory = ResolveDataDirectory(e.Args);

            _singleInstance = new SingleInstanceService();
            if (!_singleInstance.TryAcquire(out var alreadyRunning))
            {
                _singleInstance.Dispose();
                _singleInstance = null;
                SingleInstanceService.NotifyExistingInstance(showMain: !isAutoStart);
                Shutdown();
                return;
            }

            _singleInstance.StartListening(() => ShowMain());

            var store = new DataStore(dataDirectory);
            var loadResult = store.Load();
            State = new AppState(store, loadResult);
            State.SettingsChanged += OnSettingsChanged;

            var startupError = ApplyAutoStartOnLaunch();
            var firstLaunch = State.Settings.FirstRun;
            if (firstLaunch)
            {
                State.Settings.FirstRun = false;
                State.ScheduleSave();
            }

            try
            {
                _tray = new TrayService(this, State);
            }
            catch (Exception ex)
            {
                TryLog("tray", ex);
            }

            if (State.Settings.ShowWidget)
            {
                EnsureWidget();
            }

            if (!isAutoStart)
            {
                ShowMain();
            }
            else if (firstLaunch)
            {
                // First launch through autostart still gets a friendly signal that
                // the app is alive in the tray.
                _ = Dispatcher.BeginInvoke(
                    () => _tray?.ShowBalloon("清单已启动", "桌面悬浮窗已就位，可在托盘中打开主界面。"),
                    DispatcherPriority.ApplicationIdle);
            }

            if (!string.IsNullOrWhiteSpace(startupError))
            {
                var message = startupError;
                _ = Dispatcher.BeginInvoke(() =>
                {
                    if (_mainWindow is { IsVisible: true } main)
                    {
                        MessageBox.Show(main, message, "清单", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        _tray?.ShowBalloon("开机自启未生效", message, warning: true);
                    }
                }, DispatcherPriority.ApplicationIdle);
            }
        }
        catch (Exception ex)
        {
            TryLog("startup", ex);
            if (!IsPreview)
            {
                MessageBox.Show($"清单启动失败：{ex.Message}", "清单", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    public void ShowMain(bool focusComposer = false)
    {
        if (IsExiting)
        {
            return;
        }

        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(this, State);
            MainWindow = _mainWindow;
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        _mainWindow.PrepareForShow(focusComposer);
        if (focusComposer)
        {
            _mainWindow.FocusComposer();
        }
    }

    public void ShowSettings()
    {
        if (IsExiting || State is null)
        {
            return;
        }

        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Show();
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(this, State);
        if (_mainWindow is { IsVisible: true })
        {
            _settingsWindow.Owner = _mainWindow;
            _settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            _settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void NotifyMainHidden()
    {
        if (_trayTipShown || IsExiting)
        {
            return;
        }

        _trayTipShown = true;
        _tray?.ShowBalloon("清单仍在后台运行", "主窗口已收到托盘，桌面悬浮窗会继续待在桌面上。");
    }

    public void ReportLoadWarning(string message)
    {
        if (_loadWarningReported)
        {
            return;
        }

        _loadWarningReported = true;
        MessageBox.Show(_mainWindow is { IsVisible: true } main ? main : null,
            message, "清单", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void ExitApp()
    {
        if (IsExiting)
        {
            return;
        }

        IsExiting = true;
        try
        {
            State?.FlushSave();
            _mainWindow?.AllowCloseForExit();
            _desktopLayer?.Dispose();
            _desktopLayer = null;
            _widgetWindow?.Close();
            _mainWindow?.Close();
            _settingsWindow?.Close();
            _tray?.Dispose();
            _singleInstance?.Dispose();
        }
        catch (Exception ex)
        {
            TryLog("exit", ex);
        }
        finally
        {
            Shutdown();
        }
    }

    private void EnsureWidget()
    {
        if (_widgetWindow is null)
        {
            _widgetWindow = new WidgetWindow(this, State);
            _widgetWindow.Closed += (_, _) =>
            {
                _widgetWindow = null;
                if (!IsExiting)
                {
                    State.Settings.ShowWidget = false;
                }
            };

            _widgetWindow.Show();
            _desktopLayer = new DesktopLayerService(GetDesktopLogPath());
            try
            {
                _desktopLayer.Attach(_widgetWindow);
            }
            catch (Exception ex)
            {
                TryLog("desktop-layer", ex);
            }
        }
        else
        {
            if (_desktopLayer is not null)
            {
                _desktopLayer.SetVisible(true);
            }
            else if (!_widgetWindow.IsVisible)
            {
                _widgetWindow.Show();
                _desktopLayer = new DesktopLayerService(GetDesktopLogPath());
                try
                {
                    _desktopLayer.Attach(_widgetWindow);
                }
                catch (Exception ex)
                {
                    TryLog("desktop-layer", ex);
                }
            }
        }

        State.NotifyWidgetVisibilityChanged();
        _tray?.RefreshMenuState();
    }

    private void HideWidget()
    {
        if (_widgetWindow is null)
        {
            return;
        }

        if (_desktopLayer is not null)
        {
            _desktopLayer.SetVisible(false);
        }
        else
        {
            _widgetWindow.Hide();
        }

        State.NotifyWidgetVisibilityChanged();
        _tray?.RefreshMenuState();
    }

    private void OnSettingsChanged(AppState sender, string? propertyName)
    {
        _tray?.RefreshMenuState();

        if (string.Equals(propertyName, nameof(Models.AppSettings.ShowWidget), StringComparison.Ordinal))
        {
            if (State.Settings.ShowWidget)
            {
                EnsureWidget();
            }
            else
            {
                HideWidget();
            }
        }
        else if (string.Equals(propertyName, nameof(Models.AppSettings.AutoStart), StringComparison.Ordinal) &&
                 !_syncingAutoStart)
        {
            ApplyAutoStartFromSettings();
        }
    }

    private string? ApplyAutoStartOnLaunch()
    {
        if (_startupService.ExecutablePath is null)
        {
            return null;
        }

        try
        {
            if (State.Settings.AutoStart)
            {
                if (!_startupService.SetEnabled(true, out var error))
                {
                    _syncingAutoStart = true;
                    State.Settings.AutoStart = false;
                    _syncingAutoStart = false;
                    State.ScheduleSave();
                    return error ?? "无法写入开机启动项。";
                }
            }
            else
            {
                _startupService.SetEnabled(false, out _);
            }
        }
        catch (Exception ex)
        {
            TryLog("autostart", ex);
            return $"设置开机自启失败：{ex.Message}";
        }

        return null;
    }

    private void ApplyAutoStartFromSettings()
    {
        if (_startupService.ExecutablePath is null)
        {
            // Development-hosted runs must never point the registry at dotnet.exe.
            return;
        }

        var enabled = State.Settings.AutoStart;
        if (_startupService.SetEnabled(enabled, out var error))
        {
            return;
        }

        _syncingAutoStart = true;
        State.Settings.AutoStart = !enabled;
        _syncingAutoStart = false;
        State.ScheduleSave();

        var message = error ?? "无法修改开机启动项。";
        if (_mainWindow is { IsVisible: true } main)
        {
            MessageBox.Show(main, message, "清单", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            _tray?.ShowBalloon("开机自启设置失败", message, warning: true);
        }
    }

    private void Cleanup()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            State?.FlushSave();
        }
        catch
        {
            // Best effort.
        }

        try
        {
            _tray?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        _tray = null;

        try
        {
            _desktopLayer?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        _desktopLayer = null;

        try
        {
            _singleInstance?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        _singleInstance = null;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryLog("dispatcher", e.Exception);
        if (IsPreview)
        {
            Console.Error.WriteLine(e.Exception);
            e.Handled = true;
            return;
        }

        e.Handled = true;
        try
        {
            MessageBox.Show(
                $"清单遇到了一点问题：{e.Exception.Message}\n\n你的清单数据已自动保存。",
                "清单",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // If the UI is too broken to show a dialog, keep the process alive in the tray.
        }
    }

    private string? GetDesktopLogPath()
    {
        if (Environment.GetCommandLineArgs().Any(arg =>
                string.Equals(arg, "--diagnose-desktop", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                Directory.CreateDirectory(State.Store.DataDirectory);
                return Path.Combine(State.Store.DataDirectory, "desktop-layer.log");
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private string? ResolveDataDirectory(string[] args)
    {
        const string prefix = "--data-dir=";
        var fromArgs = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (fromArgs is not null)
        {
            return fromArgs[prefix.Length..].Trim('"');
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("QINGDAN_DATA_DIR");
        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment;
    }

    private static bool TryGetPreviewDirectory(string[] args, out string directory)
    {
        const string prefix = "--preview=";
        var value = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (value is not null)
        {
            directory = value[prefix.Length..].Trim('"');
            return true;
        }

        if (args.Any(arg => string.Equals(arg, "--preview", StringComparison.OrdinalIgnoreCase)))
        {
            directory = Path.Combine(Path.GetTempPath(), "QingdanPreview-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            return true;
        }

        directory = string.Empty;
        return false;
    }

    private void TryLog(string area, Exception exception)
    {
        try
        {
            var folder = State?.Store.DataDirectory ??
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Qingdan");
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "error.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{area}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
