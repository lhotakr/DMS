using System.Text.Json;

namespace DMS.Core.Sap;

public sealed class SapDecorationRulesLoader
{
    public SapDecorationRulesOptions LoadFromJson(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new SapDecorationRulesOptions();
        }

        var json = File.ReadAllText(filePath);

        return JsonSerializer.Deserialize<SapDecorationRulesOptions>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? new SapDecorationRulesOptions();
    }
}