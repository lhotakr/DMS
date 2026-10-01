using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Reporting.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Reporting.Automation;

public abstract class Mes06RawProviderBase
{
    protected readonly MesReportingDataService Service;

    protected Mes06RawProviderBase(
        MesDatabaseConnectionSettings settings)
    {
        Service =
            new MesReportingDataService(
                settings);
    }

    protected static MesReportFilter BuildFilter(
        Mes06AutomationRequest request,
        string workcenterCode = "")
    {
        return new MesReportFilter
        {
            From = request.From,
            To = request.To,
            WorkcenterCode = workcenterCode,
            OrderCode = request.Order,
            ProductCode = request.Article,
            MaxRows = request.MaxRows
        };
    }

    protected static IReadOnlyList<string> NormalizeWorkcenters(
        Mes06AutomationRequest request) =>
        request.WorkcenterCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public sealed class Mes06RawProductionAutomationProvider
    : Mes06RawProviderBase,
      IMes06AutomationReportProvider
{
    public Mes06RawProductionAutomationProvider(
        MesDatabaseConnectionSettings settings)
        : base(settings)
    {
    }

    public bool CanHandle(string reportCode) =>
        string.Equals(reportCode, "RAW_PRODUCTION", StringComparison.OrdinalIgnoreCase);

    public async Task<Mes06AutomationResult> GenerateAsync(
        Mes06AutomationRequest request,
        CancellationToken cancellationToken = default)
    {
        var workcenters =
            NormalizeWorkcenters(request);

        if (workcenters.Count == 0)
        {
            var rows =
                await Service.GetProductionAsync(
                    BuildFilter(request),
                    cancellationToken);

            return Mes06AutomationProjection.FromObjects(
                request.ReportCode,
                rows);
        }

        var all =
            new List<MesProductionRecord>();

        foreach (var workcenter in workcenters)
        {
            var rows =
                await Service.GetProductionAsync(
                    BuildFilter(request, workcenter),
                    cancellationToken);

            all.AddRange(rows);
        }

        return Mes06AutomationProjection.FromObjects(
            request.ReportCode,
            all);
    }
}

public sealed class Mes06StatesAutomationProvider
    : Mes06RawProviderBase,
      IMes06AutomationReportProvider
{
    public Mes06StatesAutomationProvider(
        MesDatabaseConnectionSettings settings)
        : base(settings)
    {
    }

    public bool CanHandle(string reportCode) =>
        string.Equals(reportCode, "STATES", StringComparison.OrdinalIgnoreCase)
        || string.Equals(reportCode, "STATE", StringComparison.OrdinalIgnoreCase)
        || string.Equals(reportCode, "DOWNTIME", StringComparison.OrdinalIgnoreCase);

    public async Task<Mes06AutomationResult> GenerateAsync(
        Mes06AutomationRequest request,
        CancellationToken cancellationToken = default)
    {
        var workcenters =
            NormalizeWorkcenters(request);

        if (workcenters.Count == 0)
        {
            var rows =
                await Service.GetStatesAsync(
                    BuildFilter(request),
                    cancellationToken);

            return Mes06AutomationProjection.FromObjects(
                request.ReportCode,
                rows);
        }

        var all =
            new List<MesStateRecord>();

        foreach (var workcenter in workcenters)
        {
            var rows =
                await Service.GetStatesAsync(
                    BuildFilter(request, workcenter),
                    cancellationToken);

            all.AddRange(rows);
        }

        return Mes06AutomationProjection.FromObjects(
            request.ReportCode,
            all);
    }
}

public sealed class Mes06CountersAutomationProvider
    : Mes06RawProviderBase,
      IMes06AutomationReportProvider
{
    public Mes06CountersAutomationProvider(
        MesDatabaseConnectionSettings settings)
        : base(settings)
    {
    }

    public bool CanHandle(string reportCode) =>
        string.Equals(reportCode, "COUNTERS", StringComparison.OrdinalIgnoreCase)
        || string.Equals(reportCode, "COUNTER", StringComparison.OrdinalIgnoreCase);

    public async Task<Mes06AutomationResult> GenerateAsync(
        Mes06AutomationRequest request,
        CancellationToken cancellationToken = default)
    {
        var workcenters =
            NormalizeWorkcenters(request);

        if (workcenters.Count == 0)
        {
            var rows =
                await Service.GetCountersAsync(
                    BuildFilter(request),
                    cancellationToken);

            return Mes06AutomationProjection.FromObjects(
                request.ReportCode,
                rows);
        }

        var all =
            new List<MesCounterRecord>();

        foreach (var workcenter in workcenters)
        {
            var rows =
                await Service.GetCountersAsync(
                    BuildFilter(request, workcenter),
                    cancellationToken);

            all.AddRange(rows);
        }

        return Mes06AutomationProjection.FromObjects(
            request.ReportCode,
            all);
    }
}
