using DMS.Core.Sap;
using DMS.Core.Scheduling;
using DMS.Integration.Mes.Database;
using DMS.Scheduler.Configuration;
using DMS.Scheduler.Reports;
using DMS.Core.Sap;

namespace DMS.Scheduler.Runtime;

public sealed class SchedulerRuntime
{
    public required ScheduledJobRepository JobRepository { get; init; }
    public required ScheduledReportTemplateRepository TemplateRepository { get; init; }
    public required ScheduledJobRunner Runner { get; init; }
    public required SchedulerEngine Engine { get; init; }
    public required SchedulerTextLogger Logger { get; init; }
}

public static class SchedulerRuntimeFactory
{
    public static SchedulerRuntime Create(
        SchedulerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Normalize();

        Directory.CreateDirectory(
            settings.SchedulerRoot);

        Directory.CreateDirectory(
            settings.OutputPath);

        var logger =
            new SchedulerTextLogger(
                settings.LogPath);

        var mesSettings =
            new MesDatabaseSettingsService()
                .Load(
                    settings.MesDatabaseSettingsPath);

        if (!mesSettings.IsEnabled)
        {
            throw new InvalidOperationException(
                "MES database connection is disabled in mes-database-settings.");
        }

        var jobRepository =
            new ScheduledJobRepository(
                settings.JobsPath);

        var templateRepository =
            new ScheduledReportTemplateRepository(
                settings.TemplatesPath);

        var sapStoragePaths =
            new SapStoragePaths(
                settings.DataRoot);

        var reports =
            new IDmsScheduledReport[]
            {
                new Mes06ScheduledReport(
                    mesSettings,
                    sapStoragePaths.SapMaterialsFilePath)
            };

        // IMPORTANT:
        // All delivery providers must be created by one factory.
        // Do not replace this with a local ServerFolder-only array.
        var deliverySettingsPath =
            Path.Combine(
                settings.SchedulerRoot,
                "scheduler-delivery-settings.json");

        var deliveries =
            SchedulerDeliveryFactory.Create(
                logger.Write,
                deliverySettingsPath);

        logger.Write(
            $"DELIVERY_SETTINGS Path={deliverySettingsPath}");

        logger.Write(
            "DELIVERY_REGISTRY "
            + string.Join(
                ",",
                deliveries.Select(
                    x => x.Type)));

        var runner =
            new ScheduledJobRunner(
                reports,
                deliveries,
                templateRepository,
                logger.Write);

        var engine =
            new SchedulerEngine(
                jobRepository,
                runner,
                logger.Write);



        return new SchedulerRuntime
        {
            JobRepository = jobRepository,
            TemplateRepository = templateRepository,
            Runner = runner,
            Engine = engine,
            Logger = logger
        };
    }
}

public sealed class SchedulerTextLogger
{
    private readonly string _path;
    private readonly object _sync = new();

    public SchedulerTextLogger(
        string path)
    {
        _path =
            path
            ?? throw new ArgumentNullException(
                nameof(path));
    }

    public void Write(
        string message)
    {
        var line =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";

        Console.WriteLine(
            line);

        lock (_sync)
        {
            var directory =
                Path.GetDirectoryName(
                    _path);

            if (!string.IsNullOrWhiteSpace(
                    directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            // Allow background scheduler, Run-now and log viewers to coexist.
            using var stream =
                new FileStream(
                    _path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite);

            using var writer =
                new StreamWriter(
                    stream);

            writer.WriteLine(
                line);
        }
    }
}