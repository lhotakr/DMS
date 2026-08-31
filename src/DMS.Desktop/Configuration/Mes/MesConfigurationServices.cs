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

            return JsonSerializer.Deserialize<MesPlcBindingSet>(
                       json,
                       MesConfigurationJson.CreateOptions())
                   ?? new MesPlcBindingSet();
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
