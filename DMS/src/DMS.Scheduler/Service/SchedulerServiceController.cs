using DMS.Scheduler.Configuration;
using System;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace DMS.Scheduler.Service;

public sealed class SchedulerServiceController
{
    private readonly SchedulerSettings _settings;

    public SchedulerServiceController(
        SchedulerSettings settings)
    {
        _settings =
            settings
            ?? throw new ArgumentNullException(nameof(settings));
    }

    public ServiceControllerStatus? TryGetStatus()
    {
        try
        {
            using var controller =
                new ServiceController(
                    _settings.ServiceName);

            return controller.Status;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public async Task StartAsync()
    {
        using var controller =
            new ServiceController(
                _settings.ServiceName);

        if (controller.Status == ServiceControllerStatus.Running)
            return;

        controller.Start();

        await Task.Run(
            () =>
                controller.WaitForStatus(
                    ServiceControllerStatus.Running,
                    TimeSpan.FromSeconds(20)));
    }

    public async Task StopAsync()
    {
        using var controller =
            new ServiceController(
                _settings.ServiceName);

        if (controller.Status == ServiceControllerStatus.Stopped)
            return;

        if (!controller.CanStop)
        {
            throw new InvalidOperationException(
                $"Service '{_settings.ServiceName}' cannot be stopped.");
        }

        controller.Stop();

        await Task.Run(
            () =>
                controller.WaitForStatus(
                    ServiceControllerStatus.Stopped,
                    TimeSpan.FromSeconds(20)));
    }
}
