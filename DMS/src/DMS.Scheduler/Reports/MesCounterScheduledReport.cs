using ClosedXML.Excel;
using DMS.Core.Scheduling;
using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Reporting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Scheduler.Reports;

public sealed class MesCounterScheduledReport : IDmsScheduledReport
{
    public const string ReportCode = "MES_COUNTER";

    private readonly MesDatabaseConnectionSettings _settings;

    public MesCounterScheduledReport(
        MesDatabaseConnectionSettings settings)
    {
        _settings =
            settings
            ?? throw new ArgumentNullException(nameof(settings));

        _settings.Normalize();
    }

    public string Code => ReportCode;

    public async Task<GeneratedReport> GenerateAsync(
        ReportGenerationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var template =
            context.Template
            ?? throw new InvalidOperationException(
                "MES_COUNTER v6 requires a report template.");

        var service =
            new MesReportingEnrichmentService(
                _settings);

        var range =
            await ResolvePeriodAsync(
                service,
                template,
                context.ScheduledAtLocal,
                cancellationToken);

        var workcenters =
            template.SelectAllWorkcenters
                ? Array.Empty<string>()
                : template.WorkcenterCodes
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

        var rows =
            await service.GetCounterReportAsync(
                range.From,
                range.To,
                workcenters,
                template.Order,
                template.Operation,
                template.Article,
                _settings.DefaultReportMaxRows,
                cancellationToken);

        var content =
            BuildWorkbook(
                rows,
                range.From,
                range.To,
                template);

        return new GeneratedReport
        {
            ReportCode = ReportCode,
            Extension = "xlsx",
            ContentType =
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Content = content,
            RowCount = rows.Count
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
                fromDate = today;
                toDate = today.AddDays(1);
                break;

            case "YESTERDAY":
                fromDate = today.AddDays(-1);
                toDate = today;
                break;

            case "LAST7":
                fromDate = today.AddDays(-6);
                toDate = today.AddDays(1);
                break;

            case "THISWEEK":
                var offset =
                    ((int)today.DayOfWeek + 6) % 7;

                var monday =
                    today.AddDays(-offset);

                fromDate = monday;
                toDate = monday.AddDays(7);
                break;

            default:
                fromDate =
                    (template.From ?? today).Date;

                toDate =
                    (template.To ?? fromDate.AddDays(1)).Date;

                if (toDate <= fromDate)
                    toDate = fromDate.AddDays(1);
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

        value = parsed;
        return true;
    }

    private static byte[] BuildWorkbook(
        IReadOnlyList<Mes06CounterReportRecord> rows,
        DateTime from,
        DateTime to,
        ScheduledReportTemplate template)
    {
        using var workbook =
            new XLWorkbook();

        var detail =
            workbook.Worksheets.Add(
                "Counter report");

        var headers =
            new[]
            {
                "Shift",
                "Time",
                "Workcenter",
                "Order",
                "Operation",
                "Article",
                "SAP number",
                "Order quantity",
                "Counter",
                "Kind",
                "Value",
                "User text",
                "Operators"
            };

        for (var c = 0; c < headers.Length; c++)
            detail.Cell(1, c + 1).Value = headers[c];

        for (var i = 0; i < rows.Count; i++)
        {
            var item = rows[i];
            var r = i + 2;

            detail.Cell(r, 1).Value = item.ShiftName;
            detail.Cell(r, 2).Value = item.Timestamp;
            detail.Cell(r, 3).Value = item.WorkcenterCode;
            detail.Cell(r, 4).Value = item.OrderCode;
            detail.Cell(r, 5).Value = item.OperationCode;
            detail.Cell(r, 6).Value = item.ProductCode;
            detail.Cell(r, 7).Value = item.SapArticleNumber;

            if (item.OrderQuantity.HasValue)
                detail.Cell(r, 8).Value = item.OrderQuantity.Value;

            detail.Cell(r, 9).Value = item.CounterName;
            detail.Cell(r, 10).Value = item.CounterKind;

            if (item.Value.HasValue)
                detail.Cell(r, 11).Value = item.Value.Value;

            detail.Cell(r, 12).Value = item.CustomText;
            detail.Cell(r, 13).Value = item.WorkersDisplay;
        }

        detail.Column(2).Style.DateFormat.Format =
            "dd.MM.yyyy HH:mm:ss";

        if (detail.RangeUsed() is { } used)
        {
            used.CreateTable();
            detail.SheetView.FreezeRows(1);
        }

        detail.ColumnsUsed().AdjustToContents();

        var info =
            workbook.Worksheets.Add(
                "Info");

        info.Cell("A1").Value = "Template";
        info.Cell("B1").Value = template.Name;

        info.Cell("A2").Value = "Source";
        info.Cell("B2").Value = template.Source;

        info.Cell("A3").Value = "Report code";
        info.Cell("B3").Value = template.ReportCode;

        info.Cell("A4").Value = "From";
        info.Cell("B4").Value = from;

        info.Cell("A5").Value = "To";
        info.Cell("B5").Value = to;

        info.Cell("A6").Value = "Workcenters";
        info.Cell("B6").Value =
            template.SelectAllWorkcenters
                ? "ALL"
                : string.Join(
                    "; ",
                    template.WorkcenterCodes);

        info.ColumnsUsed().AdjustToContents();

        using var stream =
            new MemoryStream();

        workbook.SaveAs(stream);

        return stream.ToArray();
    }
}
