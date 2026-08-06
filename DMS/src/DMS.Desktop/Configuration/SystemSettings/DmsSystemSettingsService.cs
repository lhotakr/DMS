using System.IO;
using System.Text.Json;

namespace DMS.Desktop.Configuration.SystemSettings;

public sealed class DmsSystemSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public DmsSystemSettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public DmsSystemSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            var defaults = CreateDefault();
            Save(defaults);
            return defaults;
        }

        var json = File.ReadAllText(_settingsPath);

        return JsonSerializer.Deserialize<DmsSystemSettings>(json, JsonOptions)
               ?? CreateDefault();
    }

    public void Save(DmsSystemSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private static DmsSystemSettings CreateDefault()
    {

        return new DmsSystemSettings
        {
            DocumentsRootPath = @"Z:\SAP\DMS-db\DEV\Documents",
            ArticleFoldersRootPath = @"Z:\SAP\DMS-db\DEV\Documents\Articles",
            CreateArticleFoldersOnSapImport = true,

            HeaderSecondaryLogoPath = string.Empty,
            HeaderSecondaryLogoMaxWidth = 360,
            HeaderSecondaryLogoMaxHeight = 70,

            ArticleFolderMaterialRanges =
{
    new DmsMaterialRangeDefinition
    {
        Name = "Artikly / flakony",
        From = 1000000000,
        To = 1099999999,
        IsActive = true
    },
    new DmsMaterialRangeDefinition
    {
        Name = "Nakupované díly a materiály",
        From = 1100000000,
        To = 1199999999,
        IsActive = false
    },
    new DmsMaterialRangeDefinition
    {
        Name = "Obalové materiály",
        From = 1300000000,
        To = 1399999999,
        IsActive = false
    },
    new DmsMaterialRangeDefinition
    {
        Name = "Receptury",
        From = 1700000000,
        To = 1799999999,
        IsActive = false
    },
    new DmsMaterialRangeDefinition
    {
        Name = "Nakupované komponenty 21*",
        From = 2100000000,
        To = 2199999999,
        IsActive = false
    },
    new DmsMaterialRangeDefinition
    {
        Name = "Přípravky / nástroje",
        From = 2300000000,
        To = 2399999999,
        IsActive = false
    }
},
            ArticleSubFolders =
            {
                new DmsArticleSubFolderDefinition
                {
                    Code = "QA",
                    Name = "Quality",
                    RelativePath = "QA",
                    IsActive = true
                },
                new DmsArticleSubFolderDefinition
                {
                    Code = "PD",
                    Name = "Product Development",
                    RelativePath = "PD",
                    IsActive = true
                }
            }
        };
    }
}