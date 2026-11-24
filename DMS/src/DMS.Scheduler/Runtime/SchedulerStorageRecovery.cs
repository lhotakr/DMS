using DMS.Scheduler.Configuration;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Scheduler.Runtime;

/// <summary>
/// Runs only against the configured, readable shared data source. On storage loss,
/// the scheduler pauses and retries; it never silently substitutes stale jobs.
/// </summary>
public static class SchedulerStorageRecovery
{
    public static void VerifyRequiredFiles(SchedulerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!Directory.Exists(settings.DataRoot))
        {
            throw new IOException(
                $"DMS data root is unavailable: {settings.DataRoot}. " +
                "Mapped drives (for example Z:) are often invisible to Windows services; " +
                "configure a UNC path and grant access to the service account.");
        }

        foreach (var (label, path) in new[]
        {
            ("MESSET", settings.MesDatabaseSettingsPath),
            ("JOBS", settings.JobsPath),
            ("TEMPLATES", settings.TemplatesPath)
        })
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Required scheduler source {label} is not accessible: {path}. " +
                    "Jobs are paused; cached configuration is never executed.", path);
            }

            // File.Exists can report true just before a share disappears.
            // Probing actual readability protects against silent empty job sets.
            using var probe = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
    }

    public static async Task RunAsync(
        SchedulerSettings settings,
        string mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();

        var logger = new SchedulerTextLogger(settings.LogPath, settings.LocalFallbackLogPath);
        logger.Write($"SCHEDULER_START Mode={mode}; Environment={settings.EnvironmentName}; " +
                     $"DataRoot={settings.DataRoot}; FallbackLog={settings.LocalFallbackLogPath}");

        var unavailable = false;
        var hasRun = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var runtime = SchedulerRuntimeFactory.Create(settings, logger);

                    if (unavailable)
                    {
                        logger.Write($"STORAGE_RESTORED DataRoot={settings.DataRoot}; Mode={mode}");
                        unavailable = false;

                        if (hasRun)
                        {
                            // SchedulerEngine deduplication is instance-local. After an
                            // interrupted run, avoid re-running the same cron minute.
                            // Missed minutes are NOT replayed automatically.
                            var now = DateTime.UtcNow;
                            var nextMinute = new DateTime(now.Year, now.Month, now.Day,
                                now.Hour, now.Minute, 0, DateTimeKind.Utc).AddMinutes(1);
                            await Task.Delay(nextMinute - now + TimeSpan.FromMilliseconds(200),
                                cancellationToken);
                        }
                    }

                    hasRun = true;
                    logger.Write($"SCHEDULER_ACTIVE Mode={mode}; DataRoot={settings.DataRoot}");
                    await runtime.Engine.RunLoopAsync(
                        TimeSpan.FromSeconds(settings.PollingSeconds), cancellationToken);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (IsStorageFailure(ex))
                {
                    if (!unavailable)
                    {
                        logger.Write($"STORAGE_UNAVAILABLE Mode={mode}; DataRoot={settings.DataRoot}; " +
                                     $"ErrorType={ex.GetType().Name}; Error={ex.Message}");
                        logger.Write("SCHEDULER_PAUSED Reason=StorageUnavailable; " +
                                     "No cached jobs or overdue runs will be executed.");
                        unavailable = true;
                    }

                    await Task.Delay(
                        TimeSpan.FromSeconds(Math.Clamp(settings.PollingSeconds, 10, 60)),
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown while retrying unavailable storage.
        }
        catch (Exception ex)
        {
            logger.Write($"SCHEDULER_FATAL Mode={mode}; ErrorType={ex.GetType().Name}; Error={ex}");
            throw;
        }
        finally
        {
            logger.Write($"SCHEDULER_STOP Mode={mode}");
        }
    }

    private static bool IsStorageFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}
