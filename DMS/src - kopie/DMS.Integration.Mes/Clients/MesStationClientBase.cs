using DMS.Integration.Mes.Models;
using System.Diagnostics;

namespace DMS.Integration.Mes.Clients;

public abstract class MesStationClientBase : IMesStationClient
{
    public abstract Task<MesStationSnapshot> ReadAsync(MesStationDefinition station, CancellationToken cancellationToken = default);

    protected static MesStationSnapshot CreateSnapshot(MesStationDefinition station)
    {
        return new MesStationSnapshot
        {
            StationCode = station.StationCode,
            DisplayName = station.EffectiveName,
            WorkCenter = station.WorkCenter,
            Protocol = station.Protocol,
            Host = station.Host,
            Port = station.Port,
            CheckedAt = DateTime.Now
        };
    }

    protected static MesStationDataPointValue CreateValue(MesStationDataPointDefinition definition, string value, bool isOk, string quality, string error = "")
    {
        return new MesStationDataPointValue
        {
            Name = definition.Name,
            DisplayName = definition.EffectiveDisplayName,
            Address = definition.Address,
            DataType = definition.DataType,
            Role = definition.Role,
            Value = value,
            IsOk = isOk,
            Quality = quality,
            ErrorMessage = error
        };
    }

    protected static async Task<MesStationSnapshot> MeasureAsync(
        MesStationDefinition station,
        Func<Task<MesStationSnapshot>> read)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var snapshot = await read().ConfigureAwait(false);
            sw.Stop();
            snapshot.ResponseTimeMs = sw.ElapsedMilliseconds;
            snapshot.CheckedAt = DateTime.Now;
            return snapshot;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var snapshot = CreateSnapshot(station);
            snapshot.ResponseTimeMs = sw.ElapsedMilliseconds;
            snapshot.IsOnline = false;
            snapshot.State = "Error";
            snapshot.ErrorMessage = ex.Message;
            foreach (var point in station.DataPoints)
            {
                snapshot.DataPoints.Add(CreateValue(point, string.Empty, false, "Error", ex.Message));
            }
            return snapshot;
        }
    }
}
