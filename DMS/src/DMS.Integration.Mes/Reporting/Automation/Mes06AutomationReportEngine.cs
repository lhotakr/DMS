using DMS.Integration.Mes.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Reporting.Automation;

/// <summary>
/// Universal automation dispatcher for MES06 reports.
/// JOB10 and DMS.Scheduler do not need to know individual report implementations.
/// Each report-specific provider is registered here once and then resolved by ReportCode.
/// </summary>
public sealed class Mes06AutomationReportEngine
{
    private readonly IReadOnlyList<IMes06AutomationReportProvider> _providers;

    public Mes06AutomationReportEngine(
        MesDatabaseConnectionSettings settings,
        string? sapMaterialsFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _providers =
            new IMes06AutomationReportProvider[]
            {
                new Mes06PlachtaAutomationProvider(settings, sapMaterialsFilePath),
                new Mes06CounterAutomationProvider(settings),
                new Mes06LoggedOperatorsAutomationProvider(settings),
                new Mes06BonusMachAutomationProvider(settings),
                new Mes06ScrapDetailAutomationProvider(settings),
                new Mes06RawProductionAutomationProvider(settings),
                new Mes06StatesAutomationProvider(settings),
                new Mes06CountersAutomationProvider(settings)
            };
    }

    /// <summary>
    /// Alternate constructor useful for tests or future dependency injection.
    /// </summary>
    public Mes06AutomationReportEngine(
        IEnumerable<IMes06AutomationReportProvider> providers)
    {
        _providers =
            providers?.ToArray()
            ?? throw new ArgumentNullException(nameof(providers));
    }

    public async Task<Mes06AutomationResult> GenerateAsync(
        Mes06AutomationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ReportCode))
        {
            throw new InvalidOperationException(
                "MES06 automation request does not contain ReportCode.");
        }

        var provider =
            _providers.FirstOrDefault(
                x => x.CanHandle(request.ReportCode));

        if (provider is null)
        {
            throw new NotSupportedException(
                $"MES06 automation provider for report '{request.ReportCode}' is not registered.");
        }

        return await provider.GenerateAsync(
            request,
            cancellationToken);
    }
}
