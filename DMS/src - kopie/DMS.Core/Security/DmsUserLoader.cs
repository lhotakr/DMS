using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DMS.Core.Security;

/// <summary>
/// Loads DMS users from a JSON file.
/// This can later be replaced by a database repository.
/// </summary>
public sealed class DmsUserLoader
{
    public IReadOnlyList<DmsUser> LoadFromJson(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"DMS users file was not found: {filePath}",
                filePath);
        }

        var json = File.ReadAllText(filePath, Encoding.UTF8);

        var users = JsonSerializer.Deserialize<List<DmsUser>>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        return users?
            .Where(user => user.IsActive)
            .ToList()
            ?? new List<DmsUser>();
    }
}
