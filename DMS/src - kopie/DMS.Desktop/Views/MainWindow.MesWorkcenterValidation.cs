using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Reporting;
using System.IO;
using System.Text.Json;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private readonly HashSet<string> _mesWorkcenterCodes =
        new(StringComparer.OrdinalIgnoreCase);

    // False means that the FASTEC lookup could not be loaded. In that case
    // MESWC remains permissive so a database/configuration problem is not
    // incorrectly reported as an invalid work-center code.
    private bool _mesWorkcenterValidationAvailable;

    private async Task LoadMesWorkcenterCodesAsync()
    {
        _mesWorkcenterValidationAvailable = false;
        _mesWorkcenterCodes.Clear();

        try
        {
            var settingsPath = FindMesDatabaseSettingsPath();

            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                _logger.Warning(
                    "MESWC validation skipped: MES database settings file was not found.");
                return;
            }

            var settingsService =
                new MesDatabaseSettingsService();

            var settings =
                settingsService.Load(settingsPath);

            if (!settings.IsEnabled)
            {
                _logger.Info(
                    "MESWC validation skipped: MES SQL reporting connection is disabled.");
                return;
            }

            var service =
                new MesReportingDataService(settings);

            var workcenters =
                await service.GetWorkcentersAsync();

            foreach (var workcenter in workcenters)
            {
                var code = workcenter.Code?.Trim();

                if (!string.IsNullOrWhiteSpace(code))
                {
                    _mesWorkcenterCodes.Add(
                        code.ToUpperInvariant());
                }
            }

            _mesWorkcenterValidationAvailable = true;

            _logger.Info(
                $"MESWC work-center validation loaded: {_mesWorkcenterCodes.Count} codes.");
        }
        catch (Exception ex)
        {
            _mesWorkcenterCodes.Clear();
            _mesWorkcenterValidationAvailable = false;

            _logger.Error(
                "MESWC work-center validation load failed. Validation remains permissive.",
                ex);
        }
    }

    private string? FindMesDatabaseSettingsPath()
    {
        var configurationRoot =
            _appSettings.ConfigurationRootPath;

        if (string.IsNullOrWhiteSpace(configurationRoot) ||
            !Directory.Exists(configurationRoot))
        {
            return null;
        }

        // Prefer conventional names first. The JSON probe below keeps this
        // compatible with older DMS builds that used a different file name.
        var conventionalNames = new[]
        {
            "mes-database-settings.json",
            "mes-reporting-settings.json",
            "mes-sql-settings.json"
        };

        foreach (var fileName in conventionalNames)
        {
            var candidate =
                Path.Combine(configurationRoot, fileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var candidate in Directory.EnumerateFiles(
                     configurationRoot,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            if (LooksLikeMesDatabaseSettings(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool LooksLikeMesDatabaseSettings(
        string filePath)
    {
        try
        {
            using var document =
                JsonDocument.Parse(
                    File.ReadAllText(filePath));

            if (document.RootElement.ValueKind !=
                JsonValueKind.Object)
            {
                return false;
            }

            var names = document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return names.Contains("Server") &&
                   names.Contains("Database") &&
                   (names.Contains("Schema") ||
                    names.Contains("ReportingSchema"));
        }
        catch
        {
            return false;
        }
    }
}
