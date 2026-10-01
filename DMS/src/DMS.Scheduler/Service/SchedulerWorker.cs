using DMS.Scheduler.Configuration;
using DMS.Scheduler.Runtime;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Scheduler.Service;

public sealed class SchedulerWorker
    : BackgroundService
{
    private readonly SchedulerSettingsRepository _settingsRepository;

    public SchedulerWorker(
        SchedulerSettingsRepository settingsRepository)
    {
        _settingsRepository =
            settingsRepository;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var settings =
            await _settingsRepository.LoadAsync(
                stoppingToken);

        var runtime =
            SchedulerRuntimeFactory.Create(
                settings);

        runtime.Logger.Write(
            $"SERVICE_START Environment={settings.EnvironmentName}; DataRoot={settings.DataRoot}");

        try
        {
            await runtime.Engine.RunLoopAsync(
                TimeSpan.FromSeconds(
                    settings.PollingSeconds),
                stoppingToken);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Normal service shutdown.
        }
        finally
        {
            runtime.Logger.Write(
                "SERVICE_STOP");
        }
    }
}
