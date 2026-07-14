using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using SmartWindowTool.Core;
using SmartWindowTool.Models;

namespace SmartWindowTool;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    internal const string ReplaceInstanceArgument = "--replace-instance";
    internal const string SyncAutoStartArgument = "--sync-autostart";
    private MainWindow? _mainWindow;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private readonly OperationVersion _autoStartRefreshVersion = new();

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (AutoStartService.IsHelperRequest(
            e.Args,
            out bool enable,
            out bool runAsAdmin,
            out string userName,
            out string userSid))
        {
            int exitCode = AutoStartService.RunElevatedHelper(enable, runAsAdmin, userName, userSid);
            Shutdown(exitCode);
            return;
        }

        bool waitForPreviousInstance = Array.Exists(e.Args,
            arg => arg.Equals(ReplaceInstanceArgument, StringComparison.OrdinalIgnoreCase)) || ConsumeRestartMarker();
        if (!AcquireSingleInstance(waitForPreviousInstance))
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        var settings = AppSettings.Load();

        bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        // Ensure the setting reflects the actual state if it was launched as admin by other means
        if (isAdmin)
        {
            settings.RunAsAdmin = true;
        }

        if (settings.RunAsAdmin && !isAdmin)
        {
            string? executablePath = Environment.ProcessPath;
            var processInfo = new ProcessStartInfo(executablePath ?? string.Empty)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"{ReplaceInstanceArgument} {SyncAutoStartArgument}"
            };
            try
            {
                Process.Start(processInfo);
                Application.Current.Shutdown();
                return;
            }
            catch
            {
                // User cancelled UAC or failed
                settings.RunAsAdmin = false;
                settings.Save();
            }
        }

        SystemThemeService.Initialize();

        _mainWindow = new MainWindow(settings);
        _mainWindow.Closed += (_, _) => _mainWindow = null;
        StartActivationListener();
        bool forceAutoStartSync = Array.Exists(e.Args,
            arg => arg.Equals(SyncAutoStartArgument, StringComparison.OrdinalIgnoreCase));
        _ = RefreshAutoStartStateAsync(settings, forceAutoStartSync);
        
        if (!settings.SilentStart && !AutoStartService.IsAutoStartLaunch(e.Args))
        {
            _mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemThemeService.Shutdown();
        _activationWait?.Unregister(null);
        _activationEvent?.Dispose();

        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _ownsSingleInstanceMutex = false;
        }
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }

    private bool AcquireSingleInstance(bool waitForPreviousInstance)
    {
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        string mutexName = $@"Local\SmartWindowTool.Instance.{sid}";
        string activationEventName = $@"Local\SmartWindowTool.Activate.{sid}";
        var userSid = new SecurityIdentifier(sid);

        var mutexSecurity = new MutexSecurity();
        mutexSecurity.AddAccessRule(new MutexAccessRule(
            userSid,
            MutexRights.FullControl,
            AccessControlType.Allow));
        _singleInstanceMutex = MutexAcl.Create(
            true,
            mutexName,
            out bool createdNew,
            mutexSecurity);
        if (createdNew)
        {
            _ownsSingleInstanceMutex = true;
        }
        else if (waitForPreviousInstance)
        {
            try
            {
                _ownsSingleInstanceMutex = _singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                _ownsSingleInstanceMutex = true;
            }
        }

        if (_ownsSingleInstanceMutex)
        {
            var eventSecurity = new EventWaitHandleSecurity();
            eventSecurity.AddAccessRule(new EventWaitHandleAccessRule(
                userSid,
                EventWaitHandleRights.FullControl,
                AccessControlType.Allow));
            _activationEvent = EventWaitHandleAcl.Create(
                false,
                EventResetMode.AutoReset,
                activationEventName,
                out _,
                eventSecurity);
        }
        return _ownsSingleInstanceMutex;
    }

    private void StartActivationListener()
    {
        if (_activationEvent == null) return;

        _activationWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        MainWindow? window = _mainWindow;
                        if (window != null)
                        {
                            window.ShowFromSecondaryInstance();
                        }
                    });
                }
            },
            null,
            Timeout.Infinite,
            false);
    }

    private static void SignalExistingInstance()
    {
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        string activationEventName = $@"Local\SmartWindowTool.Activate.{sid}";
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using EventWaitHandle activationEvent = EventWaitHandle.OpenExisting(activationEventName);
                activationEvent.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    private async System.Threading.Tasks.Task RefreshAutoStartStateAsync(AppSettings settings, bool forceSync)
    {
        int refreshVersion = _autoStartRefreshVersion.Capture();
        try
        {
            AutoStartConfigurationState legacyState = await System.Threading.Tasks.Task.Run(
                AutoStartService.GetLegacyTaskConfigurationState);
            AutoStartConfigurationState configurationState = legacyState == AutoStartConfigurationState.Configured
                ? AutoStartConfigurationState.Configured
                : await System.Threading.Tasks.Task.Run(AutoStartService.GetConfigurationStateForCurrentUser);

            if (settings.AutoStart &&
                (legacyState == AutoStartConfigurationState.Configured || forceSync) &&
                AutoStartService.IsCurrentProcessElevated())
            {
                AutoStartResult migrationResult = await AutoStartService.ConfigureForCurrentUserAsync(true, settings.RunAsAdmin);
                if (migrationResult.Succeeded)
                {
                    configurationState = AutoStartConfigurationState.Configured;
                }
            }

            if (configurationState == AutoStartConfigurationState.Unknown ||
                !_autoStartRefreshVersion.IsCurrent(refreshVersion))
            {
                return;
            }

            await Dispatcher.BeginInvoke(() =>
            {
                if (_autoStartRefreshVersion.IsCurrent(refreshVersion))
                {
                    settings.AutoStart = configurationState == AutoStartConfigurationState.Configured;
                }
            });
        }
        catch
        {
            // Keep the persisted value when Task Scheduler cannot be queried.
        }
    }

    internal void InvalidateAutoStartRefresh()
    {
        _autoStartRefreshVersion.Invalidate();
    }

    internal void PrepareForRestart()
    {
        string path = GetRestartMarkerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    internal void CancelRestart()
    {
        try
        {
            File.Delete(GetRestartMarkerPath());
        }
        catch
        {
        }
    }

    private static bool ConsumeRestartMarker()
    {
        string path = GetRestartMarkerPath();
        try
        {
            if (!File.Exists(path)) return false;

            DateTime createdAt = File.GetLastWriteTimeUtc(path);
            File.Delete(path);
            return DateTime.UtcNow - createdAt < TimeSpan.FromMinutes(1);
        }
        catch
        {
            return false;
        }
    }

    private static string GetRestartMarkerPath()
    {
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartWindowTool",
            $"restart-{sid}.marker");
    }
}

internal sealed class OperationVersion
{
    private int _version;

    public int Capture() => Volatile.Read(ref _version);

    public void Invalidate() => Interlocked.Increment(ref _version);

    public bool IsCurrent(int version) => version == Volatile.Read(ref _version);
}

