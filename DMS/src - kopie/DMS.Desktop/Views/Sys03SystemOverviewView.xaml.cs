using DMS.Core.Security;
using DMS.Core.Transactions;
using DMS.Desktop.Configuration;
using DMS.Desktop.Configuration.SystemSettings;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DMS.Desktop.Views.SystemOverview;

public partial class Sys03SystemOverviewView : UserControl
{
    private readonly DmsAppSettings _appSettings;
    private readonly DmsSystemSettings _systemSettings;
    private readonly string _systemSettingsPath;
    private readonly string _sapMaterialsFilePath;
    private readonly string _localizationRootPath;
    private readonly IReadOnlyList<TransactionDefinition> _visibleTransactions;
    private readonly DmsUserContext _currentUser;
    private readonly Func<string, string>? _translate;
    private readonly Action<string, string>? _logSystemOverviewAction;

    public Sys03SystemOverviewView(
        DmsAppSettings appSettings,
        DmsSystemSettings systemSettings,
        string systemSettingsPath,
        string sapMaterialsFilePath,
        string localizationRootPath,
        IReadOnlyList<TransactionDefinition> visibleTransactions,
        DmsUserContext currentUser,
        Func<string, string>? translate = null,
        Action<string, string>? logSystemOverviewAction = null)
    {
        InitializeComponent();

        _appSettings = appSettings;
        _systemSettings = systemSettings;
        _systemSettingsPath = systemSettingsPath;
        _sapMaterialsFilePath = sapMaterialsFilePath;
        _localizationRootPath = localizationRootPath;
        _visibleTransactions = visibleTransactions;
        _currentUser = currentUser;
        _translate = translate;
        _logSystemOverviewAction = logSystemOverviewAction;

        ApplyLocalization();
        LoadOverview();

        _logSystemOverviewAction?.Invoke(
            "OpenSystemOverview",
            $"Environment={_appSettings.Environment}; ConfigurationRootPath={_appSettings.ConfigurationRootPath}; DocumentsRootPath={_appSettings.DocumentsRootPath}; SystemSettingsPath={_systemSettingsPath}");
    }

    private string T(string key)
    {
        var translated = _translate?.Invoke(key);

        if (!string.IsNullOrWhiteSpace(translated) &&
            !string.Equals(translated, key, StringComparison.OrdinalIgnoreCase) &&
            !translated.StartsWith("[[", StringComparison.OrdinalIgnoreCase))
        {
            return translated;
        }

        return Fallbacks.TryGetValue(key, out var fallback)
            ? fallback
            : key;
    }

    private string T(string key, params object[] args)
    {
        return string.Format(T(key), args);
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("SYS03.Title");
        TxtSubtitle.Text = T("SYS03.Subtitle");

        TxtEnvironmentTitle.Text = T("SYS03.EnvironmentTitle");
        TxtEnvironmentLabel.Text = T("SYS03.Environment");
        TxtConfigurationModeLabel.Text = T("SYS03.ConfigurationMode");
        TxtSapModeLabel.Text = T("SYS03.SapMode");
        TxtMesModeLabel.Text = T("SYS03.MesMode");

        TxtUserTitle.Text = T("SYS03.UserTitle");
        TxtWindowsLoginLabel.Text = T("SYS03.WindowsLogin");
        TxtDisplayNameLabel.Text = T("SYS03.DisplayName");
        TxtRolesLabel.Text = T("SYS03.Roles");

        TxtTransactionTitle.Text = T("SYS03.TransactionTitle");
        TxtVisibleTransactionsLabel.Text = T("SYS03.VisibleTransactions");
        TxtVisibleModulesLabel.Text = T("SYS03.VisibleModules");
        TxtDefaultArticleLabel.Text = T("SYS03.DefaultArticle");

        TxtSystemPathsTitle.Text = T("SYS03.SystemPathsTitle");
        TxtConfigRootLabel.Text = T("SYS03.ConfigurationRoot");
        TxtDocumentsRootLabel.Text = T("SYS03.DocumentsRoot");
        TxtArticleRootLabel.Text = T("SYS03.ArticleFoldersRoot");
        TxtLogsRootLabel.Text = T("SYS03.LogsRoot");
        TxtSystemSettingsFileLabel.Text = T("SYS03.SystemSettingsFile");
        TxtSapCacheFileLabel.Text = T("SYS03.SapCacheFile");

        TxtSys01SummaryTitle.Text = T("SYS03.Sys01SummaryTitle");
        TxtAutoFoldersLabel.Text = T("SYS03.AutoFolderCreation");
        TxtSubFoldersCountLabel.Text = T("SYS03.ActiveSubFolders");
        TxtRangesCountLabel.Text = T("SYS03.ActiveMaterialRanges");
        TxtLogoPathLabel.Text = T("SYS03.HeaderLogo");
        TxtLogoSizeLabel.Text = T("SYS03.HeaderLogoSize");

        TxtLocalizationTitle.Text = T("SYS03.LocalizationTitle");
        TxtLocalizationRootLabel.Text = T("SYS03.LocalizationRoot");
        TxtLocalizationIndexLabel.Text = T("SYS03.LocalizationIndex");

        TxtSubFoldersTitle.Text = T("SYS03.SubFoldersTitle");
        TxtMaterialRangesTitle.Text = T("SYS03.MaterialRangesTitle");

        ColSubFolderActive.Header = T("Common.Active");
        ColSubFolderCode.Header = T("SYS01.System.SubFolders.Code");
        ColSubFolderName.Header = T("SYS01.System.SubFolders.Name");
        ColSubFolderRelativePath.Header = T("SYS01.System.SubFolders.RelativePath");

        ColRangeActive.Header = T("Common.Active");
        ColRangeName.Header = T("SYS01.System.MaterialRanges.Name");
        ColRangeFrom.Header = T("SYS01.System.MaterialRanges.From");
        ColRangeTo.Header = T("SYS01.System.MaterialRanges.To");
    }

    private void LoadOverview()
    {
        TxtEnvironmentValue.Text = NullDash(_appSettings.Environment);
        TxtConfigurationModeValue.Text = NullDash(_appSettings.ConfigurationMode);
        TxtSapModeValue.Text = NullDash(_appSettings.SapMode);
        TxtMesModeValue.Text = NullDash(_appSettings.MesMode);

        TxtWindowsLoginValue.Text = NullDash(_currentUser.WindowsLogin);
        TxtDisplayNameValue.Text = NullDash(_currentUser.DisplayName);
        TxtRolesValue.Text = !_currentUser.Roles.Any()
            ? "-"
            : string.Join(", ", _currentUser.Roles);

        TxtVisibleTransactionsValue.Text = _visibleTransactions.Count.ToString();
        TxtVisibleModulesValue.Text = _visibleTransactions
            .Select(x => x.Module)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count()
            .ToString();

        TxtDefaultArticleValue.Text = NullDash(_appSettings.DefaultTestArticleNumber);

        SetPath(
            TxtConfigRootValue,
            TxtConfigRootStatus,
            _appSettings.ConfigurationRootPath,
            isFile: false);

        SetPath(
            TxtDocumentsRootValue,
            TxtDocumentsRootStatus,
            _systemSettings.DocumentsRootPath,
            isFile: false);

        SetPath(
            TxtArticleRootValue,
            TxtArticleRootStatus,
            _systemSettings.ArticleFoldersRootPath,
            isFile: false);

        SetPath(
            TxtLogsRootValue,
            TxtLogsRootStatus,
            _appSettings.LogsRootPath,
            isFile: false);

        SetPath(
            TxtSystemSettingsFileValue,
            TxtSystemSettingsFileStatus,
            _systemSettingsPath,
            isFile: true);

        SetPath(
            TxtSapCacheFileValue,
            TxtSapCacheFileStatus,
            _sapMaterialsFilePath,
            isFile: true);

        TxtAutoFoldersValue.Text = _systemSettings.CreateArticleFoldersOnSapImport
            ? T("Common.Yes")
            : T("Common.No");

        var subFolders = _systemSettings.ArticleSubFolders ?? new List<DmsArticleSubFolderDefinition>();
        var materialRanges = _systemSettings.ArticleFolderMaterialRanges ?? new List<DmsMaterialRangeDefinition>();

        TxtSubFoldersCountValue.Text = T(
            "SYS03.CountActiveTotal",
            subFolders.Count(x => x.IsActive),
            subFolders.Count);

        TxtRangesCountValue.Text = T(
            "SYS03.CountActiveTotal",
            materialRanges.Count(x => x.IsActive),
            materialRanges.Count);

        TxtLogoPathValue.Text = NullDash(_systemSettings.HeaderSecondaryLogoPath);
        TxtLogoSizeValue.Text =
            $"{_systemSettings.HeaderSecondaryLogoMaxWidth:0} × {_systemSettings.HeaderSecondaryLogoMaxHeight:0} px";

        SetPath(
            TxtLocalizationRootValue,
            TxtLocalizationRootStatus,
            _localizationRootPath,
            isFile: false);

        var localizationIndexPath = Path.Combine(_localizationRootPath, "localization.index.json");

        SetPath(
            TxtLocalizationIndexValue,
            TxtLocalizationIndexStatus,
            localizationIndexPath,
            isFile: true);

        GridSubFolders.ItemsSource = subFolders
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Code)
            .ToList();

        GridMaterialRanges.ItemsSource = materialRanges
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.From)
            .ToList();
    }

    private void SetPath(
        TextBlock valueTextBlock,
        TextBlock statusTextBlock,
        string? path,
        bool isFile)
    {
        valueTextBlock.Text = NullDash(path);

        var exists = !string.IsNullOrWhiteSpace(path) &&
            (isFile ? File.Exists(path) : Directory.Exists(path));

        statusTextBlock.Text = exists
            ? T("SYS03.StatusOk")
            : T("SYS03.StatusMissing");

        statusTextBlock.Foreground = exists
            ? new SolidColorBrush(Color.FromRgb(40, 167, 69))
            : new SolidColorBrush(Color.FromRgb(220, 80, 80));
    }

    private static string NullDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "-"
            : value;
    }

    private static readonly Dictionary<string, string> Fallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SYS03.Title"] = "SYS03 - System Overview",
        ["SYS03.Subtitle"] = "Read-only overview of DMS runtime, configuration and SYS01 settings.",
        ["SYS03.EnvironmentTitle"] = "Environment",
        ["SYS03.Environment"] = "Environment:",
        ["SYS03.ConfigurationMode"] = "Configuration mode:",
        ["SYS03.SapMode"] = "SAP:",
        ["SYS03.MesMode"] = "MES:",
        ["SYS03.UserTitle"] = "Current user",
        ["SYS03.WindowsLogin"] = "Windows:",
        ["SYS03.DisplayName"] = "Name:",
        ["SYS03.Roles"] = "Roles:",
        ["SYS03.TransactionTitle"] = "Transactions",
        ["SYS03.VisibleTransactions"] = "Visible:",
        ["SYS03.VisibleModules"] = "Modules:",
        ["SYS03.DefaultArticle"] = "Default article:",
        ["SYS03.SystemPathsTitle"] = "System paths",
        ["SYS03.ConfigurationRoot"] = "Configuration:",
        ["SYS03.DocumentsRoot"] = "Documents:",
        ["SYS03.ArticleFoldersRoot"] = "Article folders:",
        ["SYS03.LogsRoot"] = "Logs:",
        ["SYS03.SystemSettingsFile"] = "SYS01 file:",
        ["SYS03.SapCacheFile"] = "SAP cache:",
        ["SYS03.Sys01SummaryTitle"] = "SYS01 summary",
        ["SYS03.AutoFolderCreation"] = "Auto folder creation:",
        ["SYS03.ActiveSubFolders"] = "Active subfolders:",
        ["SYS03.ActiveMaterialRanges"] = "Active SAP ID ranges:",
        ["SYS03.HeaderLogo"] = "Header logo:",
        ["SYS03.HeaderLogoSize"] = "Header logo size:",
        ["SYS03.LocalizationTitle"] = "Localization",
        ["SYS03.LocalizationRoot"] = "Folder:",
        ["SYS03.LocalizationIndex"] = "Index:",
        ["SYS03.SubFoldersTitle"] = "Article subfolders",
        ["SYS03.MaterialRangesTitle"] = "SAP ID ranges",
        ["SYS03.StatusOk"] = "OK",
        ["SYS03.StatusMissing"] = "Missing",
        ["SYS03.CountActiveTotal"] = "{0} active / {1} total",

        ["Common.Active"] = "Active",
        ["Common.Yes"] = "Yes",
        ["Common.No"] = "No",

        ["SYS01.System.SubFolders.Code"] = "Code",
        ["SYS01.System.SubFolders.Name"] = "Name",
        ["SYS01.System.SubFolders.RelativePath"] = "Relative path",
        ["SYS01.System.MaterialRanges.Name"] = "Name",
        ["SYS01.System.MaterialRanges.From"] = "From",
        ["SYS01.System.MaterialRanges.To"] = "To"
    };
}
