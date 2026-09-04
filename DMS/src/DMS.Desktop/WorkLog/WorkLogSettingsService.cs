using System.IO;
using System.Text;
using System.Text.Json;

namespace DMS.Desktop.WorkLog;

public sealed class WorkLogSettingsService
{
    private readonly string _filePath;

    public WorkLogSettingsService(string configurationRootPath)
    {
        if (string.IsNullOrWhiteSpace(configurationRootPath))
        {
            throw new ArgumentException(
                "Configuration root path is required.",
                nameof(configurationRootPath));
        }

        _filePath = Path.Combine(
            configurationRootPath,
            "worklog-settings.json");
    }

    public string FilePath => _filePath;

    public WorkLogSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new WorkLogSettings();
        }

        try
        {
            var json = File.ReadAllText(_filePath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<WorkLogSettings>(
                json,
                JsonOptions());

            if (settings is null)
            {
                return new WorkLogSettings();
            }

            if (string.IsNullOrWhiteSpace(settings.DatabasePath))
            {
                settings.DatabasePath = WorkLogSettings.DefaultDatabasePath;
            }

            if (settings.ServerTaskIntervalMinutes <= 0)
            {
                settings.ServerTaskIntervalMinutes = 15;
            }

            return settings;
        }
        catch
        {
            return new WorkLogSettings();
        }
    }

    public void Save(WorkLogSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(settings.DatabasePath))
        {
            settings.DatabasePath = WorkLogSettings.DefaultDatabasePath;
        }

        if (settings.ServerTaskIntervalMinutes <= 0)
        {
            settings.ServerTaskIntervalMinutes = 15;
        }

        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException(
                "WorkLog settings directory cannot be resolved.");

        Directory.CreateDirectory(directory);

        if (File.Exists(_filePath))
        {
            var backupPath =
                _filePath +
                ".bak-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmssfff");

            File.Copy(_filePath, backupPath, overwrite: false);
        }

        var json = JsonSerializer.Serialize(
            settings,
            JsonOptions());

        var tempPath =
            _filePath +
            "." +
            Guid.NewGuid().ToString("N") +
            ".tmp";

        File.WriteAllText(
            tempPath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
    }
}
