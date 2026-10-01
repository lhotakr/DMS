using DMS.Integration.Mes.Database;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Reporting.Automation;

public sealed class Mes06CounterAutomationProvider
    : IMes06AutomationReportProvider
{
    private readonly MesReportingEnrichmentService _service;

    public Mes06CounterAutomationProvider(
        MesDatabaseConnectionSettings settings)
    {
        _service =
            new MesReportingEnrichmentService(
                settings);
    }

    public bool CanHandle(string reportCode) =>
        string.Equals(reportCode, "PRODUCTION", StringComparison.OrdinalIgnoreCase)
        || string.Equals(reportCode, "MES_COUNTER", StringComparison.OrdinalIgnoreCase)
        || string.Equals(reportCode, "COUNTER_REPORT", StringComparison.OrdinalIgnoreCase);

    public async Task<Mes06AutomationResult> GenerateAsync(
        Mes06AutomationRequest request,
        CancellationToken cancellationToken = default)
    {
        var rows =
            await _service.GetCounterReportAsync(
                request.From,
                request.To,
                request.WorkcenterCodes,
                request.Order,
                request.Operation,
                request.Article,
                request.MaxRows,
                cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.ShiftCode))
        {
            rows =
                rows
                    .Where(row =>
                        string.Equals(
                            row.ShiftName,
                            request.ShiftCode,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();
        }

        return Mes06AutomationProjection.FromObjects(
            request.ReportCode,
            rows);
    }
}
