using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DMS.Core.Sap;

public sealed class JsonSapMaterialRepository
{
    private readonly string _filePath;

    public JsonSapMaterialRepository(string filePath)
    {
        _filePath = filePath;
    }

    public IReadOnlyList<SapMaterial> LoadAll()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<SapMaterial>();
        }

        var json = File.ReadAllText(_filePath, Encoding.UTF8);

        return JsonSerializer.Deserialize<List<SapMaterial>>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? new List<SapMaterial>();
    }

    public SapMaterial? FindByMaterialNumber(string materialNumber)
    {
        return LoadAll().FirstOrDefault(item =>
            string.Equals(
                item.MaterialNumber,
                materialNumber,
                StringComparison.OrdinalIgnoreCase));
    }

    public void SaveAll(IReadOnlyList<SapMaterial> materials)
    {
        var directory = Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(
            materials.OrderBy(item => item.MaterialNumber).ToList(),
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
}