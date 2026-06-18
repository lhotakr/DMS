using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DMS.Core.Sap;

public sealed class JsonSapRoutingRepository
{
    private readonly string _filePath;

    public JsonSapRoutingRepository(string filePath)
    {
        _filePath = filePath;
    }

    public void SaveAll(IReadOnlyList<SapRouting> routings)
    {
        var directory = Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(
            routings,
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

    public IReadOnlyList<SapRouting> LoadAll()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<SapRouting>();
        }

        var json = File.ReadAllText(_filePath, Encoding.UTF8);

        return JsonSerializer.Deserialize<List<SapRouting>>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? new List<SapRouting>();
    }
}