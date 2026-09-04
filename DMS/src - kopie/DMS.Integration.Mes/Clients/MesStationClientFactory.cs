using DMS.Integration.Mes.Models;

namespace DMS.Integration.Mes.Clients;

public sealed class MesStationClientFactory
{
    public IMesStationClient Create(string protocol, string snapshotFolder)
    {
        return (protocol ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "SIMULATED" => new SimulatedMesStationClient(),
            "FILEMIRROR" => new FileMirrorMesStationClient(snapshotFolder),
            "TCPTEXT" => new TcpTextMesStationClient(),
            "BRGATEWAY" => new TcpTextMesStationClient(),
            "SIEMENSS7" => new SiemensS7StationClient(),
            _ => new NotConfiguredMesStationClient($"Unsupported protocol '{protocol}'.")
        };
    }
}
