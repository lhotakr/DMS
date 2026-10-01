using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Core.Scheduling;

public sealed class ServerFolderReportDelivery : IReportDelivery
{
    public string Type => "ServerFolder";

    public async Task<ReportDeliveryResult> DeliverAsync(
        ScheduledJobDefinition job,
        ScheduledReportTemplate? template,
        ScheduledJobDeliveryDefinition delivery,
        GeneratedReport report,
        DateTime executionLocalTime,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(delivery.TargetPath))
            return ReportDeliveryResult.Fail(
                string.Empty,
                "Server folder path is empty.");

        try
        {
            Directory.CreateDirectory(delivery.TargetPath);

            var fileName =
                BuildFileName(
                    job,
                    template,
                    report,
                    executionLocalTime);

            var path =
                Path.Combine(
                    delivery.TargetPath,
                    fileName);

            await File.WriteAllBytesAsync(
                path,
                report.Content,
                cancellationToken);

            return ReportDeliveryResult.Ok(path);
        }
        catch (Exception ex)
        {
            return ReportDeliveryResult.Fail(
                delivery.TargetPath,
                ex.Message);
        }
    }

    private static string BuildFileName(
        ScheduledJobDefinition job,
        ScheduledReportTemplate? template,
        GeneratedReport report,
        DateTime time)
    {
        var value =
            string.IsNullOrWhiteSpace(job.OutputFileNameTemplate)
                ? "{TemplateName}_{yyyy-MM-dd}_{HHmmss}.{Extension}"
                : job.OutputFileNameTemplate;

        value = value
            .Replace(
                "{TemplateName}",
                Sanitize(
                    template?.Name
                    ?? job.Name
                    ?? "REPORT"),
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{ReportCode}",
                Sanitize(report.ReportCode),
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{Extension}",
                report.Extension.TrimStart('.'),
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{yyyy-MM-dd}",
                time.ToString("yyyy-MM-dd"),
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{HHmmss}",
                time.ToString("HHmmss"),
                StringComparison.OrdinalIgnoreCase);

        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return value;
    }

    private static string Sanitize(string value)
    {
        var result =
            string.IsNullOrWhiteSpace(value)
                ? "REPORT"
                : value.Trim();

        foreach (var invalid in Path.GetInvalidFileNameChars())
            result = result.Replace(invalid, '_');

        return result;
    }
}
