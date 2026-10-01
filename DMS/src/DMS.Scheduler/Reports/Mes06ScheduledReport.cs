using ClosedXML.Excel;
using DMS.Core.Scheduling;
using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Reporting;
using DMS.Integration.Mes.Reporting.Automation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Scheduler.Reports;

/// <summary>
/// Single scheduler executor for all MES06 automation templates.
/// Report-specific logic lives in DMS.Integration.Mes providers.
/// </summary>
public sealed class Mes06ScheduledReport : IDmsScheduledReport
{
    public const string ExecutorCode = "MES06";

    private readonly MesDatabaseConnectionSettings _settings;

    public Mes06ScheduledReport(
        MesDatabaseConnectionSettings settings)
    {
        _settings =
            settings
            ?? throw new ArgumentNullException(nameof(settings));

        _settings.Normalize();
    }

    public string Code => ExecutorCode;

    public async Task<GeneratedReport> GenerateAsync(
        ReportGenerationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var template =
            context.Template
            ?? throw new InvalidOperationException(
                "MES06 scheduler executor requires a published report template.");

        if (!string.Equals(
                template.Source,
                "MES06",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Template '{template.Name}' belongs to source '{template.Source}', not MES06.");
        }

        var enrichment =
            new MesReportingEnrichmentService(
                _settings);

        var period =
            await ResolvePeriodAsync(
                enrichment,
                template,
                context.ScheduledAtLocal,
                cancellationToken);

        var engine =
            new Mes06AutomationReportEngine(
                _settings);

        var request =
            new Mes06AutomationRequest
            {
                ReportCode =
                    template.ReportCode,
                From =
                    period.From,
                To =
                    period.To,
                WorkcenterCodes =
                    template.SelectAllWorkcenters
                        ? Array.Empty<string>()
                        : template.WorkcenterCodes,
                ShiftCode =
                    template.ShiftCode,
                Article =
                    template.Article,
                Order =
                    template.Order,
                Operation =
                    template.Operation,
                MaxRows =
                    _settings.DefaultReportMaxRows
            };

        var result =
            await engine.GenerateAsync(
                request,
                cancellationToken);

        var workbook =
            BuildWorkbook(
                result,
                template,
                period.From,
                period.To);

        return new GeneratedReport
        {
            ReportCode =
                template.ReportCode,
            Extension =
                "xlsx",
            ContentType =
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Content =
                workbook,
            RowCount =
                result.Rows.Count
        };
    }

    private static async Task<(DateTime From, DateTime To)>
        ResolvePeriodAsync(
            MesReportingEnrichmentService service,
            ScheduledReportTemplate template,
            DateTime scheduledAtLocal,
            CancellationToken cancellationToken)
    {
        var today =
            scheduledAtLocal.Date;

        DateTime fromDate;
        DateTime toDate;

        switch (
            template.QuickPeriodCode?.Trim().ToUpperInvariant())
        {
            case "TODAY":
                fromDate =
                    today;
                toDate =
                    today.AddDays(1);
                break;

            case "YESTERDAY":
                fromDate =
                    today.AddDays(-1);
                toDate =
                    today;
                break;

            case "LAST7":
                fromDate =
                    today.AddDays(-6);
                toDate =
                    today.AddDays(1);
                break;

            case "THISWEEK":
                var offset =
                    ((int)today.DayOfWeek + 6) % 7;

                fromDate =
                    today.AddDays(-offset);

                toDate =
                    fromDate.AddDays(7);
                break;

            default:
                fromDate =
                    (template.From ?? today).Date;

                toDate =
                    (template.To ?? fromDate.AddDays(1)).Date;

                if (toDate <= fromDate)
                    toDate =
                        fromDate.AddDays(1);
                break;
        }

        var hasFromTime =
            TryParseTime(
                template.TimeFrom,
                out var fromTime);

        var hasToTime =
            TryParseTime(
                template.TimeTo,
                out var toTime);

        if (hasFromTime != hasToTime)
        {
            throw new InvalidOperationException(
                "Automation template must define both TimeFrom and TimeTo, or neither.");
        }

        if (hasFromTime &&
            fromTime.HasValue &&
            toTime.HasValue)
        {
            var from =
                fromDate.Add(
                    fromTime.Value);

            var to =
                toDate.Add(
                    toTime.Value);

            if (to <= from)
            {
                throw new InvalidOperationException(
                    "Automation template To date/time must be later than From.");
            }

            return (
                from,
                to);
        }

        var shiftPeriod =
            await service.GetShiftRelatedPeriodAsync(
                fromDate,
                toDate,
                cancellationToken);

        return (
            shiftPeriod.From,
            shiftPeriod.To);
    }

    private static bool TryParseTime(
        string? text,
        out TimeSpan? value)
    {
        value = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (!TimeSpan.TryParseExact(
                text.Trim(),
                "hh\\:mm",
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new InvalidOperationException(
                $"Invalid automation template time '{text}'. Expected HH:mm.");
        }

        value =
            parsed;

        return true;
    }

    private static byte[] BuildWorkbook(
        Mes06AutomationResult report,
        ScheduledReportTemplate template,
        DateTime from,
        DateTime to)
    {
        using var workbook =
            new XLWorkbook();

        var sheet =
            workbook.Worksheets.Add(
                SafeSheetName(
                    string.IsNullOrWhiteSpace(template.Name)
                        ? template.ReportCode
                        : template.Name));

        for (var columnIndex = 0;
             columnIndex < report.Columns.Count;
             columnIndex++)
        {
            sheet.Cell(
                    1,
                    columnIndex + 1)
                .Value =
                report.Columns[columnIndex].Header;
        }

        for (var rowIndex = 0;
             rowIndex < report.Rows.Count;
             rowIndex++)
        {
            var row =
                report.Rows[rowIndex];

            for (var columnIndex = 0;
                 columnIndex < report.Columns.Count;
                 columnIndex++)
            {
                var column =
                    report.Columns[columnIndex];

                row.TryGetValue(
                    column.Key,
                    out var value);

                SetCellValue(
                    sheet.Cell(
                        rowIndex + 2,
                        columnIndex + 1),
                    value);
            }
        }

        if (sheet.RangeUsed() is { } used)
        {
            used.CreateTable();
            sheet.SheetView.FreezeRows(1);
        }

        sheet.ColumnsUsed()
            .AdjustToContents();

        foreach (var column in sheet.ColumnsUsed())
        {
            if (column.Width > 55d)
                column.Width = 55d;
        }

        var info =
            workbook.Worksheets.Add(
                "Info");

        info.Cell("A1").Value =
            "Template";
        info.Cell("B1").Value =
            template.Name;

        info.Cell("A2").Value =
            "Source";
        info.Cell("B2").Value =
            template.Source;

        info.Cell("A3").Value =
            "Report";
        info.Cell("B3").Value =
            template.ReportCode;

        info.Cell("A4").Value =
            "From";
        info.Cell("B4").Value =
            from;

        info.Cell("A5").Value =
            "To";
        info.Cell("B5").Value =
            to;

        info.Cell("A6").Value =
            "Work centers";
        info.Cell("B6").Value =
            template.SelectAllWorkcenters
                ? "ALL"
                : string.Join(
                    "; ",
                    template.WorkcenterCodes);

        info.Cell("A7").Value =
            "Shift";
        info.Cell("B7").Value =
            template.ShiftCode;

        info.Cell("A8").Value =
            "Order";
        info.Cell("B8").Value =
            template.Order;

        info.Cell("A9").Value =
            "Article";
        info.Cell("B9").Value =
            template.Article;

        info.Cell("A10").Value =
            "Operation";
        info.Cell("B10").Value =
            template.Operation;

        info.ColumnsUsed()
            .AdjustToContents();

        using var stream =
            new MemoryStream();

        workbook.SaveAs(
            stream);

        return stream.ToArray();
    }

    private static void SetCellValue(
        IXLCell cell,
        object? value)
    {
        switch (value)
        {
            case null:
                cell.Value =
                    string.Empty;
                break;

            case DateTime dateTime:
                cell.Value =
                    dateTime;
                cell.Style.DateFormat.Format =
                    "dd.MM.yyyy HH:mm:ss";
                break;

            case DateTimeOffset dateTimeOffset:
                cell.Value =
                    dateTimeOffset.LocalDateTime;
                cell.Style.DateFormat.Format =
                    "dd.MM.yyyy HH:mm:ss";
                break;

            case decimal decimalValue:
                cell.Value =
                    decimalValue;
                break;

            case double doubleValue:
                cell.Value =
                    doubleValue;
                break;

            case float floatValue:
                cell.Value =
                    floatValue;
                break;

            case int intValue:
                cell.Value =
                    intValue;
                break;

            case long longValue:
                cell.Value =
                    longValue;
                break;

            case bool boolValue:
                cell.Value =
                    boolValue;
                break;

            default:
                cell.Value =
                    Convert.ToString(
                        value,
                        CultureInfo.CurrentCulture)
                    ?? string.Empty;
                break;
        }
    }

    private static string SafeSheetName(
        string name)
    {
        var result =
            string.IsNullOrWhiteSpace(name)
                ? "Report"
                : name.Trim();

        foreach (var invalid in new[]
                 {
                     ':',
                     '\\',
                     '/',
                     '?',
                     '*',
                     '[',
                     ']'
                 })
        {
            result =
                result.Replace(
                    invalid,
                    '_');
        }

        return result.Length <= 31
            ? result
            : result[..31];
    }
}
