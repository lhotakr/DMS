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

        DmsAppSettings settings;

        if (!File.Exists(localPath))
        {
            settings = new DmsAppSettings();
            DmsStoragePathPolicy.Normalize(settings);
            return settings;
        }

        try
        {
            var json =
                File.ReadAllText(localPath);

            settings =
                JsonSerializer.Deserialize<DmsAppSettings>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    })
                ?? new DmsAppSettings();
        }
        catch
        {
            settings =
                new DmsAppSettings();
        }

        // Runtime canonicalization is intentional:
        // even an old appsettings.json containing Z: or Y:
        // resolves to the UNC namespace.
        DmsStoragePathPolicy.Normalize(settings);

        return settings;
    }
}
