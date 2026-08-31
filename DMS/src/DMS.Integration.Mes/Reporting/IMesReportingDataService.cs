using DMS.Integration.Mes.Reporting.Models;

namespace DMS.Integration.Mes.Reporting;

public interface IMesReportingDataService
{
    Task<IReadOnlyList<MesWorkcenterRecord>> GetWorkcentersAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MesProductionRecord>> GetProductionAsync(
        MesReportFilter filter,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MesStateRecord>> GetStatesAsync(
        MesReportFilter filter,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MesCounterRecord>> GetCountersAsync(
        MesReportFilter filter,
        CancellationToken cancellationToken = default);
}
