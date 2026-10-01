using DMS.Core.Scheduling;
using System;

namespace DMS.Scheduler.Delivery;

internal static class DeliveryTokenRenderer
{
    public const string DefaultFileName =
        "{TemplateName}_{yyyy-MM-dd}_{HHmmss}.{Extension}";

    public static string Render(
        string? value,
        ScheduledJobDefinition job,
        ScheduledReportTemplate? template,
        GeneratedReport report,
        DateTime executionLocalTime)
    {
        var result =
            value
            ?? string.Empty;

        return result
            .Replace(
                "{JobName}",
                job.Name,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{TemplateName}",
                template?.Name
                ?? job.Name,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{ReportCode}",
                report.ReportCode,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{Extension}",
                report.Extension,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{Date}",
                executionLocalTime.ToString(
                    "yyyy-MM-dd"),
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{DateTime}",
                executionLocalTime.ToString(
                    "yyyy-MM-dd_HHmmss"),
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{yyyy-MM-dd}",
                executionLocalTime.ToString(
                    "yyyy-MM-dd"),
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{HHmmss}",
                executionLocalTime.ToString(
                    "HHmmss"),
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{yyyy}",
                executionLocalTime.ToString(
                    "yyyy"),
                StringComparison.OrdinalIgnoreCase);
    }

    public static string GetFileName(
        ScheduledJobDefinition job,
        ScheduledReportTemplate? template,
        GeneratedReport report,
        DateTime executionLocalTime)
    {
        var templateText =
            string.IsNullOrWhiteSpace(
                job.OutputFileNameTemplate)
                ? DefaultFileName
                : job.OutputFileNameTemplate;

        return Render(
            templateText,
            job,
            template,
            report,
            executionLocalTime);
    }
}
