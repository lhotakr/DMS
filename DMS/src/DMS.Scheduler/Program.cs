using DMS.Scheduler.CommandLine;
using DMS.Scheduler.Configuration;
using DMS.Scheduler.ConsoleUi;
using System.Text;

namespace DMS.Scheduler;

internal static class Program
{
    public static async Task<int> Main(
        string[] args)
    {
        // SCHEDULER_STARTUP_FALLBACK_V3
        try
        {

        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        var settingsRepository =
            new SchedulerSettingsRepository();

        if (args.Length > 0)
        {
            return await SchedulerCli.ExecuteAsync(
                args,
                settingsRepository);
        }

        var app =
            new SchedulerConsoleApp(
                settingsRepository);

        return await app.RunAsync();
    
        }
        catch (Exception ex)
        {
            string? diagnosticPath = null;
            try
            {
                var local = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(local)) local = System.AppContext.BaseDirectory;
                var folder = System.IO.Path.Combine(local, "DMS", "Scheduler");
                System.IO.Directory.CreateDirectory(folder);
                diagnosticPath = System.IO.Path.Combine(folder, "startup-errors.log");
                System.IO.File.AppendAllText(diagnosticPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] SCHEDULER_STARTUP_FAILED{System.Environment.NewLine}{ex}{System.Environment.NewLine}");
            }
            catch { /* Never mask the original exception. */ }
            try
            {
                Console.Error.WriteLine("DMS Scheduler startup failed:");
                Console.Error.WriteLine(ex);
                Console.Error.WriteLine(diagnosticPath ?? "Local diagnostic log unavailable.");
                if (args.Length == 0 && !Console.IsInputRedirected)
                {
                    Console.Error.WriteLine("Press Enter to exit...");
                    Console.ReadLine();
                }
            }
            catch { }
            return 1;
        }
    }
}
