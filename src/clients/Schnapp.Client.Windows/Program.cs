namespace Schnapp.Client.Windows;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Schnapp.Client.Windows.Forms;
using Schnapp.Client.Windows.Services;

/// <summary>
/// Windows tray client entry point (Phase 10). Requires .NET Framework 4.8.
/// </summary>
/// <remarks>
/// Called by the OS / user when launching <c>schnapp-client-windows.exe</c>.
/// Enforces a single instance so two capture timers cannot run for the same profile.
/// </remarks>
internal static class Program
{
    private static Mutex? _singleInstance;
    private static int _fatalUiShown;

    /// <summary>Application entry point.</summary>
    [STAThread]
    public static void Main(string[] args)
    {
        AppLog.InitFromArgs(args);

        // Single instance per user session (shared config would otherwise double-capture).
        const string mutexName = @"Local\SchnappClientWindows";
        _singleInstance = new Mutex(true, mutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Schnapp is already running in the system tray.",
                "Schnapp",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += OnUiThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.ProcessExit += (_, __) =>
        {
            try { AppLog.Info("Process exit"); }
            catch { /* ignore */ }
        };

        try
        {
            AppLog.Info("Schnapp Windows client starting");
            Application.Run(new TrayShellForm());
            AppLog.Info("Application.Run returned (normal shutdown)");
        }
        catch (Exception ex)
        {
            AppLog.Error("Fatal exception escaped Application.Run", ex);
            ShowFatalUi("Schnapp encountered a fatal error and must close.", ex);
        }
        finally
        {
            try
            {
                _singleInstance?.ReleaseMutex();
            }
            catch
            {
                // ignore
            }

            _singleInstance?.Dispose();
        }
    }

    private static void OnUiThreadException(object sender, ThreadExceptionEventArgs e)
    {
        AppLog.Error("UI thread exception", e.Exception);
        // Keep the message pump alive for non-critical UI glitches; user can Quit from tray.
        try
        {
            MessageBox.Show(
                "Schnapp hit an unexpected error:\n\n" + SafeMessage(e.Exception)
                + "\n\nDetails were written to the log under %LocalAppData%\\Schnapp\\logs\\",
                "Schnapp",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // ignore secondary UI failures
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        AppLog.Error(
            "Unhandled exception (IsTerminating=" + e.IsTerminating + ")",
            ex ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));

        if (e.IsTerminating)
        {
            ShowFatalUi("Schnapp must close due to a fatal error.", ex);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            AppLog.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        }
        catch
        {
            // never throw from the handler
        }
    }

    private static void ShowFatalUi(string headline, Exception? ex)
    {
        // Only one modal if several handlers fire during teardown.
        if (Interlocked.Exchange(ref _fatalUiShown, 1) != 0)
        {
            return;
        }

        try
        {
            MessageBox.Show(
                headline + "\n\n" + SafeMessage(ex)
                + "\n\nLog: %LocalAppData%\\Schnapp\\logs\\",
                "Schnapp",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // ignore
        }
    }

    private static string SafeMessage(Exception? ex)
    {
        if (ex == null)
        {
            return "(no details)";
        }

        var msg = ex.GetType().Name + ": " + (ex.Message ?? "");
        if (msg.Length > 800)
        {
            msg = msg.Substring(0, 800) + "…";
        }

        return msg;
    }
}
