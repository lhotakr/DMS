using System.IO;
using System.Text.Json;

namespace DMS.Desktop.Settings;

public sealed class DmsUserSettingsService
{
    private readonly string _settingsDirectory;
    private readonly string _settingsFilePath;

    public DmsUserSettingsService()
    {
        _settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DMS");

        _settingsFilePath = Path.Combine(_settingsDirectory, "user-settings.json");
    }

    public DmsUserSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new DmsUserSettings();
            }

            var json = File.ReadAllText(_settingsFilePath);

            return JsonSerializer.Deserialize<DmsUserSettings>(json)
                   ?? new DmsUserSettings();
        }
        catch
        {
            return new DmsUserSettings();
        }
    }

    public void Save(DmsUserSettings settings)
    {
        Directory.CreateDirectory(_settingsDirectory);

        var json = JsonSerializer.Serialize(
            settings,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        File.WriteAllText(_settingsFilePath, json);
    }
}