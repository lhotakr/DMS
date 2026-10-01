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
}
