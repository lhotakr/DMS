using DMS.Integration.Mes.Models;

namespace DMS.Integration.Mes.Clients;

public sealed class SimulatedMesStationClient : MesStationClientBase
{
    public override Task<MesStationSnapshot> ReadAsync(MesStationDefinition station, CancellationToken cancellationToken = default)
    {
        return MeasureAsync(station, () =>
        {
            var snapshot = CreateSnapshot(station);
            snapshot.IsOnline = true;
            snapshot.State = "Online";
            var seed = Math.Abs(HashCode.Combine(station.StationCode, DateTime.Now.Minute));

            foreach (var point in station.DataPoints)
            {
                var isCounter = string.Equals(point.Role, MesDataPointRoles.Counter, StringComparison.OrdinalIgnoreCase);
                var value = isCounter
                    ? ((seed + point.Name.GetHashCode()) % 100000).ToString()
                    : (((seed + point.Name.GetHashCode()) & 1) == 0 ? "False" : "True");

                snapshot.DataPoints.Add(CreateValue(point, value, true, "Simulated"));
            }

            return Task.FromResult(snapshot);
        });
    }
}
