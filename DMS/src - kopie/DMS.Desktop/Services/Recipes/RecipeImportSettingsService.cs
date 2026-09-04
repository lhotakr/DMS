using DMS.Core.Recipes;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DMS.Desktop.Services.Recipes;

public sealed class RecipeImportSettingsService
{
    private readonly string _path;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public RecipeImportSettingsService(string path)
    {
        _path = path;
    }

    public string Path => _path;

    public RecipeImportSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new RecipeImportSettings();
        }

        try
        {
            var json = File.ReadAllText(_path, Encoding.UTF8);
            return JsonSerializer.Deserialize<RecipeImportSettings>(json, JsonOptions)
                   ?? new RecipeImportSettings();
        }
        catch
        {
            return new RecipeImportSettings();
        }
    }

    public void Save(RecipeImportSettings settings)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_path, json, new UTF8Encoding(true));
    }
}
