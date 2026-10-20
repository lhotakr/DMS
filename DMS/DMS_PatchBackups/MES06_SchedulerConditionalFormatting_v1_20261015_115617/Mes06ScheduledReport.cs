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
    private readonly string _sapMaterialsFilePath;

    public Mes06ScheduledReport(
        MesDatabaseConnectionSettings settings,
        string sapMaterialsFilePath)
    {
        _settings =
            settings;

        _sapMaterialsFilePath =
            sapMaterialsFilePath
            ?? string.Empty;
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
                _settings,
                _sapMaterialsFilePath);

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

        var columns =
            ResolveWorkbookColumns(
                report,
                template);

        for (var columnIndex = 0;
             columnIndex < columns.Count;
             columnIndex++)
        {
            sheet.Cell(
                    1,
                    columnIndex + 1)
                .Value =
                columns[columnIndex].Header;
        }

        for (var rowIndex = 0;
             rowIndex < report.Rows.Count;
             rowIndex++)
        {
            var row =
                report.Rows[rowIndex];

            for (var columnIndex = 0;
                 columnIndex < columns.Count;
                 columnIndex++)
            {
                var column =
                    columns[columnIndex];

                row.TryGetValue(
                    column.Source.Key,
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
            // MES06 scheduler: duplicate display headers are allowed.
            // CreateTable() requires unique field names and fails for valid
            // user-facing captions such as custom MES06 column names.
            used.SetAutoFilter();

            var header =
                sheet.Range(
                    1,
                    1,
                    1,
                    columns.Count);

            header.Style.Font.Bold =
                true;

            sheet.SheetView.FreezeRows(1);
        }

        sheet.ColumnsUsed()
            .AdjustToContents();

        ApplyScheduledColumnLayout(
            sheet,
            columns);

        ApplyPlachtaScheduledPresentation(
            sheet,
            report,
            template,
            columns,
            from,
            to);

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

    private sealed class ScheduledWorkbookColumn
    {
        public required Mes06AutomationColumn Source { get; init; }
        public required string Header { get; init; }
        public ScheduledReportColumnLayout? Layout { get; init; }
        public int SourceIndex { get; init; }
    }

    private static IReadOnlyList<ScheduledWorkbookColumn> ResolveWorkbookColumns(
        Mes06AutomationResult report,
        ScheduledReportTemplate template)
    {
        var layoutByKey =
            (template.Columns
             ?? new List<ScheduledReportColumnLayout>())
            .Where(column =>
                !string.IsNullOrWhiteSpace(
                    column.Key))
            .GroupBy(
                column =>
                    column.Key,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group =>
                    group.Key,
                group =>
                    group.Last(),
                StringComparer.OrdinalIgnoreCase);

        var projected =
            report.Columns
                .Select(
                    (column, sourceIndex) =>
                    {
                        layoutByKey.TryGetValue(
                            column.Key,
                            out var layout);

                        return new ScheduledWorkbookColumn
                        {
                            Source =
                                column,
                            SourceIndex =
                                sourceIndex,
                            Layout =
                                layout,
                            Header =
                                !string.IsNullOrWhiteSpace(
                                    layout?.Header)
                                    ? layout!.Header.Trim()
                                    : column.Header
                        };
                    });

        if (layoutByKey.Count == 0)
        {
            return projected.ToList();
        }

        return projected
            .Where(column =>
                column.Layout?.Visible
                ?? true)
            .OrderBy(column =>
                column.Layout is null
                    ? 1
                    : 0)
            .ThenBy(column =>
                column.Layout?.Order
                ?? column.SourceIndex)
            .ThenBy(column =>
                column.SourceIndex)
            .ToList();
    }

    private static void ApplyScheduledColumnLayout(
        IXLWorksheet sheet,
        IReadOnlyList<ScheduledWorkbookColumn> columns)
    {
        for (var index = 0;
             index < columns.Count;
             index++)
        {
            var excelColumn =
                sheet.Column(
                    index + 1);

            var layout =
                columns[index].Layout;

            if (layout is null)
            {
                if (excelColumn.Width > 55d)
                {
                    excelColumn.Width =
                        55d;
                }

                continue;
            }

            if (layout.Width > 0d)
            {
                // WPF layout width is approximately in device-independent
                // pixels; ClosedXML uses character widths.
                excelColumn.Width =
                    Math.Clamp(
                        layout.Width / 7d,
                        6d,
                        55d);
            }
            else if (excelColumn.Width > 55d)
            {
                excelColumn.Width =
                    55d;
            }

            excelColumn.Style.Font.Bold =
                layout.Bold;

            excelColumn.Style.Font.Underline =
                layout.Underline
                    ? XLFontUnderlineValues.Single
                    : XLFontUnderlineValues.None;

            excelColumn.Style.Alignment.Horizontal =
                layout.Alignment?.Trim()
                    .ToUpperInvariant() switch
                {
                    "CENTER" =>
                        XLAlignmentHorizontalValues.Center,
                    "RIGHT" =>
                        XLAlignmentHorizontalValues.Right,
                    _ =>
                        XLAlignmentHorizontalValues.Left
                };

            if (layout.DecimalPlaces.HasValue)
            {
                var decimals =
                    Math.Clamp(
                        layout.DecimalPlaces.Value,
                        0,
                        6);

                excelColumn.Style.NumberFormat.Format =
                    decimals == 0
                        ? "0"
                        : "0."
                          + new string(
                              '0',
                              decimals);
            }
        }
    }
    private static void ApplyPlachtaScheduledPresentation(
        IXLWorksheet sheet,
        Mes06AutomationResult report,
        ScheduledReportTemplate template,
        IReadOnlyList<ScheduledWorkbookColumn> columns,
        DateTime from,
        DateTime to)
    {
        if (!string.Equals(
                report.ReportCode,
                "PLACHTA",
                StringComparison.OrdinalIgnoreCase)
            || columns.Count == 0)
        {
            return;
        }

        var lastRow =
            report.Rows.Count
            + 1;

        var lastColumn =
            columns.Count;

        var header =
            sheet.Range(
                1,
                1,
                1,
                lastColumn);

        // Strong, readable header similar to the interactive MES06 export.
        header.Style.Fill.BackgroundColor =
            XLColor.FromHtml(
                "#3F4650");

        header.Style.Font.FontColor =
            XLColor.White;

        header.Style.Font.Bold =
            true;

        header.Style.Font.FontSize =
            10d;

        header.Style.Alignment.Horizontal =
            XLAlignmentHorizontalValues.Center;

        header.Style.Alignment.Vertical =
            XLAlignmentVerticalValues.Center;

        header.Style.Alignment.WrapText =
            true;

        header.Style.Border.BottomBorder =
            XLBorderStyleValues.Thin;

        header.Style.Border.BottomBorderColor =
            XLColor.FromHtml(
                "#7A838D");

        if (lastRow >= 2)
        {
            var data =
                sheet.Range(
                    2,
                    1,
                    lastRow,
                    lastColumn);

            data.Style.Font.FontSize =
                10d;

            data.Style.Alignment.Vertical =
                XLAlignmentVerticalValues.Center;
        }

        // Light vertical separators; useful both on screen and when printed.
        for (var columnNumber = 1;
             columnNumber < lastColumn;
             columnNumber++)
        {
            var separator =
                sheet.Range(
                    1,
                    columnNumber,
                    lastRow,
                    columnNumber);

            separator.Style.Border.RightBorder =
                XLBorderStyleValues.Thin;

            separator.Style.Border.RightBorderColor =
                XLColor.FromHtml(
                    "#CDD2D8");
        }

        // Match the interactive Plachta: alternate shading by work center
        // (not every second row) and a top border when a new machine begins.
        var workcenterColumn =
            FindScheduledColumnIndex(
                columns,
                "WorkcenterCode");

        if (workcenterColumn >= 0)
        {
            var bandByWorkcenter =
                new Dictionary<string, bool>(
                    StringComparer.CurrentCultureIgnoreCase);

            var nextBand =
                false;

            string previousWorkcenter =
                string.Empty;

            for (var rowIndex = 0;
                 rowIndex < report.Rows.Count;
                 rowIndex++)
            {
                var sourceRow =
                    report.Rows[rowIndex];

                sourceRow.TryGetValue(
                    "WorkcenterCode",
                    out var rawWorkcenter);

                var workcenter =
                    Convert.ToString(
                        rawWorkcenter,
                        CultureInfo.CurrentCulture)
                    ?.Trim()
                    ?? string.Empty;

                if (!bandByWorkcenter.TryGetValue(
                        workcenter,
                        out var shaded))
                {
                    shaded =
                        nextBand;

                    bandByWorkcenter[workcenter] =
                        shaded;

                    nextBand =
                        !nextBand;
                }

                var excelRow =
                    rowIndex
                    + 2;

                var range =
                    sheet.Range(
                        excelRow,
                        1,
                        excelRow,
                        lastColumn);

                range.Style.Fill.BackgroundColor =
                    shaded
                        ? XLColor.FromHtml(
                            "#E7E6E6")
                        : XLColor.White;

                var groupChanged =
                    !string.Equals(
                        workcenter,
                        previousWorkcenter,
                        StringComparison.CurrentCultureIgnoreCase);

                if (groupChanged)
                {
                    range.Style.Border.TopBorder =
                        XLBorderStyleValues.Thin;

                    range.Style.Border.TopBorderColor =
                        XLColor.Gray;
                }

                previousWorkcenter =
                    workcenter;
            }
        }

        // Plachta number formats. The raw service deliberately keeps numeric
        // precision; presentation decides how many decimals are useful.
        ApplyPlachtaScheduledNumberFormat(
            sheet,
            columns,
            "OrderQuantity",
            "#,##0",
            XLAlignmentHorizontalValues.Right);

        ApplyPlachtaScheduledNumberFormat(
            sheet,
            columns,
            "PlannedPerformance",
            "#,##0",
            XLAlignmentHorizontalValues.Right);

        ApplyPlachtaScheduledNumberFormat(
            sheet,
            columns,
            "PersonnelCount",
            "0",
            XLAlignmentHorizontalValues.Right);

        ApplyPlachtaScheduledNumberFormat(
            sheet,
            columns,
            "ProductionTimeHours",
            "0.00",
            XLAlignmentHorizontalValues.Right);

        ApplyPlachtaScheduledNumberFormat(
            sheet,
            columns,
            "DowntimeHours",
            "0.00",
            XLAlignmentHorizontalValues.Right);

        ApplyPlachtaScheduledNumberFormat(
            sheet,
            columns,
            "PrintedGross",
            "#,##0",
            XLAlignmentHorizontalValues.Right);

        ApplyPlachtaScheduledNumberFormat(
            sheet,
            columns,
            "PrintedNet",
            "#,##0",
            XLAlignmentHorizontalValues.Right);

        ApplyPlachtaScheduledNumberFormat(
            sheet,
            columns,
            "TotalScrap",
            "#,##0",
            XLAlignmentHorizontalValues.Right);

        ApplyPlachtaScheduledNumberFormat(
            sheet,
            columns,
            "ScrapPercent",
            "0.00",
            XLAlignmentHorizontalValues.Right);

        ApplyPlachtaScheduledAlignment(
            sheet,
            columns,
            "ShiftCode",
            XLAlignmentHorizontalValues.Center);

        ApplyPlachtaScheduledAlignment(
            sheet,
            columns,
            "SetupMark",
            XLAlignmentHorizontalValues.Center);

        ApplyPlachtaScheduledAlignment(
            sheet,
            columns,
            "ReplacementMachineMark",
            XLAlignmentHorizontalValues.Center);

        // Long article description / notes remain readable without making all
        // other columns unnecessarily wide.
        ApplyPlachtaScheduledWrap(
            sheet,
            columns,
            "ProductDescription");

        ApplyPlachtaScheduledWrap(
            sheet,
            columns,
            "Notes");

        // Excel print layout mirrors the interactive MES06 report:
        // landscape, one page wide, unlimited pages tall and repeated header.
        var pageSetup =
            sheet.PageSetup;

        pageSetup.PageOrientation =
            XLPageOrientation.Landscape;

        pageSetup.FitToPages(
            1,
            0);

        pageSetup.SetRowsToRepeatAtTop(
            1,
            1);

        pageSetup.PrintAreas.Clear();

        pageSetup.PrintAreas.Add(
            1,
            1,
            lastRow,
            lastColumn);

        pageSetup.Margins.Left =
            0.25d;

        pageSetup.Margins.Right =
            0.25d;

        pageSetup.Margins.Top =
            0.55d;

        pageSetup.Margins.Bottom =
            0.45d;

        pageSetup.Margins.Header =
            0.16d;

        pageSetup.Margins.Footer =
            0.16d;

        pageSetup.AlignHFWithMargins =
            true;

        pageSetup.ScaleHFWithDocument =
            false;

        var reportTitle =
            string.IsNullOrWhiteSpace(
                template.Name)
                ? "MES06 – Plachta"
                : template.Name;

        pageSetup.Header.Center
            .AddText(
                reportTitle,
                XLHFOccurrence.AllPages)
            .SetBold()
            .SetFontSize(
                10d);

        pageSetup.Header.Center
            .AddText(
                $"\n{from:dd.MM.yyyy HH:mm} – {to:dd.MM.yyyy HH:mm}",
                XLHFOccurrence.AllPages)
            .SetFontSize(
                7d);

        pageSetup.Footer.Right.AddText(
            XLHFPredefinedText.PageNumber,
            XLHFOccurrence.AllPages);

        pageSetup.Footer.Right.AddText(
            " / ",
            XLHFOccurrence.AllPages);

        pageSetup.Footer.Right.AddText(
            XLHFPredefinedText.NumberOfPages,
            XLHFOccurrence.AllPages);

        sheet.SheetView.FreezeRows(
            1);
    }

    private static int FindScheduledColumnIndex(
        IReadOnlyList<ScheduledWorkbookColumn> columns,
        string key)
    {
        for (var index = 0;
             index < columns.Count;
             index++)
        {
            if (string.Equals(
                    columns[index].Source.Key,
                    key,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static void ApplyPlachtaScheduledNumberFormat(
        IXLWorksheet sheet,
        IReadOnlyList<ScheduledWorkbookColumn> columns,
        string key,
        string format,
        XLAlignmentHorizontalValues alignment)
    {
        var index =
            FindScheduledColumnIndex(
                columns,
                key);

        if (index < 0)
        {
            return;
        }

        var column =
            sheet.Column(
                index + 1);

        column.Style.NumberFormat.Format =
            format;

        column.Style.Alignment.Horizontal =
            alignment;
    }

    private static void ApplyPlachtaScheduledAlignment(
        IXLWorksheet sheet,
        IReadOnlyList<ScheduledWorkbookColumn> columns,
        string key,
        XLAlignmentHorizontalValues alignment)
    {
        var index =
            FindScheduledColumnIndex(
                columns,
                key);

        if (index < 0)
        {
            return;
        }

        sheet.Column(
                index + 1)
            .Style.Alignment.Horizontal =
            alignment;
    }

    private static void ApplyPlachtaScheduledWrap(
        IXLWorksheet sheet,
        IReadOnlyList<ScheduledWorkbookColumn> columns,
        string key)
    {
        var index =
            FindScheduledColumnIndex(
                columns,
                key);

        if (index < 0)
        {
            return;
        }

        sheet.Column(
                index + 1)
            .Style.Alignment.WrapText =
            true;
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
