using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using DMS.Desktop.Services;
using DMS.Desktop.UI;

namespace DMS.Desktop;

public partial class App : Application
{
    private static int _isShowingUnhandledException;

    public App()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
        {
            DmsWindowChromeStyler.ApplyFromResources(window);
        }
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        WriteEmergencyLog("UI_DISPATCHER_UNHANDLED", e.Exception);

        // Prevent one faulty transaction/view from terminating the whole desktop client.
        // Fatal CLR/process failures still remain outside this recoverable path.
        e.Handled = true;
        ShowUnhandledException(e.Exception);
    }

    private static void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        WriteEmergencyLog(
            e.IsTerminating ? "APPDOMAIN_FATAL" : "APPDOMAIN_UNHANDLED",
            e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        WriteEmergencyLog("TASK_UNOBSERVED", e.Exception);
        e.SetObserved();
    }

    private static void ShowUnhandledException(Exception exception)
    {
        if (Interlocked.Exchange(ref _isShowingUnhandledException, 1) != 0)
        {
            return;
        }

        try
        {
            DmsMessage.Error(
                "DMS",
                "An unexpected error occurred. The error was recorded in the emergency log. " +
                "The current operation was stopped, but the application will remain open when possible.\n\n" +
                exception.Message);
        }
        catch
        {
            // Never allow the error dialog itself to cause another application failure.
        }
        finally
        {
            Interlocked.Exchange(ref _isShowingUnhandledException, 0);
        }
    }

    private static void WriteEmergencyLog(string action, Exception? exception)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DMS",
                "Logs");
            Directory.CreateDirectory(logDirectory);

            var path = Path.Combine(logDirectory, "dms-emergency.log");
            var text = new StringBuilder()
                .AppendLine("------------------------------------------------------------")
                .AppendLine($"TimestampUtc={DateTimeOffset.UtcNow:O}")
                .AppendLine($"Action={action}")
                .AppendLine($"User={Environment.UserDomainName}\\{Environment.UserName}")
                .AppendLine($"Process={Environment.ProcessPath}")
                .AppendLine(exception?.ToString() ?? "No exception object was supplied.")
                .ToString();

            File.AppendAllText(path, text, Encoding.UTF8);
        }
        catch
        {
            // Emergency logging must never throw back into the application.
        }
    }
}
