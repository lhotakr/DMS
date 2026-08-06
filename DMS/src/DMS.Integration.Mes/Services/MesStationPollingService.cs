using DMS.Integration.Mes.Clients;
using DMS.Integration.Mes.Models;
using System.Text.Json;

namespace DMS.Integration.Mes.Services;

public sealed class MesStationPollingService
{
    private readonly MesStationClientFactory _clientFactory = new();

    public async Task<IReadOnlyList<MesStationSnapshot>> PollAsync(
        IReadOnlyList<MesStationDefinition> stations,
        string snapshotFolder,
        int maxParallelism,
        CancellationToken cancellationToken = default)
    {
        maxParallelism = Math.Clamp(maxParallelism, 1, 64);
        using var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);
        var activeStations = stations.Where(station => station.IsActive).ToList();

        var tasks = activeStations.Select(async station =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var client = _clientFactory.Create(station.Protocol, snapshotFolder);
                return await client.ReadAsync(station, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        var snapshots = await Task.WhenAll(tasks).ConfigureAwait(false);
        return snapshots.OrderBy(row => row.StationCode, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void SaveSnapshots(string snapshotFolder, IReadOnlyList<MesStationSnapshot> snapshots)
    {
        if (string.IsNullOrWhiteSpace(snapshotFolder))
        {
            return;
        }

        Directory.CreateDirectory(snapshotFolder);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        foreach (var snapshot in snapshots)
        {
            var safeName = MakeSafeFileName(snapshot.StationCode);
            var path = Path.Combine(snapshotFolder, safeName + ".last.json");
            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, options));
        }
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "station" : result;
    }
}
