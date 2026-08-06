using DMS.Integration.Mes.Models;

namespace DMS.Integration.Mes.Clients;

/// <summary>
/// Safe placeholder for direct Siemens S7 communication.
/// The DMS UI and configuration are ready, but the exact S7 addresses and the approved driver package
/// must be confirmed before enabling direct reads from production PLCs.
/// </summary>
public sealed class SiemensS7StationClient : MesStationClientBase
{
    public override Task<MesStationSnapshot> ReadAsync(MesStationDefinition station, CancellationToken cancellationToken = default)
    {
        var snapshot = CreateSnapshot(station);
        snapshot.IsOnline = false;
        snapshot.State = "DriverNotConfigured";
        snapshot.ErrorMessage = "Siemens S7 station is configured, but direct S7 driver is not enabled yet. Use FileMirror/TcpText gateway or add approved S7 driver after addresses are confirmed.";

        foreach (var point in station.DataPoints)
        {
            snapshot.DataPoints.Add(CreateValue(point, string.Empty, false, "DriverNotConfigured", snapshot.ErrorMessage));
        }

        return Task.FromResult(snapshot);
    }
}
