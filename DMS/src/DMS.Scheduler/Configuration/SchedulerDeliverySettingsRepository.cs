using System;
using System.IO;
using System.Text.Json;

namespace DMS.Scheduler.Configuration;

public sealed class SchedulerDeliverySettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public string SettingsPath { get; }

    public SchedulerDeliverySettingsRepository(
        string? settingsPath = null)
    {
        SettingsPath =
            string.IsNullOrWhiteSpace(settingsPath)
                ? GetDefaultSettingsPath()
                : Path.GetFullPath(
                    Environment.ExpandEnvironmentVariables(
                        settingsPath));
    }

    public SchedulerDeliverySettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            var defaults = new SchedulerDeliverySettings();
            Save(defaults);
            return defaults;
        }

        var json = File.ReadAllText(SettingsPath);

        return JsonSerializer.Deserialize<SchedulerDeliverySettings>(
                   json,
                   JsonOptions)
               ?? new SchedulerDeliverySettings();
    }

    public void Save(
        SchedulerDeliverySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    public static string GetDefaultSettingsPath()
    {
        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(
            localAppData,
            "DMS",
            "Scheduler",
            "scheduler-delivery-settings.json");
    }
}
