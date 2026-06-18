using DMS.Desktop.Configuration.SystemSettings;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.SystemSettings;

public partial class SystemSettingsView : UserControl
{
    private readonly DmsSystemSettingsService _settingsService;
    private readonly ObservableCollection<DmsArticleSubFolderDefinition> _subFolders = new();
    private readonly ObservableCollection<DmsMaterialRangeDefinition> _materialRanges = new();
    private readonly string _sapMaterialsFilePath;

    public SystemSettingsView(
        string systemSettingsPath,
        string sapMaterialsFilePath)
    {
        InitializeComponent();

        _sapMaterialsFilePath = sapMaterialsFilePath;
        _settingsService = new DmsSystemSettingsService(systemSettingsPath);

        GridSubFolders.ItemsSource = _subFolders;
        GridMaterialRanges.ItemsSource = _materialRanges;

        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();

        TxtDocumentsRootPath.Text = settings.DocumentsRootPath;
        TxtArticleFoldersRootPath.Text = settings.ArticleFoldersRootPath;
        ChkCreateArticleFoldersOnSapImport.IsChecked = settings.CreateArticleFoldersOnSapImport;

        _subFolders.Clear();

        foreach (var folder in settings.ArticleSubFolders)
        {
            _subFolders.Add(folder);
        }

        _materialRanges.Clear();

        foreach (var range in settings.ArticleFolderMaterialRanges)
        {
            _materialRanges.Add(range);
        }

        TxtStatus.Text = "Nastavení načteno.";
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var validationMessage = ValidateSettings();

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            MessageBox.Show(
                validationMessage,
                "SYS01 - Kontrola nastavení",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var settings = BuildSettingsFromScreen();

        _settingsService.Save(settings);

        TxtStatus.Text = $"Nastavení uloženo: {DateTime.Now:dd.MM.yyyy HH:mm:ss}";

        MessageBox.Show(
            "Systémové nastavení DMS bylo uloženo.",
            "SYS01",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BtnTestPaths_Click(object sender, RoutedEventArgs e)
    {
        var documentsRoot = TxtDocumentsRootPath.Text.Trim();
        var articleRoot = TxtArticleFoldersRootPath.Text.Trim();

        var messages = new List<string>
        {
            Directory.Exists(documentsRoot)
                ? $"OK: Kořen dokumentů existuje: {documentsRoot}"
                : $"CHYBÍ: Kořen dokumentů neexistuje: {documentsRoot}",

            Directory.Exists(articleRoot)
                ? $"OK: Kořen složek SAP ID / artiklů existuje: {articleRoot}"
                : $"CHYBÍ: Kořen složek SAP ID / artiklů neexistuje: {articleRoot}"
        };

        if (File.Exists(_sapMaterialsFilePath))
        {
            messages.Add($"OK: SAP cache nalezena: {_sapMaterialsFilePath}");
        }
        else
        {
            messages.Add($"CHYBÍ: SAP cache nenalezena: {_sapMaterialsFilePath}");
        }

        TxtStatus.Text = string.Join(Environment.NewLine, messages);

        MessageBox.Show(
            TxtStatus.Text,
            "SYS01 - Test cest",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BtnGenerateFolders_Click(object sender, RoutedEventArgs e)
    {
        var validationMessage = ValidateSettings();

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            MessageBox.Show(
                validationMessage,
                "SYS01 - Kontrola nastavení",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (!File.Exists(_sapMaterialsFilePath))
        {
            MessageBox.Show(
                $"Soubor SAP materiálů nebyl nalezen:\n\n{_sapMaterialsFilePath}",
                "SYS01 - Generování složek",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var settings = BuildSettingsFromScreen();

        var allSapIds = LoadSapIdsFromCache(_sapMaterialsFilePath);

        var sapIds = allSapIds
            .Where(x => IsSapIdAllowedByRanges(x, settings.ArticleFolderMaterialRanges))
            .ToList();

        if (sapIds.Count == 0)
        {
            MessageBox.Show(
                "V SAP cache nebylo nalezeno žádné SAP ID odpovídající aktivním rozsahům v SYS01.",
                "SYS01 - Generování složek",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var activeSubFolders = settings.ArticleSubFolders
            .Where(x => x.IsActive)
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .Where(x => !string.IsNullOrWhiteSpace(x.RelativePath))
            .ToList();

        if (activeSubFolders.Count == 0)
        {
            MessageBox.Show(
                "V nastavení není žádná aktivní podsložka. Přidej například QA nebo PD.",
                "SYS01 - Generování složek",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var activeRangesText = settings.ArticleFolderMaterialRanges
            .Where(x => x.IsActive)
            .Select(x => $"{x.Name}: {x.From}–{x.To}")
            .ToList();

        var confirm = MessageBox.Show(
            "Chceš založit chybějící složky pro SAP ID podle aktivních rozsahů?\n\n" +
            $"SAP ID v cache celkem: {allSapIds.Count}\n" +
            $"SAP ID po filtru: {sapIds.Count}\n\n" +
            "Aktivní rozsahy:\n" +
            string.Join(Environment.NewLine, activeRangesText) +
            "\n\nExistující složky zůstanou beze změny.",
            "SYS01 - Generování složek",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _settingsService.Save(settings);

            var createdArticleFolders = 0;
            var existingArticleFolders = 0;
            var createdSubFolders = 0;
            var existingSubFolders = 0;
            var errors = new List<string>();

            foreach (var sapId in sapIds)
            {
                try
                {
                    var articleFolderPath = Path.Combine(
                        settings.ArticleFoldersRootPath,
                        sapId);

                    if (Directory.Exists(articleFolderPath))
                    {
                        existingArticleFolders++;
                    }
                    else
                    {
                        Directory.CreateDirectory(articleFolderPath);
                        createdArticleFolders++;
                    }

                    foreach (var subFolder in activeSubFolders)
                    {
                        var safeRelativePath = NormalizeRelativePath(subFolder.RelativePath);

                        var subFolderPath = Path.Combine(
                            articleFolderPath,
                            safeRelativePath);

                        if (Directory.Exists(subFolderPath))
                        {
                            existingSubFolders++;
                        }
                        else
                        {
                            Directory.CreateDirectory(subFolderPath);
                            createdSubFolders++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{sapId}: {ex.Message}");
                }
            }

            var message =
                $"Hotovo.\n\n" +
                $"SAP ID v cache celkem: {allSapIds.Count}\n" +
                $"SAP ID po filtru: {sapIds.Count}\n" +
                $"Nové složky SAP ID / artiklů: {createdArticleFolders}\n" +
                $"Existující složky SAP ID / artiklů: {existingArticleFolders}\n" +
                $"Nové podsložky: {createdSubFolders}\n" +
                $"Existující podsložky: {existingSubFolders}\n" +
                $"Chyby: {errors.Count}";

            TxtStatus.Text = message;

            if (errors.Count > 0)
            {
                TxtStatus.Text += Environment.NewLine +
                                  Environment.NewLine +
                                  "Prvních 20 chyb:" +
                                  Environment.NewLine +
                                  string.Join(Environment.NewLine, errors.Take(20));
            }

            MessageBox.Show(
                message,
                "SYS01 - Generování složek",
                MessageBoxButton.OK,
                errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Generování složek selhalo:\n\n{ex.Message}",
                "SYS01 - Generování složek",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private DmsSystemSettings BuildSettingsFromScreen()
    {
        return new DmsSystemSettings
        {
            DocumentsRootPath = TxtDocumentsRootPath.Text.Trim(),
            ArticleFoldersRootPath = TxtArticleFoldersRootPath.Text.Trim(),
            CreateArticleFoldersOnSapImport = ChkCreateArticleFoldersOnSapImport.IsChecked == true,

            ArticleSubFolders = _subFolders
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Code) ||
                    !string.IsNullOrWhiteSpace(x.Name) ||
                    !string.IsNullOrWhiteSpace(x.RelativePath))
                .Select(x => new DmsArticleSubFolderDefinition
                {
                    Code = x.Code.Trim().ToUpperInvariant(),
                    Name = x.Name.Trim(),
                    RelativePath = x.RelativePath.Trim(),
                    IsActive = x.IsActive
                })
                .ToList(),

            ArticleFolderMaterialRanges = _materialRanges
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Name) ||
                    x.From != 0 ||
                    x.To != 0)
                .Select(x => new DmsMaterialRangeDefinition
                {
                    Name = x.Name.Trim(),
                    From = x.From,
                    To = x.To,
                    IsActive = x.IsActive
                })
                .ToList()
        };
    }

    private string? ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(TxtDocumentsRootPath.Text))
        {
            return "Kořen dokumentů nesmí být prázdný.";
        }

        if (string.IsNullOrWhiteSpace(TxtArticleFoldersRootPath.Text))
        {
            return "Kořen složek SAP ID / artiklů nesmí být prázdný.";
        }

        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in _subFolders)
        {
            if (string.IsNullOrWhiteSpace(folder.Code) &&
                string.IsNullOrWhiteSpace(folder.Name) &&
                string.IsNullOrWhiteSpace(folder.RelativePath))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(folder.Code))
            {
                return "Každá podsložka musí mít vyplněný kód.";
            }

            if (folder.Code.Any(char.IsWhiteSpace))
            {
                return $"Kód podsložky nesmí obsahovat mezery: {folder.Code}";
            }

            if (string.IsNullOrWhiteSpace(folder.RelativePath))
            {
                return $"Podsložka {folder.Code} musí mít vyplněnou relativní cestu.";
            }

            if (folder.RelativePath.Contains(".."))
            {
                return $"Podsložka {folder.Code} nesmí obsahovat '..' v relativní cestě.";
            }

            if (Path.IsPathRooted(folder.RelativePath))
            {
                return $"Podsložka {folder.Code} musí mít relativní cestu, ne absolutní.";
            }

            if (!usedCodes.Add(folder.Code.Trim()))
            {
                return $"Duplicitní kód podsložky: {folder.Code}";
            }
        }

        foreach (var range in _materialRanges)
        {
            if (string.IsNullOrWhiteSpace(range.Name) &&
                range.From == 0 &&
                range.To == 0)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(range.Name))
            {
                return "Každý rozsah SAP ID musí mít vyplněný název.";
            }

            if (range.From <= 0 || range.To <= 0)
            {
                return $"Rozsah {range.Name} musí mít vyplněné hodnoty Od a Do.";
            }

            if (range.From > range.To)
            {
                return $"Rozsah {range.Name} má hodnotu Od větší než Do.";
            }
        }

        return null;
    }

    private static List<string> LoadSapIdsFromCache(string sapMaterialsFilePath)
    {
        var json = File.ReadAllText(sapMaterialsFilePath);

        using var document = JsonDocument.Parse(json);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in document.RootElement.EnumerateArray())
            {
                TryAddSapIdFromJsonObject(item, result);
            }
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            TryReadArrayProperty(document.RootElement, "materials", result);
            TryReadArrayProperty(document.RootElement, "Materials", result);
            TryReadArrayProperty(document.RootElement, "items", result);
            TryReadArrayProperty(document.RootElement, "Items", result);
        }

        return result
            .Where(IsSapId)
            .OrderBy(x => x)
            .ToList();
    }

    private static void TryReadArrayProperty(
        JsonElement root,
        string propertyName,
        HashSet<string> result)
    {
        if (!root.TryGetProperty(propertyName, out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in array.EnumerateArray())
        {
            TryAddSapIdFromJsonObject(item, result);
        }
    }

    private static void TryAddSapIdFromJsonObject(
        JsonElement item,
        HashSet<string> result)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var candidateProperties = new[]
        {
            "MaterialNumber",
            "SapId",
            "SapID",
            "SAPID",
            "MATNR",
            "Matnr",
            "matnr",
            "Material",
            "ArticleNumber",
            "ArticleId",
            "ArticleID",
            "SapMaterialNumber"
        };

        foreach (var propertyName in candidateProperties)
        {
            if (!item.TryGetProperty(propertyName, out var property))
            {
                continue;
            }

            var value = property.ValueKind switch
            {
                JsonValueKind.String => property.GetString()?.Trim(),
                JsonValueKind.Number => property.GetRawText().Trim(),
                _ => null
            };

            if (IsSapId(value))
            {
                result.Add(value!);
                return;
            }
        }
    }

    private static bool IsSapId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.Length == 10
               && value.All(char.IsDigit);
    }

    private static bool IsSapIdAllowedByRanges(
        string sapId,
        IEnumerable<DmsMaterialRangeDefinition> ranges)
    {
        if (!long.TryParse(sapId, out var number))
        {
            return false;
        }

        return ranges
            .Where(x => x.IsActive)
            .Any(x => number >= x.From && number <= x.To);
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath
            .Trim()
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
    }
}