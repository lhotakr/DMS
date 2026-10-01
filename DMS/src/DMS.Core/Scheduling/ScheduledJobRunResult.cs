using System;
using System.Collections.Generic;

namespace DMS.Core.Scheduling;

public sealed class ScheduledJobRunResult
{
    public string JobId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public DateTime StartedUtc { get; init; }
    public DateTime FinishedUtc { get; init; }
    public IReadOnlyList<string> Destinations { get; init; } = Array.Empty<string>();
    public string? Error { get; init; }

    public static ScheduledJobRunResult Ok(
        string jobId,
        DateTime startedUtc,
        DateTime finishedUtc,
        IReadOnlyList<string> destinations) =>
        new()
        {
            JobId = jobId,
            Success = true,
            StartedUtc = startedUtc,
            FinishedUtc = finishedUtc,
            Destinations = destinations
        };

    public static ScheduledJobRunResult Fail(
        string jobId,
        DateTime startedUtc,
        DateTime finishedUtc,
        string error) =>
        new()
        {
            JobId = jobId,
            Success = false,
            StartedUtc = startedUtc,
            FinishedUtc = finishedUtc,
            Error = error
        };
}
