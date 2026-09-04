using DMS.Integration.Mes.Models;

namespace DMS.Integration.Mes.Clients;

public interface IMesStationClient
{
    Task<MesStationSnapshot> ReadAsync(MesStationDefinition station, CancellationToken cancellationToken = default);
}
