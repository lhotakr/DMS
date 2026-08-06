using DMS.Integration.Mes.Models;
using System.Text.Json;

namespace DMS.Integration.Mes.Services;

public sealed class MesStationDefinitionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public IReadOnlyList<MesStationDefinition> Load(string path)
    {
        EnsureTemplateFile(path);
        var json = File.ReadAllText(path);
        var stations = JsonSerializer.Deserialize<List<MesStationDefinition>>(json, JsonOptions) ?? new List<MesStationDefinition>();
        foreach (var station in stations)
        {
            station.Normalize();
        }

        return stations
            .Where(station => !string.IsNullOrWhiteSpace(station.StationCode))
            .OrderBy(station => station.StationCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Save(string path, IEnumerable<MesStationDefinition> stations)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Station definition path is empty.", nameof(path));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var list = stations.ToList();
        foreach (var station in list)
        {
            station.Normalize();
        }

        File.WriteAllText(path, JsonSerializer.Serialize(list, JsonOptions));
    }

    public void EnsureTemplateFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || File.Exists(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Save(path, CreateDefaultTemplate());
    }

    public static IReadOnlyList<MesStationDataPointDefinition> CreateDefaultDataPoints()
    {
        var points = new List<MesStationDataPointDefinition>
        {
            new() { Name = "Counter1", DisplayName = "Counter 1", Address = "Counter1", DataType = "Int32", Role = MesDataPointRoles.Counter },
            new() { Name = "Counter2", DisplayName = "Counter 2", Address = "Counter2", DataType = "Int32", Role = MesDataPointRoles.Counter }
        };

        for (var i = 1; i <= 6; i++)
        {
            points.Add(new MesStationDataPointDefinition
            {
                Name = $"Input{i}",
                DisplayName = $"Input {i}",
                Address = $"Input{i}",
                DataType = "Bool",
                Role = MesDataPointRoles.Input
            });
        }

        for (var i = 1; i <= 6; i++)
        {
            points.Add(new MesStationDataPointDefinition
            {
                Name = $"Output{i}",
                DisplayName = $"Output {i}",
                Address = $"Output{i}",
                DataType = "Bool",
                Role = MesDataPointRoles.Output
            });
        }

        return points;
    }

    private static IReadOnlyList<MesStationDefinition> CreateDefaultTemplate()
    {
        var points = CreateDefaultDataPoints().ToList();
        return new List<MesStationDefinition>
        {
            new()
            {
                StationCode = "PSL-3",
                DisplayName = "PSL-3",
                WorkCenter = "PSL-3",
                Protocol = MesStationProtocols.SiemensS7,
                Host = "10.131.10.87",
                Port = 102,
                Rack = 0,
                Slot = 1,
                IsActive = true,
                Note = "Siemens PLC. Datové adresy doplň podle reálného S7 mapování.",
                DataPoints = points.Select(ClonePoint).ToList()
            },
            new()
            {
                StationCode = "HPR-1",
                DisplayName = "HPR-1",
                WorkCenter = "HPR-1",
                Protocol = MesStationProtocols.SiemensS7,
                Host = "10.131.10.88",
                Port = 102,
                Rack = 0,
                Slot = 1,
                IsActive = true,
                Note = "Siemens PLC. Datové adresy doplň podle reálného S7 mapování.",
                DataPoints = points.Select(ClonePoint).ToList()
            },
            new()
            {
                StationCode = "KMP-L1",
                DisplayName = "KMP-L1",
                WorkCenter = "KMP-L1",
                Protocol = MesStationProtocols.SiemensS7,
                Host = "10.131.10.89",
                Port = 102,
                Rack = 0,
                Slot = 1,
                IsActive = true,
                Note = "Siemens PLC. Datové adresy doplň podle reálného S7 mapování.",
                DataPoints = points.Select(ClonePoint).ToList()
            },
            new()
            {
                StationCode = "K14-01",
                DisplayName = "BRIO K14-01",
                WorkCenter = "K14-01",
                Protocol = MesStationProtocols.BRGateway,
                Host = "10.131.10.60",
                Port = 0,
                IsActive = true,
                Note = "B&R/BRIO přes gateway. Nastav port, jakmile bude jasné rozhraní.",
                DataPoints = points.Select(ClonePoint).ToList()
            }
        };
    }

    private static MesStationDataPointDefinition ClonePoint(MesStationDataPointDefinition source)
    {
        return new MesStationDataPointDefinition
        {
            Name = source.Name,
            DisplayName = source.DisplayName,
            Address = source.Address,
            DataType = source.DataType,
            Role = source.Role,
            IsRequired = source.IsRequired
        };
    }
}
