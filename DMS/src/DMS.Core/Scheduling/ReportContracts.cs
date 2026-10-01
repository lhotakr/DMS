using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Core.Scheduling;

public sealed class ReportGenerationContext
{
    public required ScheduledJobDefinition Job { get; init; }
    public ScheduledReportTemplate? Template { get; init; }
    public DateTime ScheduledAtLocal { get; init; }
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class GeneratedReport
{
    public string ReportCode { get; init; } = string.Empty;
    public string Extension { get; init; } = "xlsx";
    public string ContentType { get; init; }
        = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public byte[] Content { get; init; } = Array.Empty<byte>();
    public int RowCount { get; init; }
}

public interface IDmsScheduledReport
{
    string Code { get; }

    Task<GeneratedReport> GenerateAsync(
        ReportGenerationContext context,
        CancellationToken cancellationToken = default);
}

public interface IReportDelivery
{
    string Type { get; }

    Task<ReportDeliveryResult> DeliverAsync(
        ScheduledJobDefinition job,
        ScheduledReportTemplate? template,
        ScheduledJobDeliveryDefinition delivery,
        GeneratedReport report,
        DateTime executionLocalTime,
        CancellationToken cancellationToken = default);
}

public sealed class ReportDeliveryResult
{
    public bool Success { get; init; }
    public string Destination { get; init; } = string.Empty;
    public string? Error { get; init; }

    public static ReportDeliveryResult Ok(string destination) =>
        new() { Success = true, Destination = destination };

    public static ReportDeliveryResult Fail(string destination, string error) =>
        new() { Success = false, Destination = destination, Error = error };
}
