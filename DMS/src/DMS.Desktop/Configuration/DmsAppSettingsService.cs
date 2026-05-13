using System.IO;
using System.Text.Json;

namespace DMS.Desktop.Configuration;

public sealed class DmsAppSettingsService
{
    public DmsAppSettings Load()
    {
        var localPath = Path.Combine(
            AppContext.BaseDirectory,
            "Config",
            "appsettings.json");

        if (!File.Exists(localPath))
        {
            return new DmsAppSettings();
        }

        try
        {
            var json = File.ReadAllText(localPath);

            return JsonSerializer.Deserialize<DmsAppSettings>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new DmsAppSettings();
        }
        catch
        {
            return new DmsAppSettings();
        }
    }
}