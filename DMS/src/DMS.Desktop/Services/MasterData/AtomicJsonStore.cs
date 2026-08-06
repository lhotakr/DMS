using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DMS.Desktop.Services.MasterData;

public sealed class AtomicJsonStore<T> where T : class, new()
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _options;

    public AtomicJsonStore(string filePath)
    {
        _filePath = filePath;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public string FilePath => _filePath;

    public T Load()
    {
        if (!File.Exists(_filePath)) return new T();
        var json = File.ReadAllText(_filePath, Encoding.UTF8);
        return JsonSerializer.Deserialize<T>(json, _options) ?? new T();
    }

    public void Save(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temporaryPath = _filePath + ".tmp";
        var backupPath = _filePath + $".bak-{DateTime.Now:yyyyMMdd-HHmmss}";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, _options), new UTF8Encoding(true));

        if (File.Exists(_filePath))
        {
            File.Copy(_filePath, backupPath, overwrite: false);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        else
        {
            File.Move(temporaryPath, _filePath);
        }
    }
}
