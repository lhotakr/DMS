using DMS.Core.Mes;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DMS.Desktop.Configuration.Mes;

public static class MesConfigurationJson
{
    public static JsonSerializerOptions CreateOptions(bool writeIndented = false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = writeIndented,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public sealed class MesIntegrationSettingsService
{
    private readonly string _filePath;

    public MesIntegrationSettingsService(string filePath)
    {
        _filePath = filePath;
    }

    public string LastError { get; private set; } = string.Empty;

    public MesIntegrationSettings Load()
    {
        LastError = string.Empty;

        try
        {
            if (!File.Exists(_filePath))
            {
                LastError = $"MES integration settings file does not exist: {_filePath}";
                return new MesIntegrationSettings();
            }

            var json = ReadSharedText(_filePath);

            return JsonSerializer.Deserialize<MesIntegrationSettings>(
                       json,
                       MesConfigurationJson.CreateOptions())
                   ?? new MesIntegrationSettings();
        }
        catch (Exception ex)
        {
            LastError =
                $"MES integration settings could not be loaded: {_filePath}; {ex.Message}";
            return new MesIntegrationSettings();
        }
    }

    private static string ReadSharedText(string filePath)
    {
        using var stream = new FileStream(
            filePath,
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

public sealed class MesPlcBindingService
{
    private readonly string _filePath;

    public MesPlcBindingService(string filePath)
    {
        _filePath = filePath;
    }

    public string LastError { get; private set; } = string.Empty;

    public void Save(MesPlcBindingSet bindingSet)
    {
        ArgumentNullException.ThrowIfNull(bindingSet);
        LastError = string.Empty;

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(_filePath))
            {
                var backupPath = _filePath + $".bak-{DateTime.Now:yyyyMMdd-HHmmss-fff}";
                File.Copy(_filePath, backupPath, overwrite: false);
            }

            bindingSet.Devices = bindingSet.Devices
                .Where(binding => !string.IsNullOrWhiteSpace(binding.StationCode))
                .OrderBy(binding => binding.StationCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var json = JsonSerializer.Serialize(
                bindingSet,
                MesConfigurationJson.CreateOptions(writeIndented: true));

            var temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            LastError = $"MES PLC bindings could not be saved: {_filePath}; {ex.Message}";
            throw;
        }
    }

    public MesPlcBindingSet Load()
    {
        LastError = string.Empty;

        try
        {
            if (!File.Exists(_filePath))
            {
                LastError = $"MES PLC bindings file does not exist: {_filePath}";
                return new MesPlcBindingSet();
            }

            var json = ReadSharedText(_filePath);

            var result = JsonSerializer.Deserialize<MesPlcBindingSet>(
                             json,
                             MesConfigurationJson.CreateOptions())
                         ?? new MesPlcBindingSet();

            result.Devices ??= new List<MesPlcBinding>();
            foreach (var binding in result.Devices)
            {
                binding.Modules ??= new List<MesModuleDefinition>();
                binding.DataPoints ??= new List<MesDataPointDefinition>();
            }

            return result;
        }
        catch (Exception ex)
        {
            LastError =
                $"MES PLC bindings could not be loaded: {_filePath}; {ex.Message}";
            return new MesPlcBindingSet();
        }
    }

    private static string ReadSharedText(string filePath)
    {
        using var stream = new FileStream(
            filePath,
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

public static class MesConfigurationPathResolver
{
    public static string Resolve(
        string configurationRootPath,
        string? configuredPath)
    {
        var value = configuredPath?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return configurationRootPath;
        }

        if (Path.IsPathRooted(value))
        {
            return Path.GetFullPath(value);
        }

        return Path.GetFullPath(
            Path.Combine(configurationRootPath, value));
    }
}
