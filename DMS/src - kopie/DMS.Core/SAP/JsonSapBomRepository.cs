using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DMS.Core.Sap;

public sealed class JsonSapBomRepository
{
    private readonly string _filePath;

    public JsonSapBomRepository(string filePath)
    {
        _filePath = filePath;
    }

    public void SaveAll(IReadOnlyList<SapBom> boms)
    {
        var directory = Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(
            boms,
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

    public IReadOnlyList<SapBom> LoadAll()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<SapBom>();
        }

        var json = File.ReadAllText(_filePath, Encoding.UTF8);

        return JsonSerializer.Deserialize<List<SapBom>>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? new List<SapBom>();
    }
}