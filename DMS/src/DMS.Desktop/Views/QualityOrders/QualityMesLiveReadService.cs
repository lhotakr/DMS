using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Orders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Desktop.Views.QualityOrders;

/// <summary>Display-only MES snapshot. QA's persisted quantities and approvals are untouched.</summary>
internal sealed class QualityMesLiveResult
{
    public IReadOnlyDictionary<string, MesProductionOrderRecord> Rows { get; init; } =
        new Dictionary<string, MesProductionOrderRecord>(StringComparer.OrdinalIgnoreCase);
    public bool IsFresh { get; init; }
    public DateTime? RetrievedAt { get; init; }
    public string Error { get; init; } = string.Empty;
}

internal sealed class QualityMesLiveReadService
{
    private readonly string _configRoot;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (MesProductionOrderRecord Record, DateTime RetrievedAt)> LastKnown =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] SettingsNames =
    {
        "mes-database-settings.json", "mes-reporting-settings.json",
        "mes-sql-settings.json", "mes-database.json", "mes-reporting.json"
    };

    public QualityMesLiveReadService(string configRoot) =>
        _configRoot = configRoot ?? string.Empty;

    public async Task<QualityMesLiveResult> ReadAsync(
        IEnumerable<string> orderNumbers, CancellationToken cancellationToken = default)
    {
        var wanted = orderNumbers.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (wanted.Length == 0) return new QualityMesLiveResult { IsFresh = true };
        try
        {
            var path = SettingsNames.Select(name => Path.Combine(_configRoot, name))
                .FirstOrDefault(File.Exists);
            if (path is null) throw new InvalidOperationException("MESSET configuration not found.");
            var settings = new MesDatabaseSettingsService().Load(path);
            if (!settings.IsEnabled) throw new InvalidOperationException("MES SQL connection is disabled in MESSET.");
            var service = new MesOrderOverviewDataService(settings);
            var fetched = await service.GetOrdersByCodesAsync(wanted, cancellationToken);
            var rows = fetched.ToDictionary(x => x.OrderCode, StringComparer.OrdinalIgnoreCase);
            var now = DateTime.Now;
            lock (Gate)
            {
                foreach (var id in wanted) LastKnown.Remove(id);
                foreach (var item in rows) LastKnown[item.Key] = (item.Value, now);
            }
            return new QualityMesLiveResult { Rows = rows, IsFresh = true, RetrievedAt = now };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Dictionary<string, MesProductionOrderRecord> cached;
            DateTime? time;
            lock (Gate)
            {
                var found = wanted.Where(LastKnown.ContainsKey).ToArray();
                cached = found.ToDictionary(x => x, x => LastKnown[x].Record,
                    StringComparer.OrdinalIgnoreCase);
                time = found.Length > 0
                    ? found.Select(x => LastKnown[x].RetrievedAt).Min() : null;
            }
            return new QualityMesLiveResult
            {
                Rows = cached, IsFresh = false, RetrievedAt = time, Error = ex.Message
            };
        }
    }
}
