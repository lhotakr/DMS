using DMS.Integration.Mes.Database;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Reporting.Automation;

/// <summary>
/// JOB10 / DMS.Scheduler automation provider for the MES06 SCRAP_DETAIL report.
/// Uses the same dedicated long-range data service as the interactive MES06 view.
/// </summary>
public sealed class Mes06ScrapDetailAutomationProvider
    : IMes06AutomationReportProvider
{
    private readonly MesScrapDetailDataService _service;

    public Mes06ScrapDetailAutomationProvider(
        MesDatabaseConnectionSettings settings)
    {
        _service =
            new MesScrapDetailDataService(
                settings
                ?? throw new ArgumentNullException(
                    nameof(settings)));
    }

    public bool CanHandle(
        string reportCode) =>
        string.Equals(
            reportCode,
            "SCRAP_DETAIL",
            StringComparison.OrdinalIgnoreCase);

    public async Task<Mes06AutomationResult> GenerateAsync(
        Mes06AutomationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        cancellationToken.ThrowIfCancellationRequested();

        var rows =
            await _service.GetReportAsync(
                request.From,
                request.To,
                request.WorkcenterCodes,
                request.Order,
                request.Operation,
                request.Article,
                // Current automation request does not yet carry the interactive
                // counter selection. Empty list intentionally means ALL counters.
                Array.Empty<string>(),
                Math.Max(
                    request.MaxRows,
                    100000),
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        return Mes06AutomationProjection.FromObjects(
            request.ReportCode,
            rows);
    }
}
