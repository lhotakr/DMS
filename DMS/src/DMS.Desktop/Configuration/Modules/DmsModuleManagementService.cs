using System.IO;
using System.Text.Json;

namespace DMS.Desktop.Configuration.Modules;

public sealed class DmsModuleManagementService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _modulesPath;

    public DmsModuleManagementService(string modulesPath)
    {
        _modulesPath = modulesPath;
    }

    public List<DmsModuleDefinition> LoadAll()
    {
        if (!File.Exists(_modulesPath))
        {
            var defaults = CreateDefaultModules();
            SaveAll(defaults);
            return defaults;
        }

        var json = File.ReadAllText(_modulesPath);

        var modules = JsonSerializer.Deserialize<List<DmsModuleDefinition>>(json, JsonOptions)
                      ?? new List<DmsModuleDefinition>();

        if (modules.Count == 0)
        {
            modules = CreateDefaultModules();
            SaveAll(modules);
        }

        return modules;
    }

    public void SaveAll(IEnumerable<DmsModuleDefinition> modules)
    {
        var normalized = modules
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .Select(x => new DmsModuleDefinition
            {
                Code = x.Code.Trim().ToUpperInvariant(),
                Name = x.Name.Trim(),
                Description = x.Description.Trim(),
                SortOrder = x.SortOrder,
                IsActive = x.IsActive
            })
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToList();

        var directory = Path.GetDirectoryName(_modulesPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(_modulesPath, json);
    }

    private static List<DmsModuleDefinition> CreateDefaultModules()
    {
        return new List<DmsModuleDefinition>
        {
            new()
            {
                Code = "ADMIN",
                Name = "Administrace",
                Description = "Správa systému, transakcí, rolí, uživatelů a logů.",
                SortOrder = 10,
                IsActive = true
            },
            new()
            {
                Code = "SAP",
                Name = "SAP",
                Description = "SAP mirror, importy, materiály, kusovníky a pracovní postupy.",
                SortOrder = 20,
                IsActive = true
            },
            new()
            {
                Code = "ARTICLES",
                Name = "Artikly",
                Description = "DMS artiklové karty a artikelová data.",
                SortOrder = 30,
                IsActive = true
            },
            new()
            {
                Code = "DOCUMENTS",
                Name = "Dokumenty",
                Description = "Dokumentace navázaná na SAP ID a kontexty.",
                SortOrder = 40,
                IsActive = true
            },
            new()
            {
                Code = "QUALITY",
                Name = "Quality",
                Description = "Quality vrstva, tiskové verze, úkoly a zákaznická data.",
                SortOrder = 50,
                IsActive = true
            },
            new()
            {
                Code = "TECHNOLOGY",
                Name = "Technologie",
                Description = "Technologické souhrny, dekorace a výrobní data.",
                SortOrder = 60,
                IsActive = true
            },
            new()
            {
                Code = "SCREEN_PRINTING",
                Name = "Sítotisk",
                Description = "Síta, příprava sít a sítotisková data.",
                SortOrder = 70,
                IsActive = true
            },
            new()
            {
                Code = "ORDERS",
                Name = "Zakázky",
                Description = "Přehledy zakázek a návazná data.",
                SortOrder = 80,
                IsActive = true
            }
        };
    }
}