using DMS.Integration.Mes.Reporting;
using DMS.Integration.Mes.Reporting.Definitions;
using DMS.Integration.Mes.Reporting.Models;
using System.Collections.Generic;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private IReadOnlyList<MesReportingShiftEvent> _mes06ShiftEvents =
        Array.Empty<MesReportingShiftEvent>();

    private IReadOnlyDictionary<string, string> _mes06SapNumbersByOrder =
        new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<MesReportingStateColor> _mes06StateColors =
        Array.Empty<MesReportingStateColor>();

    private async Task LoadProductionEnrichmentAsync(
        MesReportDefinition definition,
        MesReportFilter filter,
        IReadOnlyList<object> rows)
    {
        var requiresShiftEnrichment =
            IsProductionReport(
                definition)
            || IsStatesReport(
                definition);

        if (!requiresShiftEnrichment)
        {
            _mes06ShiftEvents =
                Array.Empty<MesReportingShiftEvent>();

            _mes06SapNumbersByOrder =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            _mes06StateColors =
                Array.Empty<MesReportingStateColor>();

            return;
        }

        var service =
            new MesReportingEnrichmentService(
                _settings);

        try
        {
            _mes06ShiftEvents =
                await service.GetShiftEventsAsync(
                    filter.From,
                    filter.To);
        }
        catch (Exception ex)
        {
            _mes06ShiftEvents =
                Array.Empty<MesReportingShiftEvent>();

            _logger.Error(
                "MES06 shift enrichment load failed.",
                ex);
        }

        if (IsStatesReport(
                definition))
        {
            _mes06SapNumbersByOrder =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            try
            {
                _mes06StateColors =
                    await service.GetStateColorsAsync();
            }
            catch (Exception ex)
            {
                _mes06StateColors =
                    Array.Empty<MesReportingStateColor>();

                _logger.Error(
                    "MES06 FASTEC state-color enrichment load failed.",
                    ex);
            }

            return;
        }

        _mes06StateColors =
            Array.Empty<MesReportingStateColor>();

        try
        {
            var orderCodes =
                rows
                    .Select(row =>
                        FirstNonEmpty(
                            ReadProperty(
                                row,
                                "OrderCode"),
                            ReadProperty(
                                row,
                                "Order")))
                    .Where(code =>
                        !string.IsNullOrWhiteSpace(
                            code))
                    .Select(code =>
                        code!)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

            _mes06SapNumbersByOrder =
                await service.GetSapNumbersByOrderAsync(
                    orderCodes);
        }
        catch (Exception ex)
        {
            _mes06SapNumbersByOrder =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            _logger.Error(
                "MES06 SAP-number enrichment load failed.",
                ex);
        }
    }

    private MesReportingShiftEvent? ResolveDatabaseShift(
        object row)
    {
        var rowStart =
            ResolveRowStart(
                row);

        if (rowStart == DateTime.MinValue)
        {
            return null;
        }

        // Important for night shift:
        // an event starting 21:55 on the previous calendar day still owns
        // rows after midnight until its configured Endtime.
        return _mes06ShiftEvents
            .Where(shift =>
                rowStart >= shift.Starttime
                && rowStart < shift.Endtime)
            .OrderByDescending(shift =>
                shift.Starttime)
            .FirstOrDefault();
    }

    private string GetSapNumber(
        object row)
    {
        var orderCode =
            FirstNonEmpty(
                ReadProperty(
                    row,
                    "OrderCode"),
                ReadProperty(
                    row,
                    "Order"));

        if (string.IsNullOrWhiteSpace(
                orderCode))
        {
            return string.Empty;
        }

        return _mes06SapNumbersByOrder.TryGetValue(
            orderCode,
            out var sapNumber)
            ? sapNumber
            : string.Empty;
    }
}
