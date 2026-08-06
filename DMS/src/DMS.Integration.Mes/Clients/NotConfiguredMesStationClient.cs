using DMS.Integration.Mes.Models;

namespace DMS.Integration.Mes.Clients;

public sealed class NotConfiguredMesStationClient(string message) : MesStationClientBase
{
    public override Task<MesStationSnapshot> ReadAsync(MesStationDefinition station, CancellationToken cancellationToken = default)
    {
        var snapshot = CreateSnapshot(station);
        snapshot.IsOnline = false;
        snapshot.State = "NotConfigured";
        snapshot.ErrorMessage = message;

        foreach (var point in station.DataPoints)
        {
            snapshot.DataPoints.Add(CreateValue(point, string.Empty, false, "NotConfigured", message));
        }

        return Task.FromResult(snapshot);
    }
}
