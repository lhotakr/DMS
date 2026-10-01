using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Scheduler.Configuration;

/// <summary>
/// Per-user scheduler settings. No administrator rights are required.
/// </summary>
public sealed class SchedulerSettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public string SettingsPath { get; }

    public SchedulerSettingsRepository(
        string? settingsPath = null)
    {
        SettingsPath =
            string.IsNullOrWhiteSpace(settingsPath)
                ? GetDefaultSettingsPath()
                : Path.GetFullPath(
                    Environment.ExpandEnvironmentVariables(
                        settingsPath));
    }

    public async Task<SchedulerSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            var settings =
                new SchedulerSettings();

            await SaveAsync(
                settings,
                cancellationToken);

            return settings;
        }

        await using var stream =
            File.OpenRead(SettingsPath);

        var result =
            await JsonSerializer.DeserializeAsync<SchedulerSettings>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? new SchedulerSettings();

        result.Normalize();

        return result;
    }

    public async Task SaveAsync(
        SchedulerSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Normalize();

        var directory =
            Path.GetDirectoryName(SettingsPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        var temporary =
            SettingsPath + ".tmp";

        await using (var stream =
                     File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                settings,
                JsonOptions,
                cancellationToken);
        }

        File.Move(
            temporary,
            SettingsPath,
            overwrite: true);
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
            "scheduler-settings.json");
    }

    public static string GetRuntimeDirectory()
    {
        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(
            localAppData,
            "DMS",
            "Scheduler");
    }
}
