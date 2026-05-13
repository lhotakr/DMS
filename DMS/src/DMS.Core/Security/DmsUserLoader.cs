using System.Text.Json;

namespace DMS.Core.Security;

/// <summary>
/// Načítá uživatele DMS z JSON souboru.
/// Později může být nahrazen databázovým repository.
/// </summary>
public sealed class DmsUserLoader
{
    public IReadOnlyList<DmsUser> LoadFromJson(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"Soubor s uživateli DMS nebyl nalezen: {filePath}",
                filePath);
        }

        var json = File.ReadAllText(filePath);

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