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
        SchedulerSettings settings,
        SchedulerTextLogger? existingLogger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Normalize();

        // SCHEDULER_STORAGE_FALLBACK_V101: logger is ready BEFORE touching the share.
        var logger = existingLogger ??
            new SchedulerTextLogger(settings.LogPath, settings.LocalFallbackLogPath);

        SchedulerStorageRecovery.VerifyRequiredFiles(settings);

        Directory.CreateDirectory(settings.SchedulerRoot);
        Directory.CreateDirectory(settings.OutputPath);

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
    private readonly string _primaryPath;
    private readonly string _localPath;
    private readonly object _sync = new();
    private bool _primaryUnavailable;

    public SchedulerTextLogger(string primaryPath, string? localPath = null)
    {
        _primaryPath = primaryPath ?? throw new ArgumentNullException(nameof(primaryPath));
        _localPath = string.IsNullOrWhiteSpace(localPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DMS", "Scheduler", "scheduler-fallback.log")
            : localPath;
    }

    public void Write(string message)
    {
        // SCHEDULER_STORAGE_FALLBACK_V101: best-effort dual write.
        // Failure of a network log must NEVER abort a scheduled report.
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        try { Console.WriteLine(line); } catch { /* Console can be redirected/closed. */ }

        lock (_sync)
        {
            if (!TryAppend(_localPath, line, out var localError))
                SafeConsoleError($"LOCAL_LOG_FAILED Path={_localPath}; Error={localError}");

            if (!TryAppend(_primaryPath, line, out var primaryError))
            {
                if (!_primaryUnavailable)
                {
                    _primaryUnavailable = true;
                    var notice = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} " +
                        $"STORAGE_LOG_FALLBACK Primary={_primaryPath}; Local={_localPath}; Error={primaryError}";
                    SafeConsoleError(notice);
                    TryAppend(_localPath, notice, out _);
                }
            }
            else if (_primaryUnavailable)
            {
                _primaryUnavailable = false;
                var notice = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} STORAGE_LOG_RESTORED Primary={_primaryPath}";
                SafeConsoleError(notice);
                TryAppend(_localPath, notice, out _);
                TryAppend(_primaryPath, notice, out _);
            }
        }
    }

    private static bool TryAppend(string path, string line, out string? error)
    {
        error = null;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(line);
            return true;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static void SafeConsoleError(string text)
    {
        try { Console.Error.WriteLine(text); } catch { /* Do not hide original errors. */ }
    }
}