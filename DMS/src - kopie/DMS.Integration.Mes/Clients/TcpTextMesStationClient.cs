using DMS.Integration.Mes.Models;
using System.Net.Sockets;
using System.Text;

namespace DMS.Integration.Mes.Clients;

public sealed class TcpTextMesStationClient : MesStationClientBase
{
    public override Task<MesStationSnapshot> ReadAsync(MesStationDefinition station, CancellationToken cancellationToken = default)
    {
        return MeasureAsync(station, async () =>
        {
            if (string.IsNullOrWhiteSpace(station.Host) || station.Port <= 0)
            {
                throw new InvalidOperationException("Host/port is not configured for TCP MES station gateway.");
            }

            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(station.PollTimeoutMs));
            await client.ConnectAsync(station.Host, station.Port, timeoutCts.Token).ConfigureAwait(false);

            await using var stream = client.GetStream();
            var request = Encoding.UTF8.GetBytes($"READ {station.StationCode}\n");
            await stream.WriteAsync(request, timeoutCts.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var buffer = new char[8192];
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token).ConfigureAwait(false);
            var response = new string(buffer, 0, read);
            var parsed = ParseKeyValues(response);

            var snapshot = CreateSnapshot(station);
            snapshot.IsOnline = true;
            snapshot.State = "Online";

            foreach (var point in station.DataPoints)
            {
                var key = string.IsNullOrWhiteSpace(point.Address) ? point.Name : point.Address;
                if (parsed.TryGetValue(key, out var value) || parsed.TryGetValue(point.Name, out value))
                {
                    snapshot.DataPoints.Add(CreateValue(point, value, true, "Gateway"));
                }
                else
                {
                    snapshot.DataPoints.Add(CreateValue(point, string.Empty, false, "Missing", $"Value '{key}' was not returned by gateway."));
                }
            }

            return snapshot;
        });
    }

    private static Dictionary<string, string> ParseKeyValues(string response)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var separators = new[] { '\r', '\n', ';' };
        foreach (var part in response.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = part.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            var key = part[..index].Trim();
            var value = part[(index + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }
}
