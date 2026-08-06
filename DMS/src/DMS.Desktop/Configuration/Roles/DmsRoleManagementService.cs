using System.IO;
using System.Text.Json;

namespace DMS.Desktop.Configuration.Roles;

public sealed class DmsRoleManagementService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _rolesPath;

    public DmsRoleManagementService(string rolesPath)
    {
        _rolesPath = rolesPath;
    }

    public List<DmsRoleDefinition> LoadAll()
    {
        if (!File.Exists(_rolesPath))
        {
            var defaults = CreateDefaultRoles();
            SaveAll(defaults);
            return defaults;
        }

        var json = File.ReadAllText(_rolesPath);

        var roles = JsonSerializer.Deserialize<List<DmsRoleDefinition>>(json, JsonOptions)
                    ?? new List<DmsRoleDefinition>();

        if (roles.Count == 0)
        {
            roles = CreateDefaultRoles();
            SaveAll(roles);
        }

        return roles;
    }

    public void SaveAll(IEnumerable<DmsRoleDefinition> roles)
    {
        var normalized = roles
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .Select(x => new DmsRoleDefinition
            {
                Code = x.Code.Trim().ToUpperInvariant(),
                Name = x.Name.Trim(),
                Description = x.Description.Trim(),
                IsActive = x.IsActive
            })
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Code)
            .ToList();

        var directory = Path.GetDirectoryName(_rolesPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(_rolesPath, json);
    }

    private static List<DmsRoleDefinition> CreateDefaultRoles()
    {
        return new List<DmsRoleDefinition>
        {
            new()
            {
                Code = "DMS_ADMIN",
                Name = "Administrátor DMS",
                Description = "Plný přístup ke správě systému.",
                IsActive = true
            },
            new()
            {
                Code = "DMS_TECHNOLOGIE",
                Name = "Technologie",
                Description = "Přístup k technologickým, SAP a artikelovým datům.",
                IsActive = true
            },
            new()
            {
                Code = "DMS_READONLY",
                Name = "Pouze čtení",
                Description = "Uživatel může zobrazovat dostupná data bez editace.",
                IsActive = true
            },
            new()
            {
                Code = "DMS_QUALITY_VIEW",
                Name = "Quality - čtení",
                Description = "Zobrazení quality vrstvy.",
                IsActive = true
            },
            new()
            {
                Code = "DMS_QUALITY_EDIT",
                Name = "Quality - editace",
                Description = "Editace quality vrstvy, tiskových verzí a úkolů.",
                IsActive = true
            },
            new()
            {
                Code = "DMS_PRODUCT_DEVELOPMENT",
                Name = "Product Development",
                Description = "Přístup k vývojové dokumentaci.",
                IsActive = true
            },
            new()
            {
                Code = "DMS_KVALITA",
                Name = "Kvalita",
                Description = "Starší role kvality, ponechaná kvůli kompatibilitě.",
                IsActive = true
            }
        };
    }
}