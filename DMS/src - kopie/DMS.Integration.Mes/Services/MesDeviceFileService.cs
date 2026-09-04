using DMS.Integration.Mes.Models;
using System.Text;

namespace DMS.Integration.Mes.Services;

public sealed class MesDeviceFileService
{
    public IReadOnlyList<MesDevice> Load(string path)
    {
        EnsureTemplateFile(path);
        var rows = new List<MesDevice>();
        var lineNumber = 0;

        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split(';');
            var device = new MesDevice
            {
                Address = GetPart(parts, 0),
                Category = GetPart(parts, 1).ToUpperInvariant(),
                Name = GetPart(parts, 2),
                Note = GetPart(parts, 3),
                SourceLineNumber = lineNumber
            };

            if (string.IsNullOrWhiteSpace(device.Category))
            {
                device.Category = "DEVICE";
            }

            if (!string.IsNullOrWhiteSpace(device.Address))
            {
                rows.Add(device);
            }
        }

        return rows;
    }

    public void Save(string path, IEnumerable<MesDevice> devices)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Device list path is empty.", nameof(path));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        foreach (var device in devices)
        {
            var address = Clean(device.Address);
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }

            builder.Append(address)
                .Append(';')
                .Append(Clean(device.Category).ToUpperInvariant())
                .Append(';')
                .Append(Clean(device.Name))
                .Append(';')
                .Append(Clean(device.Note))
                .AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    public void EnsureTemplateFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || File.Exists(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            path,
            "# address-or-hostname;category;name;note" + Environment.NewLine +
            "10.131.10.5;SERVER;CZE-FASTEC01;Hlavní MES server" + Environment.NewLine,
            Encoding.UTF8);
    }

    private static string GetPart(string[] parts, int index)
    {
        return index >= 0 && index < parts.Length
            ? Clean(parts[index])
            : string.Empty;
    }

    private static string Clean(string? value)
    {
        return (value ?? string.Empty).Replace(";", ",").Trim();
    }
}
