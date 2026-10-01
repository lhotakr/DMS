using DMS.Core.Scheduling;
using DMS.Scheduler.Configuration;
using DMS.Scheduler.Delivery;
using System;

namespace DMS.Scheduler.Runtime;

public static class SchedulerDeliveryFactory
{
    public static IReportDelivery[] Create(
        Action<string>? log = null,
        string? settingsPath = null)
    {
        var settings =
            new SchedulerDeliverySettingsRepository(
                settingsPath)
            .Load();

        return
        [
            new ServerFolderReportDelivery(),
            new EmailReportDelivery(
                settings.Smtp,
                log),
            new SharePointReportDelivery(
                settings.SharePoint,
                log: log)
        ];
    }
}
