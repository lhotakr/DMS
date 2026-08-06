using DMS.Desktop.Configuration.SystemSettings;
using DMS.Desktop.UI;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DMS.Desktop.Views.SystemSettings;

public partial class SystemSettingsView : UserControl
{
    private readonly DmsSystemSettingsService _settingsService;
    private readonly ObservableCollection<DmsArticleSubFolderDefinition> _subFolders = new();
    private readonly ObservableCollection<DmsMaterialRangeDefinition> _materialRanges = new();
    private readonly string _sapMaterialsFilePath;
    private readonly string _systemSettingsPath;
    private readonly string _configurationRootPath;
    private readonly Action<DmsSystemSettings>? _afterSave;
    private readonly Func<string, string>? _translate;
    private readonly Action<string, string>? _logSystemSettingsAction;

    public SystemSettingsView(
        string systemSettingsPath,
        string sapMaterialsFilePath,
        Action<DmsSystemSettings>? afterSave = null,
        Func<string, string>? translate = null,
        Action<string, string>? logSystemSettingsAction = null)
    {
        InitializeComponent();

        _systemSettingsPath = systemSettingsPath;
        _configurationRootPath = Path.GetDirectoryName(systemSettingsPath) ?? AppContext.BaseDirectory;
        _sapMaterialsFilePath = sapMaterialsFilePath;
        _afterSave = afterSave;
        _translate = translate;
        _logSystemSettingsAction = logSystemSettingsAction;
        _settingsService = new DmsSystemSettingsService(systemSettingsPath);

        GridSubFolders.ItemsSource = _subFolders;
        GridMaterialRanges.ItemsSource = _materialRanges;

        ApplyLocalization();
        LoadSettings();
    }

    private string T(string key)
    {
        var translated = _translate?.Invoke(key);

        if (!string.IsNullOrWhiteSpace(translated) &&
            !string.Equals(translated, key, StringComparison.OrdinalIgnoreCase) &&
            !translated.StartsWith("[[", StringComparison.Ordinal))
        {
            return translated;
        }

        return FallbackTranslations.TryGetValue(key, out var fallback)
            ? fallback
            : key;
    }

    private string T(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, T(key), args);
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("SYS01.System.Title");
        TxtDocumentStorageTitle.Text = T("SYS01.System.DocumentStorage");
        TxtDocumentsRootPathLabel.Text = T("SYS01.System.DocumentsRootPath");
        TxtArticleFoldersRootPathLabel.Text = T("SYS01.System.ArticleFoldersRootPath");
        ChkCreateArticleFoldersOnSapImport.Content = T("SYS01.System.CreateFoldersOnSapImport");

        TxtSubFoldersTitle.Text = T("SYS01.System.SubFolders");
        TxtMaterialRangesTitle.Text = T("SYS01.System.MaterialRanges");
        TxtMaterialRangesHelp.Text = T("SYS01.System.MaterialRangesHelp");

        TxtBrandingTitle.Text = T("SYS01.System.Branding");
        TxtBrandingHelp.Text = T("SYS01.System.BrandingHelp");
        TxtHeaderSecondaryLogoPathLabel.Text = T("SYS01.System.LogoPath");
        TxtHeaderSecondaryLogoMaxWidthLabel.Text = T("SYS01.System.LogoMaxWidth");
        TxtHeaderSecondaryLogoMaxHeightLabel.Text = T("SYS01.System.LogoMaxHeight");

        BtnBrowseHeaderLogo.Content = T("Common.Browse");
        BtnGenerateFolders.Content = T("SYS01.System.GenerateFolders");
        BtnTestPaths.Content = T("SYS01.System.TestPaths");
        BtnSave.Content = T("Common.Save");

        ApplyGridHeaders();
    }

    private void ApplyGridHeaders()
    {
        if (GridSubFolders.Columns.Count >= 4)
        {
            GridSubFolders.Columns[0].Header = T("SYS01.System.SubFolders.Code");
            GridSubFolders.Columns[1].Header = T("SYS01.System.SubFolders.Name");
            GridSubFolders.Columns[2].Header = T("SYS01.System.SubFolders.RelativePath");
            GridSubFolders.Columns[3].Header = T("Common.Active");
        }

        if (GridMaterialRanges.Columns.Count >= 4)
        {
            GridMaterialRanges.Columns[0].Header = T("SYS01.System.MaterialRanges.Name");
            GridMaterialRanges.Columns[1].Header = T("SYS01.System.MaterialRanges.From");
            GridMaterialRanges.Columns[2].Header = T("SYS01.System.MaterialRanges.To");
            GridMaterialRanges.Columns[3].Header = T("Common.Active");
        }
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();

        TxtDocumentsRootPath.Text = settings.DocumentsRootPath;
        TxtArticleFoldersRootPath.Text = settings.ArticleFoldersRootPath;
        ChkCreateArticleFoldersOnSapImport.IsChecked = settings.CreateArticleFoldersOnSapImport;

        TxtHeaderSecondaryLogoPath.Text = settings.HeaderSecondaryLogoPath;
        TxtHeaderSecondaryLogoMaxWidth.Text = settings.HeaderSecondaryLogoMaxWidth.ToString(CultureInfo.InvariantCulture);
        TxtHeaderSecondaryLogoMaxHeight.Text = settings.HeaderSecondaryLogoMaxHeight.ToString(CultureInfo.InvariantCulture);

        UpdateHeaderLogoPreview(settings.HeaderSecondaryLogoPath);

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

        TxtStatus.Text = T("SYS01.System.StatusLoaded");
        LogSystemSettingsAction("LoadSystemSettings", BuildLogDetails(settings));
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var validationMessage = ValidateSettings();

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            ShowWarning(T("SYS01.System.ValidationTitle"), validationMessage);

            LogSystemSettingsAction(
                "SaveSystemSettingsFailed",
                $"Validation failed: {validationMessage}");

            return;
        }

        var settings = BuildSettingsFromScreen();

        _settingsService.Save(settings);
        _afterSave?.Invoke(settings);

        TxtStatus.Text = T("SYS01.System.StatusSaved", DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture));

        LogSystemSettingsAction("SaveSystemSettings", BuildLogDetails(settings));

        ShowInfo(
            T("SYS01.System.SavedTitle"),
            T("SYS01.System.SavedMessage"));
    }

    private void BtnTestPaths_Click(object sender, RoutedEventArgs e)
    {
        var documentsRoot = TxtDocumentsRootPath.Text.Trim();
        var articleRoot = TxtArticleFoldersRootPath.Text.Trim();

        var messages = new List<string>
        {
            Directory.Exists(documentsRoot)
                ? T("SYS01.System.PathOk.DocumentsRoot", documentsRoot)
                : T("SYS01.System.PathMissing.DocumentsRoot", documentsRoot),

            Directory.Exists(articleRoot)
                ? T("SYS01.System.PathOk.ArticleRoot", articleRoot)
                : T("SYS01.System.PathMissing.ArticleRoot", articleRoot)
        };

        if (File.Exists(_sapMaterialsFilePath))
        {
            messages.Add(T("SYS01.System.PathOk.SapCache", _sapMaterialsFilePath));
        }
        else
        {
            messages.Add(T("SYS01.System.PathMissing.SapCache", _sapMaterialsFilePath));
        }

        TxtStatus.Text = string.Join(Environment.NewLine, messages);

        LogSystemSettingsAction(
            "TestSystemPaths",
            $"DocumentsRoot={documentsRoot}; ArticleRoot={articleRoot}; SapCache={_sapMaterialsFilePath}");

        ShowInfo(
            T("SYS01.System.TestPathsTitle"),
            TxtStatus.Text);
    }

    private void BtnGenerateFolders_Click(object sender, RoutedEventArgs e)
    {
        var validationMessage = ValidateSettings();

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            ShowWarning(T("SYS01.System.ValidationTitle"), validationMessage);

            LogSystemSettingsAction(
                "GenerateFoldersFailed",
                $"Validation failed: {validationMessage}");

            return;
        }

        if (!File.Exists(_sapMaterialsFilePath))
        {
            var message = T("SYS01.System.SapCacheMissing", _sapMaterialsFilePath);

            ShowWarning(
                T("SYS01.System.GenerateFoldersTitle"),
                message);

            LogSystemSettingsAction(
                "GenerateFoldersFailed",
                message);

            return;
        }

        var settings = BuildSettingsFromScreen();

        var allSapIds = LoadSapIdsFromCache(_sapMaterialsFilePath);

        var sapIds = allSapIds
            .Where(x => IsSapIdAllowedByRanges(x, settings.ArticleFolderMaterialRanges))
            .ToList();

        if (sapIds.Count == 0)
        {
            var message = T("SYS01.System.NoSapIdsForRanges");

            ShowWarning(
                T("SYS01.System.GenerateFoldersTitle"),
                message);

            LogSystemSettingsAction(
                "GenerateFoldersFailed",
                message);

            return;
        }

        var activeSubFolders = settings.ArticleSubFolders
            .Where(x => x.IsActive)
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .Where(x => !string.IsNullOrWhiteSpace(x.RelativePath))
            .ToList();

        if (activeSubFolders.Count == 0)
        {
            var message = T("SYS01.System.NoActiveSubFolders");

            ShowWarning(
                T("SYS01.System.GenerateFoldersTitle"),
                message);

            LogSystemSettingsAction(
                "GenerateFoldersFailed",
                message);

            return;
        }

        var activeRangesText = settings.ArticleFolderMaterialRanges
            .Where(x => x.IsActive)
            .Select(x => $"{x.Name}: {x.From}–{x.To}")
            .ToList();

        var confirmMessage =
            T("SYS01.System.GenerateFoldersConfirmIntro") +
            Environment.NewLine +
            Environment.NewLine +
            T("SYS01.System.SapIdsTotal", allSapIds.Count) +
            Environment.NewLine +
            T("SYS01.System.SapIdsFiltered", sapIds.Count) +
            Environment.NewLine +
            Environment.NewLine +
            T("SYS01.System.ActiveRanges") +
            Environment.NewLine +
            string.Join(Environment.NewLine, activeRangesText) +
            Environment.NewLine +
            Environment.NewLine +
            T("SYS01.System.ExistingFoldersRemain");

        var confirm = ShowQuestion(
            T("SYS01.System.GenerateFoldersTitle"),
            confirmMessage);

        if (!confirm)
        {
            LogSystemSettingsAction(
                "GenerateFoldersCancelled",
                $"SapIdsTotal={allSapIds.Count}; SapIdsFiltered={sapIds.Count}");

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
                T("SYS01.System.GenerateFoldersDone") +
                Environment.NewLine +
                Environment.NewLine +
                T("SYS01.System.SapIdsTotal", allSapIds.Count) +
                Environment.NewLine +
                T("SYS01.System.SapIdsFiltered", sapIds.Count) +
                Environment.NewLine +
                T("SYS01.System.CreatedArticleFolders", createdArticleFolders) +
                Environment.NewLine +
                T("SYS01.System.ExistingArticleFolders", existingArticleFolders) +
                Environment.NewLine +
                T("SYS01.System.CreatedSubFolders", createdSubFolders) +
                Environment.NewLine +
                T("SYS01.System.ExistingSubFolders", existingSubFolders) +
                Environment.NewLine +
                T("SYS01.System.Errors", errors.Count);

            TxtStatus.Text = message;

            if (errors.Count > 0)
            {
                TxtStatus.Text += Environment.NewLine +
                                  Environment.NewLine +
                                  T("SYS01.System.FirstErrors") +
                                  Environment.NewLine +
                                  string.Join(Environment.NewLine, errors.Take(20));
            }

            LogSystemSettingsAction(
                errors.Count == 0 ? "GenerateFolders" : "GenerateFoldersWithErrors",
                $"SapIdsTotal={allSapIds.Count}; SapIdsFiltered={sapIds.Count}; CreatedArticleFolders={createdArticleFolders}; ExistingArticleFolders={existingArticleFolders}; CreatedSubFolders={createdSubFolders}; ExistingSubFolders={existingSubFolders}; Errors={errors.Count}");

            if (errors.Count == 0)
            {
                ShowInfo(T("SYS01.System.GenerateFoldersTitle"), message);
            }
            else
            {
                ShowWarning(T("SYS01.System.GenerateFoldersTitle"), TxtStatus.Text);
            }
        }
        catch (Exception ex)
        {
            LogSystemSettingsAction(
                "GenerateFoldersFailed",
                ex.Message);

            ShowWarning(
                T("SYS01.System.GenerateFoldersTitle"),
                T("SYS01.System.GenerateFoldersFailed", ex.Message));
        }
    }

    private DmsSystemSettings BuildSettingsFromScreen()
    {
        return new DmsSystemSettings
        {
            DocumentsRootPath = TxtDocumentsRootPath.Text.Trim(),
            ArticleFoldersRootPath = TxtArticleFoldersRootPath.Text.Trim(),
            CreateArticleFoldersOnSapImport = ChkCreateArticleFoldersOnSapImport.IsChecked == true,

            HeaderSecondaryLogoPath = TxtHeaderSecondaryLogoPath.Text.Trim(),
            HeaderSecondaryLogoMaxWidth = ParsePositiveDouble(TxtHeaderSecondaryLogoMaxWidth.Text, 360),
            HeaderSecondaryLogoMaxHeight = ParsePositiveDouble(TxtHeaderSecondaryLogoMaxHeight.Text, 70),

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
            return T("SYS01.System.Validation.DocumentsRootRequired");
        }

        if (string.IsNullOrWhiteSpace(TxtArticleFoldersRootPath.Text))
        {
            return T("SYS01.System.Validation.ArticleRootRequired");
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
                return T("SYS01.System.Validation.SubFolderCodeRequired");
            }

            if (folder.Code.Any(char.IsWhiteSpace))
            {
                return T("SYS01.System.Validation.SubFolderCodeNoSpaces", folder.Code);
            }

            if (string.IsNullOrWhiteSpace(folder.RelativePath))
            {
                return T("SYS01.System.Validation.SubFolderRelativePathRequired", folder.Code);
            }

            if (folder.RelativePath.Contains(".."))
            {
                return T("SYS01.System.Validation.SubFolderRelativePathNoParent", folder.Code);
            }

            if (Path.IsPathRooted(folder.RelativePath))
            {
                return T("SYS01.System.Validation.SubFolderRelativePathNotAbsolute", folder.Code);
            }

            if (!usedCodes.Add(folder.Code.Trim()))
            {
                return T("SYS01.System.Validation.SubFolderDuplicateCode", folder.Code);
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
                return T("SYS01.System.Validation.RangeNameRequired");
            }

            if (range.From <= 0 || range.To <= 0)
            {
                return T("SYS01.System.Validation.RangeValuesRequired", range.Name);
            }

            if (range.From > range.To)
            {
                return T("SYS01.System.Validation.RangeFromGreaterThanTo", range.Name);
            }
        }

        var logoMaxWidth = ParsePositiveDouble(TxtHeaderSecondaryLogoMaxWidth.Text, 0);
        var logoMaxHeight = ParsePositiveDouble(TxtHeaderSecondaryLogoMaxHeight.Text, 0);

        if (logoMaxWidth <= 0)
        {
            return T("SYS01.System.Validation.LogoMaxWidthPositive");
        }

        if (logoMaxHeight <= 0)
        {
            return T("SYS01.System.Validation.LogoMaxHeightPositive");
        }

        var logoPath = TxtHeaderSecondaryLogoPath.Text.Trim();

        if (!string.IsNullOrWhiteSpace(logoPath) && !File.Exists(logoPath))
        {
            return T("SYS01.System.Validation.LogoFileMissing", logoPath);
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

    private void BtnBrowseHeaderLogo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = T("SYS01.System.SelectLogoDialogTitle"),
            Filter = T("SYS01.System.SelectLogoFilter")
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var maxWidth = (int)ParsePositiveDouble(TxtHeaderSecondaryLogoMaxWidth.Text, 360);
        var maxHeight = (int)ParsePositiveDouble(TxtHeaderSecondaryLogoMaxHeight.Text, 70);

        var brandingFolder = Path.Combine(
            _configurationRootPath,
            "Branding");

        Directory.CreateDirectory(brandingFolder);

        var destinationPath = Path.Combine(
            brandingFolder,
            "header-secondary-logo.png");

        try
        {
            SaveResizedImageAsPng(
                dialog.FileName,
                destinationPath,
                maxWidth,
                maxHeight);

            TxtHeaderSecondaryLogoPath.Text = destinationPath;

            UpdateHeaderLogoPreview(destinationPath);

            TxtStatus.Text = T("SYS01.System.LogoUploadedStatus", destinationPath);

            LogSystemSettingsAction(
                "UploadHeaderLogo",
                $"Source={dialog.FileName}; Destination={destinationPath}; MaxWidth={maxWidth}; MaxHeight={maxHeight}");
        }
        catch (Exception ex)
        {
            LogSystemSettingsAction(
                "UploadHeaderLogoFailed",
                ex.Message);

            ShowWarning(
                T("SYS01.System.BrandingTitle"),
                T("SYS01.System.LogoUploadFailed", ex.Message));
        }
    }

    private static double ParsePositiveDouble(
        string? text,
        double fallback)
    {
        if (double.TryParse(
                text,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var invariantValue) &&
            invariantValue > 0)
        {
            return invariantValue;
        }

        if (double.TryParse(
                text,
                NumberStyles.Any,
                CultureInfo.GetCultureInfo("cs-CZ"),
                out var czValue) &&
            czValue > 0)
        {
            return czValue;
        }

        return fallback;
    }

    private void UpdateHeaderLogoPreview(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ImgHeaderSecondaryLogoPreview.Source = null;
            ImgHeaderSecondaryLogoPreview.Visibility = Visibility.Collapsed;
            return;
        }

        var maxWidth = ParsePositiveDouble(TxtHeaderSecondaryLogoMaxWidth.Text, 360);
        var maxHeight = ParsePositiveDouble(TxtHeaderSecondaryLogoMaxHeight.Text, 70);

        ImgHeaderSecondaryLogoPreview.MaxWidth = maxWidth;
        ImgHeaderSecondaryLogoPreview.MaxHeight = maxHeight;
        ImgHeaderSecondaryLogoPreview.Source = LoadBitmap(path, (int)maxWidth, (int)maxHeight);
        ImgHeaderSecondaryLogoPreview.Visibility = Visibility.Visible;
    }

    private static BitmapImage LoadBitmap(
        string path,
        int maxWidth,
        int maxHeight)
    {
        var bitmap = new BitmapImage();

        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);

        if (maxWidth > 0)
        {
            bitmap.DecodePixelWidth = maxWidth;
        }

        if (maxHeight > 0)
        {
            bitmap.DecodePixelHeight = maxHeight;
        }

        bitmap.EndInit();
        bitmap.Freeze();

        return bitmap;
    }

    private static void SaveResizedImageAsPng(
        string sourcePath,
        string destinationPath,
        int maxWidth,
        int maxHeight)
    {
        var original = new BitmapImage();

        original.BeginInit();
        original.CacheOption = BitmapCacheOption.OnLoad;
        original.UriSource = new Uri(sourcePath, UriKind.Absolute);
        original.EndInit();
        original.Freeze();

        var scale = Math.Min(
            (double)maxWidth / original.PixelWidth,
            (double)maxHeight / original.PixelHeight);

        if (scale <= 0)
        {
            scale = 1.0;
        }

        if (scale > 1.0)
        {
            scale = 1.0;
        }

        var transformed = new TransformedBitmap(
            original,
            new ScaleTransform(scale, scale));

        transformed.Freeze();

        var directory = Path.GetDirectoryName(destinationPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(transformed));

        using var stream = File.Create(destinationPath);
        encoder.Save(stream);
    }

    private void ShowInfo(string title, string message)
    {
        DmsConfirmDialog.Show(
            Window.GetWindow(this),
            title,
            message,
            DmsDialogButtons.Ok);
    }

    private void ShowWarning(string title, string message)
    {
        DmsConfirmDialog.Show(
            Window.GetWindow(this),
            title,
            message,
            DmsDialogButtons.Ok);
    }

    private bool ShowQuestion(string title, string message)
    {
        return DmsConfirmDialog.Show(
            Window.GetWindow(this),
            title,
            message,
            DmsDialogButtons.YesNo) == MessageBoxResult.Yes;
    }

    private void LogSystemSettingsAction(string action, string details)
    {
        _logSystemSettingsAction?.Invoke(action, details);
    }

    private static string BuildLogDetails(DmsSystemSettings settings)
    {
        return
            $"DocumentsRootPath={settings.DocumentsRootPath}; " +
            $"ArticleFoldersRootPath={settings.ArticleFoldersRootPath}; " +
            $"CreateArticleFoldersOnSapImport={settings.CreateArticleFoldersOnSapImport}; " +
            $"SubFolders={settings.ArticleSubFolders.Count}; " +
            $"MaterialRanges={settings.ArticleFolderMaterialRanges.Count}; " +
            $"HeaderSecondaryLogoPath={settings.HeaderSecondaryLogoPath}; " +
            $"HeaderSecondaryLogoMaxWidth={settings.HeaderSecondaryLogoMaxWidth}; " +
            $"HeaderSecondaryLogoMaxHeight={settings.HeaderSecondaryLogoMaxHeight}";
    }

    private static readonly Dictionary<string, string> FallbackTranslations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Common.Active"] = "Aktivní",
        ["Common.Browse"] = "Vybrat...",
        ["Common.Save"] = "Uložit",

        ["SYS01.System.Title"] = "SYS01 - Nastavení systému DMS",
        ["SYS01.System.TabSystem"] = "Systém",
        ["SYS01.System.TabLocalization"] = "Lokalizace",
        ["SYS01.System.DocumentStorage"] = "Dokumentové úložiště",
        ["SYS01.System.DocumentsRootPath"] = "Kořen dokumentů",
        ["SYS01.System.ArticleFoldersRootPath"] = "Kořen složek SAP ID / artiklů",
        ["SYS01.System.CreateFoldersOnSapImport"] = "Zakládat složky automaticky při SAP importu",
        ["SYS01.System.SubFolders"] = "Podsložky artiklu",
        ["SYS01.System.SubFolders.Code"] = "Kód",
        ["SYS01.System.SubFolders.Name"] = "Název",
        ["SYS01.System.SubFolders.RelativePath"] = "Relativní cesta",
        ["SYS01.System.MaterialRanges"] = "Rozsahy SAP ID pro zakládání složek",
        ["SYS01.System.MaterialRangesHelp"] = "Aktivní rozsahy určují, pro které SAP ID se budou zakládat složky. Výchozí doporučení je pouze rozsah 1000000000–1099999999 pro hlavní artikly / flakony.",
        ["SYS01.System.MaterialRanges.Name"] = "Název",
        ["SYS01.System.MaterialRanges.From"] = "Od",
        ["SYS01.System.MaterialRanges.To"] = "Do",
        ["SYS01.System.Branding"] = "Branding záhlaví",
        ["SYS01.System.BrandingTitle"] = "SYS01 - Branding",
        ["SYS01.System.BrandingHelp"] = "Doplňkové logo v záhlaví se automaticky zmenší podle zadané maximální velikosti. Obrázek se uloží jako optimalizovaná kopie do Config\\Branding.",
        ["SYS01.System.LogoPath"] = "Cesta k logu",
        ["SYS01.System.LogoMaxWidth"] = "Max. šířka",
        ["SYS01.System.LogoMaxHeight"] = "Max. výška",
        ["SYS01.System.GenerateFolders"] = "Vygenerovat složky",
        ["SYS01.System.TestPaths"] = "Test cest",

        ["SYS01.System.StatusLoaded"] = "Nastavení načteno.",
        ["SYS01.System.StatusSaved"] = "Nastavení uloženo: {0}",
        ["SYS01.System.ValidationTitle"] = "SYS01 - Kontrola nastavení",
        ["SYS01.System.SavedTitle"] = "SYS01",
        ["SYS01.System.SavedMessage"] = "Systémové nastavení DMS bylo uloženo.",
        ["SYS01.System.TestPathsTitle"] = "SYS01 - Test cest",
        ["SYS01.System.GenerateFoldersTitle"] = "SYS01 - Generování složek",

        ["SYS01.System.PathOk.DocumentsRoot"] = "OK: Kořen dokumentů existuje: {0}",
        ["SYS01.System.PathMissing.DocumentsRoot"] = "CHYBÍ: Kořen dokumentů neexistuje: {0}",
        ["SYS01.System.PathOk.ArticleRoot"] = "OK: Kořen složek SAP ID / artiklů existuje: {0}",
        ["SYS01.System.PathMissing.ArticleRoot"] = "CHYBÍ: Kořen složek SAP ID / artiklů neexistuje: {0}",
        ["SYS01.System.PathOk.SapCache"] = "OK: SAP cache nalezena: {0}",
        ["SYS01.System.PathMissing.SapCache"] = "CHYBÍ: SAP cache nenalezena: {0}",

        ["SYS01.System.SapCacheMissing"] = "Soubor SAP materiálů nebyl nalezen:\n\n{0}",
        ["SYS01.System.NoSapIdsForRanges"] = "V SAP cache nebylo nalezeno žádné SAP ID odpovídající aktivním rozsahům v SYS01.",
        ["SYS01.System.NoActiveSubFolders"] = "V nastavení není žádná aktivní podsložka. Přidej například QA nebo PD.",
        ["SYS01.System.GenerateFoldersConfirmIntro"] = "Chceš založit chybějící složky pro SAP ID podle aktivních rozsahů?",
        ["SYS01.System.SapIdsTotal"] = "SAP ID v cache celkem: {0}",
        ["SYS01.System.SapIdsFiltered"] = "SAP ID po filtru: {0}",
        ["SYS01.System.ActiveRanges"] = "Aktivní rozsahy:",
        ["SYS01.System.ExistingFoldersRemain"] = "Existující složky zůstanou beze změny.",
        ["SYS01.System.GenerateFoldersDone"] = "Hotovo.",
        ["SYS01.System.CreatedArticleFolders"] = "Nové složky SAP ID / artiklů: {0}",
        ["SYS01.System.ExistingArticleFolders"] = "Existující složky SAP ID / artiklů: {0}",
        ["SYS01.System.CreatedSubFolders"] = "Nové podsložky: {0}",
        ["SYS01.System.ExistingSubFolders"] = "Existující podsložky: {0}",
        ["SYS01.System.Errors"] = "Chyby: {0}",
        ["SYS01.System.FirstErrors"] = "Prvních 20 chyb:",
        ["SYS01.System.GenerateFoldersFailed"] = "Generování složek selhalo:\n\n{0}",

        ["SYS01.System.Validation.DocumentsRootRequired"] = "Kořen dokumentů nesmí být prázdný.",
        ["SYS01.System.Validation.ArticleRootRequired"] = "Kořen složek SAP ID / artiklů nesmí být prázdný.",
        ["SYS01.System.Validation.SubFolderCodeRequired"] = "Každá podsložka musí mít vyplněný kód.",
        ["SYS01.System.Validation.SubFolderCodeNoSpaces"] = "Kód podsložky nesmí obsahovat mezery: {0}",
        ["SYS01.System.Validation.SubFolderRelativePathRequired"] = "Podsložka {0} musí mít vyplněnou relativní cestu.",
        ["SYS01.System.Validation.SubFolderRelativePathNoParent"] = "Podsložka {0} nesmí obsahovat '..' v relativní cestě.",
        ["SYS01.System.Validation.SubFolderRelativePathNotAbsolute"] = "Podsložka {0} musí mít relativní cestu, ne absolutní.",
        ["SYS01.System.Validation.SubFolderDuplicateCode"] = "Duplicitní kód podsložky: {0}",
        ["SYS01.System.Validation.RangeNameRequired"] = "Každý rozsah SAP ID musí mít vyplněný název.",
        ["SYS01.System.Validation.RangeValuesRequired"] = "Rozsah {0} musí mít vyplněné hodnoty Od a Do.",
        ["SYS01.System.Validation.RangeFromGreaterThanTo"] = "Rozsah {0} má hodnotu Od větší než Do.",
        ["SYS01.System.Validation.LogoMaxWidthPositive"] = "Maximální šířka loga musí být větší než 0.",
        ["SYS01.System.Validation.LogoMaxHeightPositive"] = "Maximální výška loga musí být větší než 0.",
        ["SYS01.System.Validation.LogoFileMissing"] = "Zadané logo neexistuje:\n{0}",

        ["SYS01.System.SelectLogoDialogTitle"] = "Vyber logo do záhlaví",
        ["SYS01.System.SelectLogoFilter"] = "Obrázky|*.png;*.jpg;*.jpeg;*.bmp;*.webp|Všechny soubory|*.*",
        ["SYS01.System.LogoUploadedStatus"] = "Logo bylo nahráno a zmenšeno: {0}",
        ["SYS01.System.LogoUploadFailed"] = "Logo se nepodařilo nahrát:\n\n{0}"
    };
}
