using System.Text.Json;

namespace DMS.Core.Sap;

public sealed class SapMaterialRulesLoader
{
    public SapMaterialRulesOptions LoadFromJson(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new SapMaterialRulesOptions();
        }

        var json = File.ReadAllText(filePath);

        return JsonSerializer.Deserialize<SapMaterialRulesOptions>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? new SapMaterialRulesOptions();
    }

    public void SaveToJson(string filePath, SapMaterialRulesOptions options)
    {
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(
            options,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        File.WriteAllText(filePath, json);
    }
}