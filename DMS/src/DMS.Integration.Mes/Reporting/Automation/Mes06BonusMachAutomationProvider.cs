using DMS.Integration.Mes.Database;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Reporting.Automation;

/// <summary>
/// Automation provider for MES06 bonus reports.
/// Handles both BONUS_MACH and BONUS_BASE through the same
/// MesReportingEnrichmentService.GetBonusReportAsync() logic
/// used by the interactive MES06 report.
/// </summary>
public sealed class Mes06BonusMachAutomationProvider
    : IMes06AutomationReportProvider
{
    private readonly MesReportingEnrichmentService _service;

    public Mes06BonusMachAutomationProvider(
        MesDatabaseConnectionSettings settings)
    {
        _service =
            new MesReportingEnrichmentService(
                settings
                ?? throw new ArgumentNullException(
                    nameof(settings)));
    }

    public bool CanHandle(
        string reportCode) =>
        string.Equals(
            reportCode,
            "BONUS_MACH",
            StringComparison.OrdinalIgnoreCase)
        ||
        string.Equals(
            reportCode,
            "BONUS_BASE",
            StringComparison.OrdinalIgnoreCase);

    public async Task<Mes06AutomationResult> GenerateAsync(
        Mes06AutomationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        cancellationToken.ThrowIfCancellationRequested();

        var rows =
            await _service.GetBonusReportAsync(
                request.From,
                request.To,
                request.WorkcenterCodes,
                request.ShiftCode,
                request.Order,
                request.Operation,
                request.Article,
                request.MaxRows);

        cancellationToken.ThrowIfCancellationRequested();

        return Mes06AutomationProjection.FromObjects(
            request.ReportCode,
            rows);
    }
}