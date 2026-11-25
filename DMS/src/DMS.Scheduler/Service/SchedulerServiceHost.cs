using DMS.Scheduler.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Threading.Tasks;

namespace DMS.Scheduler.Service;

/// <summary>
/// Windows Service entry point, separate from the interactive scheduler.
/// The service loads an explicit machine-level configuration, never the
/// LocalApplicationData settings of an RDP user.
/// </summary>
public static class SchedulerServiceHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DMS Scheduler service-host requires Windows.");

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "DMS", "Scheduler", "scheduler-settings.json");

        if (args.Length != 0)
        {
            if (args.Length != 2 ||
                !string.Equals(args[0], "--settings", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(args[1]))
                throw new ArgumentException("Usage: DMS.Scheduler.exe service-host [--settings <absolute-json-path>]");

            settingsPath = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(args[1]));
        }

        // Fail closed. LoadAsync() creates defaults for interactive users,
        // but a service must never silently create a Z: based configuration.
        if (!File.Exists(settingsPath))
            throw new FileNotFoundException(
                "Service configuration is missing. IT must provide scheduler-settings.json " +
                "with an accessible UNC DataRoot before starting the service.", settingsPath);

        var repository = new SchedulerSettingsRepository(settingsPath);
        var settings = await repository.LoadAsync();
        settings.Normalize();
        if (string.IsNullOrWhiteSpace(settings.DataRoot))
            throw new InvalidOperationException("Service DataRoot must not be empty.");
        if (string.IsNullOrWhiteSpace(settings.ServiceName))
            throw new InvalidOperationException("ServiceName must not be empty.");

        // The existing SchedulerWorker calls SchedulerStorageRecovery.RunAsync,
        // which pauses during network outages without using stale job definitions.
        // WindowsServiceLifetime handles service stop requests gracefully.
        using var host = Host.CreateDefaultBuilder()
            .UseWindowsService(options => options.ServiceName = settings.ServiceName)
            .ConfigureServices(services =>
            {
                services.AddSingleton(repository);
                services.AddHostedService<SchedulerWorker>();
            })
            .Build();

        await host.RunAsync();
        return 0;
    }
}
