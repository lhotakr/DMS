using System.Text.Json;

namespace DMS.Core.Sap;

public sealed class SapMaterialStatusRuleLoader
{
    public IReadOnlyList<SapMaterialStatusRule> LoadFromJson(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return Array.Empty<SapMaterialStatusRule>();
        }

        var json = File.ReadAllText(filePath);

        return JsonSerializer.Deserialize<List<SapMaterialStatusRule>>(
                   json,
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = true
                   })
               ?? new List<SapMaterialStatusRule>();
    }
}