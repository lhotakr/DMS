using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DMS.Core.Sap.Validation;

public sealed class JsonSapValidationRuleRepository
{
    private readonly string _filePath;

    public JsonSapValidationRuleRepository(string filePath)
    {
        _filePath = filePath;
    }

    public SapValidationRuleSet Load()
    {
        if (!File.Exists(_filePath))
        {
            return CreateDefaultRuleSet();
        }

        var json = File.ReadAllText(_filePath, Encoding.UTF8);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        options.Converters.Add(new FlexibleStringJsonConverter());

        return JsonSerializer.Deserialize<SapValidationRuleSet>(
            json,
            options)
            ?? CreateDefaultRuleSet();
    }

    public void Save(SapValidationRuleSet ruleSet)
    {
        var directory = Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        options.Converters.Add(new FlexibleStringJsonConverter());

        var json = JsonSerializer.Serialize(ruleSet, options);

        File.WriteAllText(
            _filePath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static SapValidationRuleSet CreateDefaultRuleSet()
    {
        return new SapValidationRuleSet
        {
            Version = 1,
            Name = "SAP validační pravidla DMS",
            Rules = new List<SapValidationRule>()
        };
    }
}