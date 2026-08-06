using DMS.Integration.Mes.Models;
using System.Text.Json;

namespace DMS.Integration.Mes.Clients;

public sealed class FileMirrorMesStationClient(string snapshotFolder) : MesStationClientBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public override Task<MesStationSnapshot> ReadAsync(MesStationDefinition station, CancellationToken cancellationToken = default)
    {
        return MeasureAsync(station, () =>
        {
            if (string.IsNullOrWhiteSpace(snapshotFolder))
            {
                throw new InvalidOperationException("Station snapshot folder is empty.");
            }

            var path = Path.Combine(snapshotFolder, station.StationCode + ".json");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Station mirror file was not found.", path);
            }

            var text = File.ReadAllText(path);
            var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(text, JsonOptions)
                         ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            var snapshot = CreateSnapshot(station);
            snapshot.IsOnline = true;
            snapshot.State = "Online";

            foreach (var point in station.DataPoints)
            {
                var key = string.IsNullOrWhiteSpace(point.Address) ? point.Name : point.Address;
                if (values.TryGetValue(key, out var value) || values.TryGetValue(point.Name, out value))
                {
                    snapshot.DataPoints.Add(CreateValue(point, ConvertValue(value), true, "FileMirror"));
                }
                else
                {
                    snapshot.DataPoints.Add(CreateValue(point, string.Empty, false, "Missing", $"Value '{key}' is missing in mirror file."));
                }
            }

            return Task.FromResult(snapshot);
        });
    }

    private static string ConvertValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.True => "True",
                JsonValueKind.False => "False",
                JsonValueKind.Number => element.ToString(),
                JsonValueKind.String => element.GetString() ?? string.Empty,
                _ => element.ToString()
            };
        }

        return value.ToString() ?? string.Empty;
    }
}
