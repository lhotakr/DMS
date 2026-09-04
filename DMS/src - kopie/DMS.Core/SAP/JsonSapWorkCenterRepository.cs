using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DMS.Core.Sap;

public sealed class JsonSapWorkCenterRepository
{
    private readonly string _filePath;

    public JsonSapWorkCenterRepository(string filePath)
    {
        _filePath = filePath;
    }

    public void SaveAll(IReadOnlyList<SapWorkCenter> workCenters)
    {
        var directory = Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(
            workCenters,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

        File.WriteAllText(
            _filePath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public IReadOnlyList<SapWorkCenter> LoadAll()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<SapWorkCenter>();
        }

        var json = File.ReadAllText(_filePath, Encoding.UTF8);

        return JsonSerializer.Deserialize<List<SapWorkCenter>>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? new List<SapWorkCenter>();
    }
}