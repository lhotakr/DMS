using System.Text.Json;

namespace DMS.Core.Sap.Diagnostics;

public sealed class SapCacheStatusService
{
    private readonly string _basePath;
    private readonly string _configPath;

    public SapCacheStatusService(string basePath, string configPath)
    {
        _basePath = basePath;
        _configPath = configPath;
    }

    public SapCacheStatusOverview BuildOverview()
    {
        var dataPath = Path.Combine(_basePath, "Data");

        var overview = new SapCacheStatusOverview
        {
            BasePath = _basePath,
            CreatedAt = DateTime.Now
        };

        overview.Rows.Add(CreateJsonArrayRow(
            "Materiály",
            Path.Combine(dataPath, "sap-materials.json"),
            "materiálů"));

        overview.Rows.Add(CreateJsonArrayRow(
            "Kusovníky",
            Path.Combine(dataPath, "sap-boms.json"),
            "kusovníků"));

        overview.Rows.Add(CreateJsonArrayRow(
            "Pracovní postupy",
            Path.Combine(dataPath, "sap-routings.json"),
            "postupů"));

        overview.Rows.Add(CreateJsonArrayRow(
            "Pracoviště",
            Path.Combine(dataPath, "sap-work-centers.json"),
            "pracovišť"));

        overview.Rows.Add(CreateJsonArrayRow(
            "Validační pravidla",
            Path.Combine(_configPath, "sap-validation-rules.json"),
            "pravidel"));

        overview.Rows.Add(CreateJsonArrayRow(
            "Pravidla materiálů",
            Path.Combine(_configPath, "sap-material-rules.json"),
            "pravidel"));

        overview.Rows.Add(CreateJsonArrayRow(
            "Pravidla dekorací",
            Path.Combine(_configPath, "sap-decoration-rules.json"),
            "pravidel"));

        return overview;
    }

    private static SapCacheStatusRow CreateJsonArrayRow(
        string area,
        string filePath,
        string countUnit)
    {
        var exists = File.Exists(filePath);
        var count = exists ? TryCountJsonArrayItems(filePath) : null;

        return new SapCacheStatusRow
        {
            Area = area,
            FileName = Path.GetFileName(filePath),
            Status = exists ? "OK" : "Chybí",
            Count = count,
            CountText = exists
                ? count.HasValue
                    ? $"{count.Value:N0} {countUnit}"
                    : "Nelze načíst"
                : "-",
            LastChangedText = exists
                ? File.GetLastWriteTime(filePath).ToString("dd.MM.yyyy HH:mm:ss")
                : "-",
            Path = filePath,
            Exists = exists
        };
    }

    private static int? TryCountJsonArrayItems(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var document = JsonDocument.Parse(stream);

            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.GetArrayLength()
                : null;
        }
        catch
        {
            return null;
        }
    }
}