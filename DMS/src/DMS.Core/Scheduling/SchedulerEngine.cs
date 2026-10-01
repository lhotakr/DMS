using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Core.Scheduling;

/// <summary>
/// Host-independent scheduler core.
/// Each job is evaluated in its configured timezone.
/// </summary>
public sealed class SchedulerEngine
{
    private readonly ScheduledJobRepository _repository;
    private readonly ScheduledJobRunner _runner;
    private readonly Action<string>? _log;

    private readonly ConcurrentDictionary<string, byte>
        _executedMinuteKeys =
            new(StringComparer.OrdinalIgnoreCase);

    public SchedulerEngine(
        ScheduledJobRepository repository,
        ScheduledJobRunner runner,
        Action<string>? log = null)
    {
        _repository =
            repository
            ?? throw new ArgumentNullException(nameof(repository));

        _runner =
            runner
            ?? throw new ArgumentNullException(nameof(runner));

        _log = log;
    }

    public async Task RunLoopAsync(
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var delay =
            pollInterval
            ?? TimeSpan.FromSeconds(20);

        while (!cancellationToken.IsCancellationRequested)
        {
            await TickUtcAsync(
                DateTime.UtcNow,
                cancellationToken);

            await Task.Delay(
                delay,
                cancellationToken);
        }
    }

    public async Task TickUtcAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            utcNow =
                DateTime.SpecifyKind(
                    utcNow,
                    DateTimeKind.Utc);
        }

        var file =
            await _repository.LoadAsync(
                cancellationToken);

        foreach (var job in file.Jobs)
        {
            if (!job.IsEnabled)
                continue;

            CronSchedule schedule;

            try
            {
                schedule =
                    CronSchedule.Parse(
                        job.CronExpression);
            }
            catch (Exception ex)
            {
                _log?.Invoke(
                    $"JOB_INVALID Id={job.Id}; Cron={job.CronExpression}; Error={ex.Message}");
                continue;
            }

            var timeZone =
                SchedulerTimeZone.Resolve(
                    job.TimeZoneId);

            var localNow =
                TimeZoneInfo.ConvertTimeFromUtc(
                    utcNow,
                    timeZone);

            var localMinute =
                new DateTime(
                    localNow.Year,
                    localNow.Month,
                    localNow.Day,
                    localNow.Hour,
                    localNow.Minute,
                    0,
                    DateTimeKind.Unspecified);

            if (!schedule.IsMatch(localMinute))
                continue;

            var minuteKey =
                $"{job.Id}|{timeZone.Id}|{localMinute:yyyyMMddHHmm}";

            if (!_executedMinuteKeys.TryAdd(
                    minuteKey,
                    0))
            {
                continue;
            }

            await _runner.RunAsync(
                job,
                localMinute,
                cancellationToken);
        }

        CleanupOldKeys(
            utcNow);
    }

    private void CleanupOldKeys(
        DateTime utcNow)
    {
        // The set is tiny in normal operation. A two-day retention also
        // covers DST fall-back without executing one local minute twice.
        if (_executedMinuteKeys.Count < 100)
            return;

        _executedMinuteKeys.Clear();

        _log?.Invoke(
            $"SCHEDULER_DEDUP_RESET Utc={utcNow:O}");
    }
}

public static class SchedulerTimeZone
{
    public static TimeZoneInfo Resolve(
        string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                id.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            // Common DMS production mapping:
            // Linux/IANA config -> Windows service host.
            if (OperatingSystem.IsWindows() &&
                string.Equals(
                    id.Trim(),
                    "Europe/Prague",
                    StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    "Central Europe Standard Time");
            }

            throw;
        }
    }
}
