using System.Text.Json;

namespace DMS.Core.Sap;

public sealed class SapMaterialRangeLoader
{
    public IReadOnlyList<SapMaterialNumberRange> LoadFromJson(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return Array.Empty<SapMaterialNumberRange>();
        }

        var json = File.ReadAllText(filePath);

        return JsonSerializer.Deserialize<List<SapMaterialNumberRange>>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? new List<SapMaterialNumberRange>();
    }
}