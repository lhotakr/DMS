using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.IO;

namespace DMS.Desktop.Services.Checklists;

internal static class AtomicChecklistJsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static T? Load<T>(string path)
    {
        if (!File.Exists(path)) return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), Options);
    }

    public static void Save<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Missing directory.");
        Directory.CreateDirectory(directory);
        var tempPath = path + ".tmp";
        var backupPath = path + $".bak-{DateTime.Now:yyyyMMdd-HHmmss}";
        var json = JsonSerializer.Serialize(value, Options);
        File.WriteAllText(tempPath, json, new UTF8Encoding(false));
        _ = JsonSerializer.Deserialize<T>(File.ReadAllText(tempPath, Encoding.UTF8), Options)
            ?? throw new InvalidOperationException("Checklist JSON validation failed.");
        if (File.Exists(path)) File.Copy(path, backupPath, overwrite: false);
        File.Move(tempPath, path, overwrite: true);
    }
}
