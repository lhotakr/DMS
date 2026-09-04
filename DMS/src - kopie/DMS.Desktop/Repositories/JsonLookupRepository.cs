using System.IO;
using System.Text.Json;

namespace DMS.Desktop.Repositories;

public sealed class JsonLookupRepository
{
    public IReadOnlyList<T> LoadList<T>(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new List<T>();
            }

            var json = File.ReadAllText(filePath);

            return JsonSerializer.Deserialize<List<T>>(
                       json,
                       new JsonSerializerOptions
                       {
                           PropertyNameCaseInsensitive = true
                       })
                   ?? new List<T>();
        }
        catch
        {
            return new List<T>();
        }
    }
}