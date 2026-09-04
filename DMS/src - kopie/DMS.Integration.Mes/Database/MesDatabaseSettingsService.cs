using System.Text;
using System.Text.Json;

namespace DMS.Integration.Mes.Database;

public sealed class MesDatabaseSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public MesDatabaseConnectionSettings Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            var defaults = new MesDatabaseConnectionSettings();
            defaults.Normalize();
            return defaults;
        }

        try
        {
            var json = ReadSharedText(path);
            var settings =
                JsonSerializer.Deserialize<MesDatabaseConnectionSettings>(
                    json,
                    JsonOptions)
                ?? new MesDatabaseConnectionSettings();

            settings.Normalize();
            return settings;
        }
        catch
        {
            var defaults = new MesDatabaseConnectionSettings();
            defaults.Normalize();
            return defaults;
        }
    }

    public void Save(
        string path,
        MesDatabaseConnectionSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);

        settings.Normalize();

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(path))
        {
            var backup =
                path +
                $".bak-{DateTime.Now:yyyyMMdd-HHmmss-fff}";

            File.Copy(path, backup, overwrite: false);
        }

        var json =
            JsonSerializer.Serialize(
                settings,
                JsonOptions);

        var temporaryPath =
            path + ".tmp";

        File.WriteAllText(
            temporaryPath,
            json,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false));

        File.Move(
            temporaryPath,
            path,
            overwrite: true);
    }

    private static string ReadSharedText(
        string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        return reader.ReadToEnd();
    }
}
