using DMS.Integration.Mes.Database;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Reporting.Automation;

public sealed class Mes06LoggedOperatorsAutomationProvider
    : IMes06AutomationReportProvider
{
    private readonly IMesReportingDataService _service;

    public Mes06LoggedOperatorsAutomationProvider(
        MesDatabaseConnectionSettings settings)
    {
        _service =
            new MesReportingDataService(
                settings);
    }

    public bool CanHandle(string reportCode) =>
        string.Equals(
            reportCode,
            MesLoggedOperatorsReportSupport.ReportCode,
            StringComparison.OrdinalIgnoreCase);

    public async Task<Mes06AutomationResult> GenerateAsync(
        Mes06AutomationRequest request,
        CancellationToken cancellationToken = default)
    {
        var filter =
            new MesReportFilter
            {
                From = request.From,
                To = request.To,
                OrderCode = request.Order,
                ProductCode = request.Article,
                MaxRows = request.MaxRows
            };

        var rows =
            await _service.GetLoggedOperatorsAsync(
                filter,
                request.WorkcenterCodes,
                request.ShiftCode,
                request.Operation);

        return Mes06AutomationProjection.FromObjects(
            request.ReportCode,
            rows,
            nameof(MesLoggedOperatorRecord.CostCenter),
            nameof(MesLoggedOperatorRecord.DurationText),
            nameof(MesLoggedOperatorRecord.HasOperator));
    }
}
