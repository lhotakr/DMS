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
        // SCHEDULER_STORAGE_FALLBACK_V101: retry when network storage disappears.
        // The engine is suspended until required sources are readable again.
        var settings = await _settingsRepository.LoadAsync(stoppingToken);
        await SchedulerStorageRecovery.RunAsync(settings, "SERVICE", stoppingToken);
    }
}
