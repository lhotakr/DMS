using System.Net;
using System.Text;

namespace DMS.Core.Mes;

public sealed class MesDeviceInventoryLoadResult
{
    public bool Success { get; init; }

    public IReadOnlyList<MesDeviceEntry> Devices { get; init; } =
        Array.Empty<MesDeviceEntry>();

    public IReadOnlyList<string> Errors { get; init; } =
        Array.Empty<string>();
}

public sealed class MesDeviceInventoryParser
{
    public MesDeviceInventoryLoadResult Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Failure("The devices.txt path is empty.");
        }

        if (!File.Exists(filePath))
        {
            return Failure($"The devices inventory file does not exist: {filePath}");
        }

        IReadOnlyList<string> lines;

        try
        {
            lines = ReadSharedLines(filePath);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return Failure(
                $"The devices inventory could not be read: {filePath}; {ex.Message}");
        }

        var devices = new List<MesDeviceEntry>();
        var errors = new List<string>();
        var uniqueIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < lines.Count; index++)
        {
            var rawLine = lines[index];
            var lineNumber = index + 1;
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith('#') ||
                line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split(';');

            if (parts.Length < 3)
            {
                errors.Add(
                    $"Line {lineNumber}: expected IP;TYPE;NAME; but found '{line}'.");
                continue;
            }

            var ipAddress = parts[0].Trim();
            var deviceType = parts[1].Trim();
            var name = parts[2].Trim();

            if (!IPAddress.TryParse(ipAddress, out _))
            {
                errors.Add(
                    $"Line {lineNumber}: invalid IP address '{ipAddress}'.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(deviceType))
            {
                errors.Add(
                    $"Line {lineNumber}: device type is empty for {ipAddress}.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add(
                    $"Line {lineNumber}: device name is empty for {ipAddress}.");
                continue;
            }

            if (!uniqueIps.Add(ipAddress))
            {
                errors.Add(
                    $"Line {lineNumber}: duplicate IP address '{ipAddress}'. " +
                    "The first entry is used.");
                continue;
            }

            devices.Add(new MesDeviceEntry
            {
                IpAddress = ipAddress,
                DeviceType = deviceType,
                Name = name,
                RawLine = rawLine,
                SourceLineNumber = lineNumber
            });
        }

        return new MesDeviceInventoryLoadResult
        {
            Success = true,
            Devices = devices,
            Errors = errors
        };
    }

    private static IReadOnlyList<string> ReadSharedLines(string filePath)
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

        var lines = new List<string>();

        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static MesDeviceInventoryLoadResult Failure(string error)
    {
        return new MesDeviceInventoryLoadResult
        {
            Success = false,
            Errors = new[] { error }
        };
    }
}
