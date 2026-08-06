using DMS.Integration.Mes.Models;
using System.Text.Json;

namespace DMS.Integration.Mes.Services;

public sealed class MesCommunicationSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public MesCommunicationSettings Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            var defaults = new MesCommunicationSettings();
            defaults.Normalize();
            return defaults;
        }

        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<MesCommunicationSettings>(json, JsonOptions) ?? new MesCommunicationSettings();
        settings.Normalize();
        return settings;
    }

    public void Save(string path, MesCommunicationSettings settings)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Settings path is empty.", nameof(path));
        }

        settings.Normalize();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }
}
