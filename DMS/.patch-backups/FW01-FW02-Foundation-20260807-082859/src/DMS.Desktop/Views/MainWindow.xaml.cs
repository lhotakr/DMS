using DMS.Core.Sap;
using DMS.Core.Security;
using DMS.Core.Transactions;
using DMS.Core.Transactions.Handlers;
using DMS.Desktop.Configuration;
using DMS.Desktop.Configuration.Modules;
using DMS.Desktop.Configuration.SystemSettings;
using DMS.Desktop.Localization;
using DMS.Desktop.Logging;
using DMS.Desktop.Models;
using DMS.Desktop.Repositories;
using DMS.Desktop.Services;
using DMS.Desktop.Settings;
using DMS.Desktop.Performance;
using System.Diagnostics;
using DMS.Desktop.Views.Admin;
using DMS.Desktop.Views.Articles;
using DMS.Desktop.Views.Dialogs;
using DMS.Desktop.Views.Documents;
using DMS.Desktop.Views.Help;
using DMS.Desktop.Views.Settings;
using DMS.Desktop.Views.Mes;
using DMS.Desktop.Views.SystemModules;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DMS.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _isLeftPanelVisible = true;
    private GridLength _lastLeftPanelWidth = new(350);
    private TransactionDispatcher _transactionDispatcher = null!;
    private readonly DmsUserSettingsService _settingsService = new();
    private readonly DmsAppSettingsService _appSettingsService = new();
    private DmsLogger _logger = null!;
    private readonly DmsLogReader _logReader = new();
    private SapDecorationRuleService _decorationRuleService = null!;

    private DmsLocalizationService _localizationService = null!;
    private DmsSystemSettingsService _systemSettingsService = null!;
    private DmsSystemSettings _systemSettings = new();
    private string _systemSettingsPath = string.Empty;

    private DmsAppSettings _appSettings = new();
    private DmsUserSettings _userSettings = new();
    private DmsUserContext _currentUser = new();
    private FavoriteTransactionItem? _favoriteContextMenuItem;
    private JsonArticleRepository _articleRepository = null!;
    private string _usersConfigPath = string.Empty;

    private readonly Stack<string> _navigationBackStack = new();
    private readonly Stack<string> _navigationForwardStack = new();
    private string? _currentTransactionCommand;
    private bool _isNavigatingFromHistory;
    private bool _startupTransactionExecuted;
    private IReadOnlyList<DmsModuleDefinition> _configuredModules = Array.Empty<DmsModuleDefinition>();
    public MainWindow()
    {
        InitializeComponent();

        _appSettings = _appSettingsService.Load();

        _logger = new DmsLogger(_appSettings.LogsRootPath);
        _logger.Info("DMS klient spuĹˇtÄ›n.");

        _systemSettingsPath = GetConfigPath("dms-system-settings.json");

        var localizationRootPath = Path.Combine(_appSettings.ConfigurationRootPath, "Localization");

        _localizationService = new DmsLocalizationService(localizationRootPath);
        _localizationService.Load("Auto", null);

        _systemSettingsService = new DmsSystemSettingsService(_systemSettingsPath);
        _systemSettings = _systemSettingsService.Load();

        var articlesFilePath = string.IsNullOrWhiteSpace(_appSettings.ArticlesDataPath)
            ? GetDataPath("articles.json")
            : _appSettings.ArticlesDataPath;

        articlesFilePath = Path.GetFullPath(articlesFilePath);

        _articleRepository = new JsonArticleRepository(articlesFilePath);
        _logger.Info($"Articles repository path: {articlesFilePath}; Exists: {File.Exists(articlesFilePath)}");

        var decorationRulesPath = GetConfigPath("sap-decoration-rules.json");

        var decorationRules = new SapDecorationRulesLoader()
            .LoadFromJson(decorationRulesPath);

        _decorationRuleService = new SapDecorationRuleService(decorationRules);

        EnsureLeftPanelVisibleOnStartup();
        InitializeCurrentUser();
        InitializeTransactions();
        LoadUserSettings();
        ApplyTheme();
        ApplyHeaderBranding();

        UpdateCurrentTransactionText(_currentTransactionCommand);

        FocusTransactionInput();
        UpdateNavigationButtons();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startupTransactionExecuted)
            return;

        _startupTransactionExecuted = true;
        var startupTransaction = _userSettings.StartupTransaction?.Trim();
        if (string.IsNullOrWhiteSpace(startupTransaction))
            return;

        Dispatcher.BeginInvoke(() => ExecuteTransaction(startupTransaction));
    }

    private void ApplyTheme()
    {
        var themeMode = _userSettings.ThemeMode ?? "Light";

        SolidColorBrush backgroundBrush;
        SolidColorBrush panelBrush;
        SolidColorBrush foregroundBrush;
        SolidColorBrush mutedForegroundBrush;
        SolidColorBrush borderBrush;
        SolidColorBrush accentBrush;
        SolidColorBrush onAccentBrush;

        if (string.Equals(themeMode, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            backgroundBrush = CreateBrushFromHex("#18181B", "#18181B");
            panelBrush = CreateBrushFromHex("#27272A", "#27272A");
            foregroundBrush = CreateBrushFromHex("#F5F5F5", "#F5F5F5");
            mutedForegroundBrush = CreateBrushFromHex("#B4B4B9", "#B4B4B9");
            borderBrush = CreateBrushFromHex("#4B4B50", "#4B4B50");

            // Dark mĂˇ mĂ­t vlastnĂ­ tmavÄ› modrĂ˝ DMS akcent,
            // ne poslednĂ­ uloĹľenou barvu z HG/Custom.
            accentBrush = CreateBrushFromHex("#0B2A4A", "#0B2A4A");
            onAccentBrush = CreateBrushFromHex("#FFFFFF", "#FFFFFF");
        }
        else if (string.Equals(themeMode, "HG", StringComparison.OrdinalIgnoreCase))
        {
            backgroundBrush = CreateBrushFromHex("#050505", "#050505");
            panelBrush = CreateBrushFromHex("#111111", "#111111");
            foregroundBrush = CreateBrushFromHex("#F5F5F5", "#F5F5F5");
            mutedForegroundBrush = CreateBrushFromHex("#C7C7C7", "#C7C7C7");
            borderBrush = CreateBrushFromHex("#3A3A32", "#3A3A32");
            accentBrush = CreateBrushFromHex("#FFE500", "#FFE500");
            onAccentBrush = CreateBrushFromHex("#111111", "#111111");
        }
        else if (string.Equals(themeMode, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            backgroundBrush = CreateBrushFromHex(_userSettings.BackgroundColor, "#18181B");
            panelBrush = CreateBrushFromHex(_userSettings.PanelColor, "#27272A");
            foregroundBrush = CreateBrushFromHex(_userSettings.ForegroundColor, "#F5F5F5");
            mutedForegroundBrush = CreateBrushFromHex(_userSettings.MutedForegroundColor, "#B4B4B9");
            borderBrush = CreateBrushFromHex(_userSettings.BorderColor, "#4B4B50");
            accentBrush = CreateBrushFromHex(_userSettings.AccentColor, "#0B2A4A");
            onAccentBrush = CreateBrushFromHex(_userSettings.OnAccentColor, "#FFFFFF");
        }
        else
        {
            backgroundBrush = CreateBrushFromHex("#F4F6F8", "#F4F6F8");
            panelBrush = CreateBrushFromHex("#FFFFFF", "#FFFFFF");
            foregroundBrush = CreateBrushFromHex("#111111", "#111111");
            mutedForegroundBrush = CreateBrushFromHex("#666666", "#666666");
            borderBrush = CreateBrushFromHex("#D0D7DE", "#D0D7DE");

            // Light default DMS akcent
            accentBrush = CreateBrushFromHex("#0B2A4A", "#0B2A4A");
            onAccentBrush = CreateBrushFromHex("#FFFFFF", "#FFFFFF");
        }

        SetApplicationBrush("DmsBackgroundBrush", backgroundBrush);
        SetApplicationBrush("DmsPanelBrush", panelBrush);
        SetApplicationBrush("DmsForegroundBrush", foregroundBrush);
        SetApplicationBrush("DmsMutedForegroundBrush", mutedForegroundBrush);
        SetApplicationBrush("DmsBorderBrush", borderBrush);
        SetApplicationBrush("DmsAccentBrush", accentBrush);
        SetApplicationBrush("DmsOnAccentBrush", onAccentBrush);
        SetApplicationBrush("DmsDataGridAddedRowBrush",
            CreateBrushFromHex(_userSettings.DataGridAddedRowColor, "#263A28"));
        SetApplicationBrush("DmsDataGridModifiedRowBrush",
            CreateBrushFromHex(_userSettings.DataGridModifiedRowColor, "#4A3820"));
        SetApplicationBrush("DmsDataGridDeletedRowBrush",
            CreateBrushFromHex(_userSettings.DataGridDeletedRowColor, "#4A2020"));
        DmsWindowChromeStyler.ApplyToAllOpenWindows();

        Application.Current.Resources[SystemColors.HighlightBrushKey] = accentBrush;
        Application.Current.Resources[SystemColors.HighlightTextBrushKey] = onAccentBrush;

        Application.Current.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = accentBrush;
        Application.Current.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = onAccentBrush;

        RootPanel.Background = backgroundBrush;
        TopBar.Background = accentBrush;
        TransactionBar.Background = panelBrush;
        LeftMenuPanel.Background = panelBrush;
        WorkspaceHost.Background = panelBrush;
        WorkspaceHost.BorderBrush = borderBrush;
    }
    private void ApplyHeaderBranding()
    {
        if (_systemSettings is null)
        {
            _logger.Warning("Header logo: system settings are not available.");
            ImgSecondaryHeaderLogo.Source = null;
            ImgSecondaryHeaderLogo.Visibility = Visibility.Collapsed;
            return;
        }

        var configuredLogoPath = _systemSettings.HeaderSecondaryLogoPath?.Trim();
        var logoPath = ResolveHeaderLogoPath(configuredLogoPath);

        _logger.Info(
            $"Header logo configured path: '{configuredLogoPath}'; resolved path: '{logoPath}'");

        if (string.IsNullOrWhiteSpace(logoPath) || !File.Exists(logoPath))
        {
            _logger.Warning("Header logo was not found. The secondary header logo will be hidden.");
            ImgSecondaryHeaderLogo.Source = null;
            ImgSecondaryHeaderLogo.Visibility = Visibility.Collapsed;
            return;
        }

        ImgSecondaryHeaderLogo.MaxWidth = _systemSettings.HeaderSecondaryLogoMaxWidth;
        ImgSecondaryHeaderLogo.MaxHeight = _systemSettings.HeaderSecondaryLogoMaxHeight;

        ImgSecondaryHeaderLogo.Source = LoadScaledBitmap(
            logoPath,
            (int)_systemSettings.HeaderSecondaryLogoMaxWidth,
            (int)_systemSettings.HeaderSecondaryLogoMaxHeight);

        ImgSecondaryHeaderLogo.Visibility = Visibility.Visible;

        _logger.Info($"Header logo loaded: {logoPath}");
    }

    private string? ResolveHeaderLogoPath(string? configuredLogoPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredLogoPath))
        {
            if (File.Exists(configuredLogoPath))
            {
                return configuredLogoPath;
            }

            var configuredFileName = Path.GetFileName(configuredLogoPath);

            if (!string.IsNullOrWhiteSpace(configuredFileName))
            {
                foreach (var root in GetBrandingSearchRoots())
                {
                    var candidate = Path.Combine(root, configuredFileName);

                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        foreach (var root in GetBrandingSearchRoots())
        {
            var candidate = FindFirstLogoFile(root);

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private IEnumerable<string> GetBrandingSearchRoots()
    {
        var roots = new[]
        {
            _appSettings.BrandingRootPath,
            Path.Combine(GetDmsDataRootPath(), "Branding"),
            Path.Combine(AppContext.BaseDirectory, "Branding"),
            Path.Combine(AppContext.BaseDirectory, "Assets"),
            AppContext.BaseDirectory
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string fullPath;

            try
            {
                fullPath = Path.GetFullPath(root);
            }
            catch
            {
                continue;
            }

            if (Directory.Exists(fullPath))
            {
                yield return fullPath;
            }
        }
    }

    private static string? FindFirstLogoFile(string folderPath)
    {
        var patterns = new[]
        {
            "*logo*.png",
            "*logo*.jpg",
            "*logo*.jpeg",
            "*logo*.bmp",
            "*.png",
            "*.jpg",
            "*.jpeg",
            "*.bmp"
        };

        foreach (var pattern in patterns)
        {
            try
            {
                var file = Directory
                    .EnumerateFiles(folderPath, pattern, SearchOption.TopDirectoryOnly)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(file))
                {
                    return file;
                }
            }
            catch
            {
                // Ignore invalid or inaccessible branding folders.
            }
        }

        return null;
    }

    private static BitmapImage LoadScaledBitmap(
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

    private void ResetWorkspaceScroll()
    {
        Dispatcher.BeginInvoke(() =>
        {
            WorkspaceScrollViewer.ScrollToTop();
            WorkspaceScrollViewer.ScrollToLeftEnd();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private StackPanel CreateWorkspaceStack()
    {
        WorkspacePanel.Children.Clear();

        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Vertical
        };

        WorkspacePanel.Children.Add(stackPanel);

        return stackPanel;
    }
    private static TextBlock CreateTitle(string text)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 16)
        };

        textBlock.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        return textBlock;
    }

    private static TextBlock CreateSectionTitle(string text)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };

        textBlock.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        return textBlock;
    }

    private static TextBlock CreateBodyText(string text)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        };

        textBlock.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        return textBlock;
    }

    private static void SetApplicationBrush(string key, Brush brush)
    {
        Application.Current.Resources[key] = brush;
    }

    private static SolidColorBrush CreateBrushFromHex(string? hex, string fallbackHex)
    {
        try
        {
            var value = string.IsNullOrWhiteSpace(hex)
                ? fallbackHex
                : hex.Trim();

            if (value.StartsWith("#"))
            {
                value = value[1..];
            }

            if (value.Length != 6)
            {
                value = fallbackHex.TrimStart('#');
            }

            var r = Convert.ToByte(value.Substring(0, 2), 16);
            var g = Convert.ToByte(value.Substring(2, 2), 16);
            var b = Convert.ToByte(value.Substring(4, 2), 16);

            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(11, 42, 74));
        }
    }
    private void NewWindowCommand_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        OpenEmptyNewWindow();
    }

    private void OpenEmptyNewWindow()
    {
        var newWindow = new MainWindow();
        newWindow.Show();
    }

    private void InitializeCurrentUser()
    {
        var windowsLogin = WindowsIdentity.GetCurrent()?.Name ?? string.Empty;
        _usersConfigPath = GetConfigPath("users.json");

        var loader = new DmsUserLoader();
        var users = loader.LoadFromJson(_usersConfigPath);

        var user = users.FirstOrDefault(item =>
            string.Equals(item.WindowsLogin, windowsLogin, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            _currentUser = new DmsUserContext
            {
                WindowsLogin = windowsLogin,
                DisplayName = windowsLogin,
                PersonId = null,
                Roles = new[] { "DMS_READONLY" }
            };

            UpdateCurrentUserText();

            DmsMessage.Show(
                $"UĹľivatel nenĂ­ zaloĹľenĂ˝ v DMS.\n\nWindows login:\n{windowsLogin}\n\nBude pouĹľit reĹľim DMS_READONLY.",
                "DMS - uĹľivatel nenalezen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        _currentUser = new DmsUserContext
        {
            WindowsLogin = user.WindowsLogin,
            DisplayName = user.DisplayName,
            PersonId = user.PersonId,
            Roles = user.Roles
        };
        _logger.Info($"AktuĂˇlnĂ­ uĹľivatel: {_currentUser.WindowsLogin}; DMS jmĂ©no: {_currentUser.DisplayName}; Role: {string.Join(", ", _currentUser.Roles)}");
        UpdateCurrentUserText();
    }

    private void UpdateCurrentUserText()
    {
        TxtCurrentUser.Text = T(
            "Shell.UserFormat",
            _currentUser.DisplayName,
            string.Join(", ", _currentUser.Roles));

        UpdateCurrentTransactionText(_currentTransactionCommand);
    }

    private void InitializeTransactions()
    {
        var configPath = GetConfigPath("transactions.json");

        var loader = new TransactionDefinitionLoader();
        var definitions = loader.LoadFromJson(configPath);
        definitions = DMS.Core.Checklists.ChecklistTransactionDefinitions.AddMissing(definitions);
        definitions = DMS.Core.Quality.QualityMenuTransactionDefinitions.AddMissing(definitions);
        definitions = DMS.Core.Framework.FrameworkTransactionDefinitions.AddMissing(definitions);

        if (definitions.Count == 0)
        {
            DmsMessage.Show(
                $"NenaÄŤetly se ĹľĂˇdnĂ© transakce.\n\nOÄŤekĂˇvanĂˇ cesta:\n{configPath}",
                "DMS - konfigurace transakcĂ­",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        var handlers = new ITransactionHandler[]
{
    // DMS zĂˇklad
    new SettingsTransactionHandler(() => _userSettings.MaxTransactionHistoryItems),
    new HelpTransactionHandler(() => _transactionDispatcher.GetDefinitions()),
    new SimpleMessageTransactionHandler("SystemInfo", "AktuĂˇlnĂ­ uĹľivatel"),

    // Administrace / systĂ©m
    new SimpleMessageTransactionHandler("ClientSettings", "NastavenĂ­ klienta"),
    new SimpleMessageTransactionHandler("SystemSettings", "NastavenĂ­ systĂ©mu DMS"),
    new SimpleMessageTransactionHandler("SystemDisplay", "NĂˇhled systĂ©mu DMS"),
    new SimpleMessageTransactionHandler("TransactionManagement", "SprĂˇva transakcĂ­"),
    new SimpleMessageTransactionHandler("RoleManagement", "SprĂˇva rolĂ­"),
    new SimpleMessageTransactionHandler("ModuleManagement", "SprĂˇva modulĹŻ"),
    new SimpleMessageTransactionHandler("UserManagement", "SprĂˇva uĹľivatelĹŻ"),
    new SimpleMessageTransactionHandler("LogViewer", "Log aplikace"),
    new SimpleMessageTransactionHandler("FrameworkHub", "DMS Framework"),
    new SimpleMessageTransactionHandler("FrameworkDiagnostics", "DMS Framework diagnostics"),

    // Artikly / dokumenty
    new ArticleCreateTransactionHandler(),
    new ArticleChangeTransactionHandler(),
    new ArticleCardTransactionHandler(),
    new ArticleDocumentsTransactionHandler(),
    new ArticleDocumentCreateTransactionHandler(),
    new ArticleDocumentEditTransactionHandler(),
    new SimpleMessageTransactionHandler("DocumentDisplay", "ZobrazenĂ­ dokumentĹŻ"),
    new ArticleScreensTransactionHandler(),

    new SimpleMessageTransactionHandler("ScreenPreparationQueue", "Fronta pĹ™Ă­pravy sĂ­t"),
    new SimpleMessageTransactionHandler("OrderOverview", "PĹ™ehled zakĂˇzek"),
    new SimpleMessageTransactionHandler("RecipeOverview", "PĹ™ehled receptur"),
    new SimpleMessageTransactionHandler("TechnicalArticleSummary", "TechnologickĂ˝ souhrn artiklu"),
    new SimpleMessageTransactionHandler("TechnologyArticleSummary", "TechnologickĂ˝ souhrn artiklu"),
    new SimpleMessageTransactionHandler("TechnicalSummary", "TechnologickĂ˝ souhrn artiklu"),

    // SAP
    new SimpleMessageTransactionHandler("SapSettings", "SAP nastavenĂ­"),
    new SimpleMessageTransactionHandler("SapCockpit", "SAP import cockpit"),
    new SimpleMessageTransactionHandler("SapMaterialDisplay", "NĂˇhled SAP materiĂˇlu"),
    new SimpleMessageTransactionHandler("SapRecipeDisplay", "NĂˇhled receptury"),

    // Quality
    new SimpleMessageTransactionHandler("QualitySettings", "Quality nastavenĂ­"),
    new SimpleMessageTransactionHandler("QualityCockpit", "Quality cockpit"),
    new SimpleMessageTransactionHandler("QualityArticleDisplay", "Quality karta"),
    new SimpleMessageTransactionHandler("QualityArticleEdit", "ZmÄ›na quality dat"),
    new SimpleMessageTransactionHandler("QualityArticleCreate", "ZaloĹľenĂ­ quality dat"),
    new SimpleMessageTransactionHandler("QualityPrintVersionList", "PĹ™ehled tiskovĂ˝ch verzĂ­"),
    new SimpleMessageTransactionHandler("QualityTasksOverview", "Quality Ăşkoly"),
    new SimpleMessageTransactionHandler("QualityTaskOverview", "Quality Ăşkoly"),
    new SimpleMessageTransactionHandler("QualityTasks", "Quality Ăşkoly"),

    // MES
    new MesDataPointMonitorTransactionHandler(),
    new ChecklistTransactionHandler(),

    // fallback / obecnĂ©
    new QualityOrderCreateTransactionHandler(),
    new QualityOrderEditTransactionHandler(),
    new QualityOrderDisplayTransactionHandler(),
    new QualityOrderListTransactionHandler(),
    new QualityOrderReleaseTransactionHandler(),
    new SimpleMessageTransactionHandler("MesCommunicationSettings", "MES nastavenĂ­ komunikace"),
    new SimpleMessageTransactionHandler("MesDeviceEditor", "MES editace zaĹ™Ă­zenĂ­"),
    new SimpleMessageTransactionHandler("MesStationData", "MES data stanic"),
    new SimpleMessageTransactionHandler("MesWorkplaceOverview", "MES soupis pracoviĹˇĹĄ"),
    new SimpleMessageTransactionHandler("SimpleMessage", "Transakce"),
};

        _transactionDispatcher = new TransactionDispatcher(definitions, handlers);
    }

    private void LoadUserSettings()
    {
        _userSettings = _settingsService.Load();

        if (_userSettings.MaxTransactionHistoryItems <= 0)
        {
            _userSettings.MaxTransactionHistoryItems = 10;
        }

        if (_userSettings.FavoriteTransactions.Count == 0)
        {
            _userSettings.FavoriteTransactions.AddRange(new[]
            {
                "ART03",
                "DOC03",
                "SCR03",
                "SCR10",
                "ORD10"
            });
        }

        _localizationService.Load(
            _userSettings.LanguageMode,
            _userSettings.CultureName);

        RefreshTransactionHistoryList();
        RefreshFavoritesList();
        RefreshModulesList();
        RefreshModuleTransactionsList("VĹˇe");
        ApplyLocalization();
    }

    private void RefreshFavoritesList()
    {
        LstFavorites.Items.Clear();

        foreach (var transactionCode in _userSettings.FavoriteTransactions)
        {
            var definition = _transactionDispatcher.FindDefinition(transactionCode);

            if (definition is null || !UserCanSeeTransaction(definition))
            {
                continue;
            }

            LstFavorites.Items.Add(new FavoriteTransactionItem
            {
                Code = definition.Code,
                Name = DmsTransactionText.Name(definition, T)
            });
        }
    }
    private string GetSelectedModuleName()
    {
        if (LstModules.SelectedItem is ModuleMenuItem module)
        {
            return module.Name;
        }

        return "VĹˇe";
    }

    private bool UserCanSeeTransaction(TransactionDefinition definition)
    {
        if (!IsTransactionModuleActive(definition))
        {
            return false;
        }

        if (definition.Roles.Count == 0)
        {
            return true;
        }

        return _currentUser.HasAnyRole(definition.Roles);
    }

    private IReadOnlyList<TransactionDefinition> GetVisibleTransactionDefinitions()
    {
        EnsureConfiguredModulesLoaded();

        return _transactionDispatcher
            .GetDefinitions()
            .Where(UserCanSeeTransaction)
            .OrderBy(definition => GetModuleSortOrder(definition.Module))
            .ThenBy(definition => DmsTransactionText.Name(definition, T))
            .ToList();
    }

    private void EnsureConfiguredModulesLoaded(bool forceReload = false)
    {
        if (!forceReload && _configuredModules.Count > 0)
        {
            return;
        }

        try
        {
            var modulesPath = GetConfigPath("dms-modules.json");
            _configuredModules = new DmsModuleManagementService(modulesPath)
                .LoadAll()
                .OrderBy(module => module.SortOrder)
                .ThenBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger?.Info($"DMS module configuration loaded: Path={modulesPath}; Count={_configuredModules.Count}; Active={_configuredModules.Count(module => module.IsActive)}");
        }
        catch (Exception ex)
        {
            _configuredModules = Array.Empty<DmsModuleDefinition>();
            _logger?.Error("DMS module configuration load failed.", ex);
        }
    }

    private DmsModuleDefinition? FindConfiguredModule(string? rawModule)
    {
        if (string.IsNullOrWhiteSpace(rawModule))
        {
            return null;
        }

        EnsureConfiguredModulesLoaded();
        var value = rawModule.Trim();

        return _configuredModules.FirstOrDefault(module =>
            string.Equals(module.Code, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(module.Name, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(DmsModuleText.Name(module, T), value, StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveModuleCode(string? rawModule)
    {
        var configured = FindConfiguredModule(rawModule);

        if (configured is not null)
        {
            return configured.Code;
        }

        return string.IsNullOrWhiteSpace(rawModule)
            ? string.Empty
            : rawModule.Trim();
    }

    private string GetModuleDisplayName(string? rawModule)
    {
        var configured = FindConfiguredModule(rawModule);

        if (configured is not null)
        {
            return DmsModuleText.Name(configured, T);
        }

        return DmsTransactionText.Module(rawModule ?? string.Empty, T);
    }

    private int GetModuleSortOrder(string? rawModule)
    {
        return FindConfiguredModule(rawModule)?.SortOrder ?? int.MaxValue;
    }

    private bool IsTransactionModuleActive(TransactionDefinition definition)
    {
        var configured = FindConfiguredModule(definition.Module);

        // Backward-compatible fallback: a transaction whose module is not yet
        // registered remains available until SYS13 receives its definition.
        return configured?.IsActive ?? true;
    }

    private void RefreshLocalizedTransactionNavigation()
    {
        var selectedModuleName = GetSelectedModuleName();

        EnsureConfiguredModulesLoaded(forceReload: true);
        RefreshFavoritesList();
        RefreshModulesList(selectedModuleName);
        RefreshModuleTransactionsList(GetSelectedModuleName());
    }

    private void RefreshModulesList(string? selectedModuleName = null)
    {
        EnsureConfiguredModulesLoaded(forceReload: true);

        selectedModuleName = string.IsNullOrWhiteSpace(selectedModuleName)
            ? "VĹˇe"
            : selectedModuleName;

        LstModules.Items.Clear();

        LstModules.Items.Add(new ModuleMenuItem
        {
            Name = "VĹˇe",
            DisplayName = DmsTransactionText.AllModules(T)
        });

        var referencedModuleCodes = _transactionDispatcher
            .GetDefinitions()
            .Where(definition =>
                definition.Roles.Count == 0 ||
                _currentUser.HasAnyRole(definition.Roles))
            .Select(definition => ResolveModuleCode(definition.Module))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var module in _configuredModules
                     .Where(module => module.IsActive)
                     .Where(module => referencedModuleCodes.Contains(module.Code))
                     .OrderBy(module => module.SortOrder)
                     .ThenBy(module => DmsModuleText.Name(module, T)))
        {
            LstModules.Items.Add(new ModuleMenuItem
            {
                Name = module.Code,
                DisplayName = DmsModuleText.Name(module, T)
            });
        }

        // Keep unknown runtime modules visible until they are formally added in SYS13.
        var configuredCodes = _configuredModules
            .Select(module => module.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var rawModule in _transactionDispatcher.GetDefinitions()
                     .Where(definition =>
                         definition.Roles.Count == 0 ||
                         _currentUser.HasAnyRole(definition.Roles))
                     .Select(definition => definition.Module)
                     .Where(module => !string.IsNullOrWhiteSpace(module))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(module => !configuredCodes.Contains(ResolveModuleCode(module)))
                     .OrderBy(module => DmsTransactionText.Module(module, T)))
        {
            LstModules.Items.Add(new ModuleMenuItem
            {
                Name = ResolveModuleCode(rawModule),
                DisplayName = DmsTransactionText.Module(rawModule, T)
            });
        }

        for (var index = 0; index < LstModules.Items.Count; index++)
        {
            if (LstModules.Items[index] is ModuleMenuItem item &&
                string.Equals(item.Name, selectedModuleName, StringComparison.OrdinalIgnoreCase))
            {
                LstModules.SelectedIndex = index;
                return;
            }
        }

        if (LstModules.Items.Count > 0)
        {
            LstModules.SelectedIndex = 0;
        }
    }

    private void RefreshModuleTransactionsList(string selectedModule)
    {
        LstModuleTransactions.Items.Clear();

        var definitions = GetVisibleTransactionDefinitions();

        if (!string.Equals(selectedModule, "VĹˇe", StringComparison.OrdinalIgnoreCase))
        {
            definitions = definitions
                .Where(definition =>
                    string.Equals(
                        ResolveModuleCode(definition.Module),
                        selectedModule,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var definition in definitions
                     .OrderBy(definition => GetModuleSortOrder(definition.Module))
                     .ThenBy(definition => DmsTransactionText.Name(definition, T)))
        {
            LstModuleTransactions.Items.Add(new TransactionMenuItem
            {
                Code = definition.Code,
                Name = DmsTransactionText.Name(definition, T),
                Module = ResolveModuleCode(definition.Module),
                DisplayModule = GetModuleDisplayName(definition.Module),
                Description = DmsTransactionText.Description(definition, T),
                RequiresArticleNumber = definition.RequiresArticleNumber,
                IsFavorite = IsFavoriteTransaction(definition.Code)
            });
        }
    }
    private void LstModules_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstModules.SelectedItem is not ModuleMenuItem module)
        {
            return;
        }

        RefreshModuleTransactionsList(module.Name);
    }
    private bool IsFavoriteTransaction(string transactionCode)
    {
        return _userSettings.FavoriteTransactions.Any(item =>
            string.Equals(item, transactionCode, StringComparison.OrdinalIgnoreCase));
    }
    private void LstFavorites_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var listBoxItem = FindParent<ListBoxItem>((DependencyObject)e.OriginalSource);

        if (listBoxItem is null)
        {
            _favoriteContextMenuItem = null;
            return;
        }

        listBoxItem.IsSelected = true;
        _favoriteContextMenuItem = listBoxItem.DataContext as FavoriteTransactionItem;
    }
    private void ToggleFavoriteTransaction(string transactionCode)
    {
        var definition = _transactionDispatcher.FindDefinition(transactionCode);

        if (definition is null)
        {
            RenderTransactionResult(TransactionResult.Fail(
                transactionCode,
                $"Transakce {transactionCode} neexistuje."));
            return;
        }

        var existingItem = _userSettings.FavoriteTransactions.FirstOrDefault(item =>
            string.Equals(item, definition.Code, StringComparison.OrdinalIgnoreCase));

        if (existingItem is not null)
        {
            _userSettings.FavoriteTransactions.RemoveAll(item =>
                string.Equals(item, definition.Code, StringComparison.OrdinalIgnoreCase));

            _settingsService.Save(_userSettings);
            RefreshFavoritesList();
            RefreshModuleTransactionsList(GetSelectedModuleName());

            return;
        }

        _userSettings.FavoriteTransactions.Add(definition.Code);

        _settingsService.Save(_userSettings);
        RefreshFavoritesList();
        RefreshModuleTransactionsList(GetSelectedModuleName());
    }
    private void FavoriteItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem listBoxItem)
        {
            return;
        }

        if (listBoxItem.DataContext is not FavoriteTransactionItem item)
        {
            return;
        }

        ExecuteFavoriteTransaction(item);
    }

    private void ExecuteFavoriteTransaction(FavoriteTransactionItem item)
    {
        ExecuteTransaction(item.Code);
    }
    private static T? FindParent<T>(DependencyObject child)
    where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);

        while (parent is not null)
        {
            if (parent is T typedParent)
            {
                return typedParent;
            }

            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }
    private void RemoveFavoriteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var item = _favoriteContextMenuItem;

        if (item is null)
        {
            RenderSimplePage(
                "OblĂ­benĂ© transakce",
                "NejdĹ™Ă­v klikni pravĂ˝m tlaÄŤĂ­tkem na transakci, kterou chceĹˇ odebrat.");
            return;
        }

        _userSettings.FavoriteTransactions.RemoveAll(code =>
            string.Equals(code, item.Code, StringComparison.OrdinalIgnoreCase));

        _settingsService.Save(_userSettings);
        RefreshFavoritesList();

        RenderSimplePage(
            "OblĂ­benĂ© transakce",
            $"Transakce {item.Code} byla odebrĂˇna z oblĂ­benĂ˝ch.");

        _favoriteContextMenuItem = null;
    }
    private void ExecuteTransaction(string input)
    {
        var performanceStarted = Stopwatch.GetTimestamp();
        var performanceTransactionCode = TryGetTransactionCode(input);
        var performanceResult = "CANCELLED";

        try
        {
            var command = TransactionParser.Parse(input);

            performanceTransactionCode = command.Code;
            _logger.Transaction(input, _currentUser.DisplayName);

            if (!TryCompleteMissingParameter(command, out var completedCommand))
            {
                performanceResult = "CANCELLED";
                return;
            }

            performanceTransactionCode = completedCommand.Code;

            if (!UserCanExecuteTransaction(completedCommand.Code, out var authorizationMessage))
            {
                performanceResult = "DENIED";

                _logger.Warning($"ZamĂ­tnutĂ© spuĹˇtÄ›nĂ­ transakce {completedCommand.Code}: {authorizationMessage}");

                RenderTransactionResult(TransactionResult.Fail(
                    completedCommand.Code,
                    authorizationMessage));

                ClearTransactionInput();
                return;
            }

            var completedTransactionText = BuildTransactionText(completedCommand);

            RegisterNavigation(completedTransactionText);
            AddTransactionToHistory(completedTransactionText);
            ClearTransactionInput();

            if (completedCommand.Mode == "NewWindow")
            {
                OpenTransactionInNewWindow(completedCommand);
                performanceResult = "OK";
                return;
            }

            var result = _transactionDispatcher.Dispatch(completedCommand);
            RenderTransactionResult(result);

            performanceResult =
                result.Success
                    ? "OK"
                    : "FAIL";
        }
        catch (Exception ex)
        {
            performanceResult = "EXCEPTION";

            var transactionCode = TryGetTransactionCode(input);
            performanceTransactionCode = transactionCode;

            _logger.Error(
                $"NeoÄŤekĂˇvanĂˇ chyba pĹ™i spuĹˇtÄ›nĂ­ transakce {transactionCode}: {ex.Message}",
                ex);

            RenderTransactionResult(TransactionResult.Fail(
                transactionCode,
                $"NeoÄŤekĂˇvanĂˇ chyba pĹ™i spuĹˇtÄ›nĂ­ transakce:\n\n{ex.Message}"));

            ClearTransactionInput();
        }
        finally
        {
            DmsPerformanceService.Current.RecordTransaction(
                performanceTransactionCode,
                Stopwatch.GetElapsedTime(performanceStarted).TotalMilliseconds,
                performanceResult);
        }
    }

    private bool UserCanExecuteTransaction(string transactionCode, out string message)
    {
        message = string.Empty;

        var definition = _transactionDispatcher.FindDefinition(transactionCode);

        if (definition is null)
        {
            message = $"NeznĂˇmĂˇ transakce: {transactionCode}";
            return false;
        }

        if (!IsTransactionModuleActive(definition))
        {
            var module = FindConfiguredModule(definition.Module);
            message = $"Modul {module?.Name ?? definition.Module} je v SYS13 deaktivovaný.";
            return false;
        }

        if (definition.Roles.Count == 0)
        {
            return true;
        }

        if (_currentUser.HasAnyRole(definition.Roles))
        {
            return true;
        }

        message =
            $"NemĂˇte oprĂˇvnÄ›nĂ­ ke spuĹˇtÄ›nĂ­ transakce {transactionCode}.\n\n" +
            $"PoĹľadovanĂ© role: {string.Join(", ", definition.Roles)}\n" +
            $"VaĹˇe role: {string.Join(", ", _currentUser.Roles)}";

        return false;
    }

    private void AddFavoriteTransaction(string transactionCode)
    {
        var definition = _transactionDispatcher.FindDefinition(transactionCode);

        if (definition is null)
        {
            RenderTransactionResult(TransactionResult.Fail(
                transactionCode,
                $"Transakce {transactionCode} neexistuje, nelze ji pĹ™idat do oblĂ­benĂ˝ch."));
            return;
        }

        if (_userSettings.FavoriteTransactions.Any(item =>
                string.Equals(item, definition.Code, StringComparison.OrdinalIgnoreCase)))
        {
            RenderSimplePage(
                "OblĂ­benĂ© transakce",
                $"Transakce {definition.Code} uĹľ je v oblĂ­benĂ˝ch.");
            return;
        }

        _userSettings.FavoriteTransactions.Add(definition.Code);
        _settingsService.Save(_userSettings);

        RefreshFavoritesList();
        RefreshModuleTransactionsList(GetSelectedModuleName());

        RenderSimplePage(
            "OblĂ­benĂ© transakce",
            $"Transakce {definition.Code} byla pĹ™idĂˇna do oblĂ­benĂ˝ch.");
    }
    private bool TryCompleteMissingParameter(
    TransactionCommand command,
    out TransactionCommand completedCommand)
    {
        completedCommand = command;

        var definition = _transactionDispatcher.FindDefinition(command.Code);

        if (definition is null)
        {
            return true;
        }

        if (!definition.RequiresArticleNumber)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(command.Parameter))
        {
            return true;
        }

        var selectionConfig = GetSelectionConfig(command.Code);

        if (selectionConfig is null)
        {
            return false;
        }

        var storagePaths = new SapStoragePaths(GetDmsDataRootPath());

        var dialog = new ArticleNumberPromptWindow(
            selectionConfig.Value.MaterialKind,
            selectionConfig.Value.TitleKey,
            selectionConfig.Value.SubtitleKey,
            storagePaths,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args))
        {
            Owner = this
        };

        var dialogResult = dialog.ShowDialog();

        if (dialogResult != true || string.IsNullOrWhiteSpace(dialog.ArticleNumber))
        {
            return false;
        }

        completedCommand = new TransactionCommand
        {
            RawInput = command.RawInput,
            Mode = command.Mode,
            Code = command.Code,
            Parameter = dialog.ArticleNumber,
            Arguments = new[] { dialog.ArticleNumber }
        };

        return true;
    }

    private static string BuildTransactionText(TransactionCommand command)
    {
        var prefix = command.Mode switch
        {
            "Replace" => "/n",
            "NewWindow" => "/o",
            _ => string.Empty
        };
        var arguments = command.GetArguments();

        if (arguments.Count == 0)
        {
            return $"{prefix}{command.Code}";
        }

        return $"{prefix}{command.Code} {string.Join(" ", arguments)}";
    }

    private void OpenTransactionInNewWindow(TransactionCommand command)
    {
        var newWindow = new MainWindow();

        newWindow.Show();

        var commandForNewWindow = new TransactionCommand
        {
            RawInput = command.RawInput,
            Mode = "Current",
            Code = command.Code,
            Parameter = command.Parameter,
            Arguments = command.GetArguments().ToArray()
        };

        newWindow.SetTransactionInputText(BuildTransactionText(commandForNewWindow));

        var result = newWindow._transactionDispatcher.Dispatch(commandForNewWindow);
        newWindow.RenderTransactionResult(result);
    }

    private static string? GetDisplayTransactionForMaterialKind(string? materialKind)
    {
        return materialKind switch
        {
            nameof(SapMaterialKind.GlassArticle) => "ART03",
            nameof(SapMaterialKind.Recipe) => "REC03",
            _ => "SAP03"
        };
    }

    private static string GetMaterialKindDisplayName(string? materialKind)
    {
        return materialKind switch
        {
            nameof(SapMaterialKind.GlassArticle) => "sklenÄ›nĂ˝ artikl / flakon",
            nameof(SapMaterialKind.PurchasedPart) => "nakupovanĂ˝ dĂ­l",
            nameof(SapMaterialKind.Packaging) => "obalovĂ˝ materiĂˇl",
            nameof(SapMaterialKind.Recipe) => "receptura",
            nameof(SapMaterialKind.AssemblyPart) => "kompletaÄŤnĂ­ dĂ­l",
            nameof(SapMaterialKind.ToolFixture) => "pĹ™Ă­pravek",
            nameof(SapMaterialKind.Ignored) => "ignorovanĂ˝ SAP materiĂˇl",
            _ => "neznĂˇmĂ˝ typ materiĂˇlu"
        };
    }

    private static string GetPackagingKindDisplayName(string? packagingKind)
    {
        return packagingKind switch
        {
            "PackagingSetOldReference" => "BalicĂ­ sada - vazba podle starĂ©ho ÄŤĂ­sla",
            "PackagingSetSapReference" => "BalicĂ­ sada - vazba podle SAP ÄŤĂ­sla",
            "PackagingComponent" => "Komponenta balicĂ­ sady",
            _ => "NeznĂˇmĂ˝ typ obalu"
        };
    }
    private void RenderTransactionResult(TransactionResult result)
    {
        WorkspacePanel.Children.Clear();

        if (!result.Success)
        {
            _logger.Warning($"Chyba transakce {result.TransactionCode}: {result.Message}");
            var panel = CreateWorkspaceStack();

            panel.Children.Add(new TextBlock
            {
                Text = "Chyba transakce",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.IndianRed,
                Margin = new Thickness(0, 0, 0, 16)
            });

            panel.Children.Add(CreateBodyText(result.Message));
            ResetWorkspaceScroll();

            return;
        }

        switch (result.TransactionCode)
        {
            case "ART01":
                RenderArticleCreate();
                break;

            case "ART02":
                RenderArticleEdit(result.Parameter ?? string.Empty);
                break;

            case "ART03":
                RenderArticleCard(result.Parameter ?? string.Empty);
                break;

            case "DOC01":
                RenderArticleDocumentCreate(result.Parameter ?? string.Empty);
                break;

            case "DOC02":
                RenderArticleDocumentEdit(result.Parameter ?? string.Empty);
                break;

            case "DOC03":
                RenderArticleDocuments(result.Parameter ?? string.Empty);
                break;

            case "SCR03":
                RenderSimplePage("SĂ­ta artiklu", result.Message);
                break;

            case "SCR10":
                RenderSimplePage("Fronta pĹ™Ă­pravy sĂ­t", result.Message);
                break;

            case "ORD10":
                RenderSimplePage("PĹ™ehled zakĂˇzek", result.Message);
                break;

            case "WHOAMI":
                RenderSimplePage("AktuĂˇlnĂ­ uĹľivatel", result.Message);
                break;

            case "HELP":
                RenderHelp();
                break;

            case "SET01":
            case "CLSET":
                RenderClientSettings();
                break;

            case "USR01":
                RenderUserManagement();
                break;

            case "SYS01":
                RenderSystemSettings();
                break;

            case "SYS03":
                RenderSystemConfiguration();
                break;

            case "SYS11":
                RenderTransactionManagement();
                break;

            case "SYS12":
                RenderRoleManagement();
                break;

            case "SYS13":
                RenderModuleManagement();
                break;

            case "FW01":
            case "FW02":
                RenderFrameworkHub(result.TransactionCode);
                break;

            case "FW06":
                RenderFrameworkSecurity();
                break;

            case "FW07":
                RenderFrameworkWorkflow();
                break;

            case "FW08":
                RenderFrameworkPerformance();
                break;

            case "FW09":                 RenderFrameworkMasterData();                 break; 
                RenderFrameworkHub(result.TransactionCode);
                break;

            case "FW03":
                RenderFrameworkRuntimeConfiguration();
                break;

            case "FW04":
                RenderFrameworkDiagnostics();
                break;

            case "FW05":
                RenderFrameworkAuditLogging();
                break;

            case "LOG03":
                RenderLogViewer();
                break;

            case "SAPSET":
                RenderSapSettings();
                break;

            case "SAP00":
                RenderSapCockpit();
                break;

            case "SAP03":
                RenderSapMaterialDisplay(result.Parameter ?? string.Empty);
                break;

            case "MAT03":
                RenderMaterialUsage(result.Parameter ?? string.Empty);
                break;

            case "REC03":
                RenderRecipeOverview(result.Parameter ?? string.Empty);
                break;

            case "TEC03":
                RenderTechnicalArticleSummary(result.Parameter ?? string.Empty);
                break;

            case "QAMENU":
                RenderQualityMenu();
                break;

            case "QASET":
                RenderQualitySettings();
                break;

            case "QA00":
                RenderQualityCockpit();
                break;

            case "QA01":
                RenderQualityArticleCreate(result.Parameter ?? string.Empty);
                break;

            case "QA02":
                RenderQualityArticleEditWithCreatePrompt(result.Parameter ?? string.Empty);
                break;

            case "QA03":
                RenderQualityArticle(result.Parameter ?? string.Empty);
                break;

            case "QA05":
                RenderQualityPrintVersions();
                break;

            case "QATASK":
                RenderQualityTasksOverview();
                break;

            case "QO01":
                RenderQualityOrderCreate(result.Parameter ?? string.Empty);
                break;
            case "QO02":
                RenderQualityOrderEdit(result.Parameter ?? string.Empty);
                break;
            case "QO03":
                RenderQualityOrderDisplay(result.Parameter ?? string.Empty);
                break;
            case "QO05":
                RenderQualityOrderList();
                break;
            case "QO06":
                RenderQualityOrderRelease(result.Parameter ?? string.Empty);
                break;
            case "MES00":
                RenderMesCommunicationSettings();
                break;

            case "MES02":
                RenderMesDeviceEditor();
                break;

            case "MES03":
                RenderMesStationData();
                break;

            case "MES05":
                RenderMesWorkplaceOverview();
                break;
            case "MESDPM":
                RenderMesDataPointMonitor(result.Parameter);
                break;
            case "CHLSET":
                RenderChecklistSettings();
                break;

            case "CHL00":
            case "CHL01":
            case "CHL02":
            case "CHL03":
            case "CHL04":
            case "CHL05":
            case "CHL06":
                RenderChecklistWorkspace(result.TransactionCode, result.Arguments);
                break;
            default:
                RenderSimplePage(result.TransactionCode, result.Message);
                break;
        }
        ResetWorkspaceScroll();
    }
    private void RenderTypedSapMaterialDisplay(
    string materialNumber,
    string expectedMaterialKind,
    string title)
    {
        var panel = CreateWorkspaceStack();

        panel.Children.Add(CreateTitle(title));

        try
        {
            var storagePaths = new SapStoragePaths(GetDmsDataRootPath());
            var repository = new JsonSapMaterialRepository(storagePaths.SapMaterialsFilePath);

            var material = repository.FindByMaterialNumber(materialNumber);

            if (material is null)
            {
                panel.Children.Add(CreateArticleWarning(
                    "MateriĂˇl nenalezen",
                    $"SAP materiĂˇl {materialNumber} nebyl nalezen v SAP mirror cache.\n\n" +
                    $"Soubor:\n{storagePaths.SapMaterialsFilePath}\n\n" +
                    "NejdĹ™Ă­v proveÄŹ import pĹ™es SAP00."));
                return;
            }

            if (material.PackagingInfo is not null)
            {
                panel.Children.Add(CreateArticleSectionTitle("BalicĂ­ vazba"));

                panel.Children.Add(CreateArticleFullLine(
                    "Typ obalu",
                    GetPackagingKindDisplayName(material.PackagingInfo.PackagingKind)));

                if (!string.IsNullOrWhiteSpace(material.PackagingInfo.LinkedArticleSapNumber))
                {
                    panel.Children.Add(CreateArticleFullLine(
                        "Vazba na SAP artikl",
                        material.PackagingInfo.LinkedArticleSapNumber));
                }

                if (!string.IsNullOrWhiteSpace(material.PackagingInfo.LinkedArticleOldNumber))
                {
                    panel.Children.Add(CreateArticleFullLine(
                        "Vazba na starĂ© ÄŤĂ­slo artiklu",
                        material.PackagingInfo.LinkedArticleOldNumber));
                }
            }

            if (!string.Equals(material.MaterialKind, expectedMaterialKind, StringComparison.OrdinalIgnoreCase))
            {
                var correctTransaction = GetDisplayTransactionForMaterialKind(material.MaterialKind);

                if (!string.IsNullOrWhiteSpace(correctTransaction))
                {
                    panel.Children.Add(CreateArticleWarning(
                        "PĹ™esmÄ›rovĂˇnĂ­ na sprĂˇvnou transakci",
                        $"ZadanĂ˝ materiĂˇl {material.MaterialNumber} nenĂ­ typ " +
                        $"{GetMaterialKindDisplayName(expectedMaterialKind)}, ale {GetMaterialKindDisplayName(material.MaterialKind)}.\n\n" +
                        $"OtevĂ­rĂˇm sprĂˇvnou transakci: {correctTransaction} {material.MaterialNumber}"));

                    ExecuteTransaction($"{correctTransaction} {material.MaterialNumber}");
                    return;
                }

                panel.Children.Add(CreateArticleWarning(
                    "NesprĂˇvnĂ˝ typ materiĂˇlu",
                    $"ZadanĂ˝ materiĂˇl {material.MaterialNumber} mĂˇ typ {material.MaterialKind}, " +
                    $"kterĂ˝ nemĂˇ pĹ™iĹ™azenou nĂˇhledovou transakci.\n\n" +
                    "Pro obecnĂ˝ nĂˇhled pouĹľij SAP03."));
                return;
            }

            panel.Children.Add(CreateMaterialHeaderCard(material, title));

            panel.Children.Add(CreateArticleSectionTitle("SAP zĂˇklad"));
            panel.Children.Add(CreateArticleTwoColumnLine("SAP ÄŤĂ­slo", material.MaterialNumber, "Status", NullDash(material.MaterialStatus)));
            panel.Children.Add(CreateArticleTwoColumnLine("StarĂ© ÄŤĂ­slo", NullDash(material.OldMaterialNumber), "Typ v DMS", material.MaterialKind));
            panel.Children.Add(CreateArticleTwoColumnLine("Prefix", NullDash(material.TransactionPrefix), "ImportovĂˇno", material.ImportedAt.ToString("dd.MM.yyyy HH:mm:ss")));
            panel.Children.Add(CreateArticleFullLine("OznaÄŤenĂ­", material.Description));

            if (!string.IsNullOrWhiteSpace(material.ToolFixtureKind))
            {
                panel.Children.Add(CreateArticleSectionTitle("Klasifikace pĹ™Ă­pravku"));
                panel.Children.Add(CreateArticleFullLine("Druh pĹ™Ă­pravku", material.ToolFixtureKind));
            }

            panel.Children.Add(CreateArticleSectionTitle("DMS vazby"));

            var linksGrid = new UniformGrid
            {
                Columns = 3,
                Margin = new Thickness(0, 4, 0, 0)
            };

            switch (expectedMaterialKind)
            {
                case nameof(SapMaterialKind.PurchasedPart):
                    linksGrid.Children.Add(CreateArticleLinkTile("PouĹľitĂ­ v kusovnĂ­cĂ­ch", "BOM", "Kde je dĂ­l pouĹľitĂ˝"));
                    linksGrid.Children.Add(CreateArticleLinkTile("Dokumentace", "DOC03", "TechnickĂ© listy, specifikace"));
                    linksGrid.Children.Add(CreateArticleLinkTile("PoznĂˇmky", "DMS", "LokĂˇlnĂ­ poznĂˇmky k dĂ­lu"));
                    break;

                case nameof(SapMaterialKind.Recipe):
                    linksGrid.Children.Add(CreateArticleLinkTile("PouĹľitĂ­ receptury", "REC", "Artikly pouĹľĂ­vajĂ­cĂ­ recepturu"));
                    linksGrid.Children.Add(CreateArticleLinkTile("Dokumentace", "DOC03", "Receptura, schvĂˇlenĂ­, verze"));
                    linksGrid.Children.Add(CreateArticleLinkTile("KusovnĂ­ky", "BOM", "VĂ˝skyt v SAP kusovnĂ­cĂ­ch"));
                    break;

                case nameof(SapMaterialKind.AssemblyPart):
                    linksGrid.Children.Add(CreateArticleLinkTile("PouĹľitĂ­ v kompletaci", "KOM", "Vazby na lepenĂ­/kompletaci"));
                    linksGrid.Children.Add(CreateArticleLinkTile("KusovnĂ­ky", "BOM", "VĂ˝skyt v SAP kusovnĂ­cĂ­ch"));
                    linksGrid.Children.Add(CreateArticleLinkTile("Dokumentace", "DOC03", "VĂ˝kresy, schvĂˇlenĂ­, specifikace"));
                    break;

                case nameof(SapMaterialKind.ToolFixture):
                    linksGrid.Children.Add(CreateArticleLinkTile("PouĹľitĂ­ pĹ™Ă­pravku", "PRIP", "Artikly a operace pouĹľĂ­vajĂ­cĂ­ pĹ™Ă­pravek"));
                    linksGrid.Children.Add(CreateArticleLinkTile("Dokumentace", "DOC03", "VĂ˝kresy, ĂşdrĹľba, nastavenĂ­"));
                    linksGrid.Children.Add(CreateArticleLinkTile("PracovnĂ­ postupy", "RTG", "Vazby na operace"));
                    break;
            }

            panel.Children.Add(linksGrid);
        }
        catch (Exception ex)
        {
            panel.Children.Add(CreateArticleWarning(
                $"{title} se nepodaĹ™ilo naÄŤĂ­st",
                ex.Message));
        }
    }
    private static TextBlock CreateFilterLabel(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            FontWeight = FontWeights.SemiBold
        };

        label.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        return label;
    }
    private void RenderArticleCard(string articleNumber)
    {
        var panel = CreateWorkspaceStack();

        panel.Children.Add(CreateTitle("ART03 - Artikelmapa"));

        try
        {
            var storagePaths = new SapStoragePaths(GetDmsDataRootPath());
            var repository = new JsonSapMaterialRepository(storagePaths.SapMaterialsFilePath);

            var material = repository.FindByMaterialNumber(articleNumber);

            if (material is null)
            {
                panel.Children.Add(CreateArticleWarning(
                    "Artikl nenalezen",
                    $"SAP artikl {articleNumber} nebyl nalezen v SAP mirror cache.\n\n" +
                    $"Soubor:\n{storagePaths.SapMaterialsFilePath}\n\n" +
                    "NejdĹ™Ă­v proveÄŹ import pĹ™es SAP00."));
                return;
            }

            if (!string.Equals(material.MaterialKind, nameof(SapMaterialKind.GlassArticle), StringComparison.OrdinalIgnoreCase))
            {
                var correctTransaction = GetDisplayTransactionForMaterialKind(material.MaterialKind);

                if (!string.IsNullOrWhiteSpace(correctTransaction))
                {
                    panel.Children.Add(CreateArticleWarning(
                        "PĹ™esmÄ›rovĂˇnĂ­ na sprĂˇvnou transakci",
                        $"MateriĂˇl {material.MaterialNumber} nenĂ­ sklenÄ›nĂ˝ artikl / flakon, " +
                        $"ale {GetMaterialKindDisplayName(material.MaterialKind)}.\n\n" +
                        $"OtevĂ­rĂˇm sprĂˇvnou transakci: {correctTransaction} {material.MaterialNumber}"));

                    ExecuteTransaction($"{correctTransaction} {material.MaterialNumber}");
                    return;
                }

                panel.Children.Add(CreateArticleWarning(
                    "NejednĂˇ se o sklenÄ›nĂ˝ artikl",
                    $"MateriĂˇl {material.MaterialNumber} nenĂ­ sklenÄ›nĂ˝ artikl / flakon.\n\n" +
                    $"Typ v DMS: {material.MaterialKind}\n\n" +
                    "Pro obecnĂ˝ SAP nĂˇhled pouĹľij SAP03."));
                return;
            }

            panel.Children.Add(CreateArticleHeaderCard(material));

            panel.Children.Add(CreateArticleSectionTitle("SAP zĂˇklad"));
            panel.Children.Add(CreateArticleTwoColumnLine("SAP ÄŤĂ­slo", material.MaterialNumber, "Status", NullDash(material.MaterialStatus)));
            panel.Children.Add(CreateArticleTwoColumnLine("StarĂ© ÄŤĂ­slo", NullDash(material.OldMaterialNumber), "Typ v DMS", material.MaterialKind));
            panel.Children.Add(CreateArticleFullLine("OznaÄŤenĂ­", material.Description));

            if (material.GlassInfo is not null)
            {
                panel.Children.Add(CreateArticleSectionTitle("Rozpad oznaÄŤenĂ­"));
                panel.Children.Add(CreateArticleTwoColumnLine("Forma", NullDash(material.GlassInfo.MoldNumber), "Typ skla", NullDash(material.GlassInfo.GlassTypeNumber)));
                panel.Children.Add(CreateArticleTwoColumnLine("Objem", FormatVolume(material.GlassInfo.VolumeMl), "Dekorace", NullDash(material.GlassInfo.DecorationChain)));
                panel.Children.Add(CreateArticleFullLine("Popis", NullDash(material.GlassInfo.RemainingDescription)));

                panel.Children.Add(CreateArticleSectionTitle("DekoraÄŤnĂ­ tok"));
                panel.Children.Add(CreateDecorationFlow(material.GlassInfo.DecorationSteps));
            }
            else
            {
                panel.Children.Add(CreateArticleWarning(
                    "OznaÄŤenĂ­ se nepodaĹ™ilo rozparsovat",
                    "KrĂˇtkĂ˝ text neodpovĂ­dĂˇ oÄŤekĂˇvanĂ©mu formĂˇtu:\n" +
                    "<forma> <typ skla> <objem> <dekorace> <popis>"));
            }

            panel.Children.Add(CreateArticleSectionTitle("DMS vazby"));

            var linksGrid = new UniformGrid
            {
                Columns = 3,
                Margin = new Thickness(0, 4, 0, 0)
            };

            linksGrid.Children.Add(CreateArticleLinkTile("Dokumentace", "DOC03", "VĂ˝kresy, MB, tiskovĂ© oblasti"));
            linksGrid.Children.Add(CreateArticleLinkTile("Receptury", "REC03", "SAP receptury a DMS vazby"));
            linksGrid.Children.Add(CreateArticleLinkTile("SĂ­ta", "SCR03", "SĂ­ta a pĹ™Ă­prava sĂ­t"));
            linksGrid.Children.Add(CreateArticleLinkTile("KusovnĂ­k", "BOM03", "SAP snapshot kusovnĂ­ku"));
            linksGrid.Children.Add(CreateArticleLinkTile("Postup", "RTG03", "SAP snapshot pracovnĂ­ho postupu"));
            linksGrid.Children.Add(CreateArticleLinkTile("PĹ™Ă­pravky", "PRIP03", "NĂˇstroje a pĹ™Ă­pravky"));

            panel.Children.Add(linksGrid);
        }
        catch (Exception ex)
        {
            panel.Children.Add(CreateArticleWarning(
                "ART03 se nepodaĹ™ilo naÄŤĂ­st",
                ex.Message));
        }
    }

    private static string NullDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "-"
            : value;
    }

    private static string FormatVolume(int? volumeMl)
    {
        return volumeMl.HasValue
            ? $"{volumeMl.Value} ml"
            : "-";
    }

    private string GetDecorationName(string? code)
    {
        return _decorationRuleService.GetName(code);
    }

    private void RenderArticleCreate()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(new ArticleEditView(
            article: null,
            saveArticle: SaveArticleFromView,
            currentUserName: _currentUser.DisplayName));

        ResetWorkspaceScroll();
    }

    private void RenderArticleEdit(string articleNumber)
    {
        WorkspacePanel.Children.Clear();

        var article = _articleRepository.FindBySapNumber(articleNumber);

        if (article is null)
        {
            RenderSimplePage(
                "Artikl nenalezen",
                $"Artikl {articleNumber} nebyl nalezen v DMS. PouĹľij ART01 pro zaloĹľenĂ­.");
            return;
        }

        WorkspacePanel.Children.Add(new ArticleEditView(
            article,
            SaveArticleFromView,
            _currentUser.DisplayName));

        ResetWorkspaceScroll();
    }

    private void RenderArticleDetail(string articleNumber)
    {
        WorkspacePanel.Children.Clear();

        var article = _articleRepository.FindBySapNumber(articleNumber);

        if (article is null)
        {
            RenderSimplePage(
                "Artikl nenalezen",
                $"Artikl {articleNumber} nebyl nalezen v DMS.");
            return;
        }

        WorkspacePanel.Children.Add(new ArticleDetailView(article));

        ResetWorkspaceScroll();
    }

    private void SaveArticleFromView(DmsArticle article)
    {
        _articleRepository.Save(article);

        _logger.Info($"UloĹľen artikl {article.SapArticleNumber}; uĹľivatel: {_currentUser.DisplayName}");

        RenderArticleDetail(article.SapArticleNumber);
    }
    private void RenderMesDataPointMonitor(string? query)
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(
            new MesDataPointMonitorView(
                query,
                _appSettings.ConfigurationRootPath,
                _logger,
                _currentUser.DisplayName,
                key => T(key)));

        ResetWorkspaceScroll();
    }
    private void RenderSimplePage(string title, string message)
    {
        var panel = CreateWorkspaceStack();

        panel.Children.Add(CreateTitle(title));
        panel.Children.Add(CreateBodyText(message));
        ResetWorkspaceScroll();
    }

    private void RenderClientSettings()
    {
        WorkspacePanel.Children.Clear();

        var view = new ClientSettingsView(
            _userSettings,
            ApplyTheme,
            ReloadLocalizationFromUserSettings,
            SaveUserSettings,
            key => T(key),
            (key, args) => T(key, args),
            (action, details) =>
            {
                _logger.AdminAction(
                    "CLSET",
                    action,
                    _currentUser.DisplayName,
                    details);
            });

        WorkspacePanel.Children.Add(view);

        ResetWorkspaceScroll();
    }

    private void SaveUserSettings()
    {
        _settingsService.Save(_userSettings);
        RefreshTransactionHistoryList();
        RefreshFavoritesList();
        RefreshModuleTransactionsList(GetSelectedModuleName());
    }

    private static TextBlock CreateLine(string text)
    {
        return CreateBodyText(text);
    }

    private static Border CreateSapLikeSectionHeader(string text)
    {
        var border = new Border
        {
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 16, 0, 8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromRgb(42, 57, 72)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(86, 112, 137))
        };

        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(207, 230, 255))
        };

        border.Child = textBlock;

        return border;
    }

    private static Grid CreateSapLikeLine(string label, string value)
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 3, 0, 3)
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(170)
        });

        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        var labelBlock = new TextBlock
        {
            Text = label + ":",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 4, 12, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 205, 220))
        };

        var valueBorder = new Border
        {
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 1, 8, 1),
            MinHeight = 24,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(31, 42, 53)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(73, 94, 115))
        };

        var valueBlock = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "-" : value,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(245, 248, 252))
        };

        valueBorder.Child = valueBlock;

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBorder, 1);

        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBorder);

        return grid;
    }

    private static Border CreateSapLikeSeparator()
    {
        return new Border
        {
            Height = 1,
            Margin = new Thickness(0, 14, 0, 10),
            Background = new SolidColorBrush(Color.FromRgb(78, 96, 116))
        };
    }
    private static Border CreateSapLikeInfoBar(string text)
    {
        var border = new Border
        {
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromRgb(50, 73, 94)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(92, 125, 155)),
            BorderThickness = new Thickness(1)
        };

        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(230, 242, 255))
        };

        border.Child = textBlock;

        return border;
    }

    private static Border CreateArticleHeaderCard(SapMaterial material)
    {
        var border = new Border
        {
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 18),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1)
        };

        border.SetResourceReference(Border.BackgroundProperty, "DmsPanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

        var stack = new StackPanel();

        var title = new TextBlock
        {
            Text = material.MaterialNumber,
            FontSize = 28,
            FontWeight = FontWeights.Bold
        };

        title.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        var subtitle = new TextBlock
        {
            Text = material.Description,
            FontSize = 17,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        subtitle.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsMutedForegroundBrush");

        var badgePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 14, 0, 0)
        };

        badgePanel.Children.Add(CreateArticleBadge(material.MaterialKind));
        badgePanel.Children.Add(CreateArticleBadge($"Status {NullDash(material.MaterialStatus)}"));

        if (!string.IsNullOrWhiteSpace(material.GlassInfo?.DecorationChain))
        {
            badgePanel.Children.Add(CreateArticleBadge($"Dekorace {material.GlassInfo.DecorationChain}"));
        }

        stack.Children.Add(title);
        stack.Children.Add(subtitle);
        stack.Children.Add(badgePanel);

        border.Child = stack;
        return border;
    }
    private static Border CreateArticleBadge(string text)
    {
        var border = new Border
        {
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1)
        };

        border.SetResourceReference(Border.BackgroundProperty, "DmsBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold
        };

        textBlock.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        border.Child = textBlock;
        return border;
    }

    private static Border CreateArticleSectionTitle(string text)
    {
        var border = new Border
        {
            Padding = new Thickness(0, 0, 0, 6),
            Margin = new Thickness(0, 18, 0, 10),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

        border.Child = new TextBlock
        {
            Text = text,
            FontSize = 20,
            FontWeight = FontWeights.Bold
        };

        ((TextBlock)border.Child).SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        return border;
    }

    private static Grid CreateArticleTwoColumnLine(string leftLabel, string leftValue, string rightLabel, string rightValue)
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 3, 0, 3)
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = CreateArticleField(leftLabel, leftValue);
        var right = CreateArticleField(rightLabel, rightValue);

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);

        grid.Children.Add(left);
        grid.Children.Add(right);

        return grid;
    }

    private static Border CreateArticleFullLine(string label, string value)
    {
        return CreateArticleField(label, value);
    }

    private static Border CreateArticleField(string label, string value)
    {
        var border = new Border
        {
            Padding = new Thickness(12),
            Margin = new Thickness(0, 2, 8, 6),
            MinHeight = 52,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1)
        };

        border.SetResourceReference(Border.BackgroundProperty, "DmsPanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

        var stack = new StackPanel();

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };

        labelBlock.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsMutedForegroundBrush");

        var valueBlock = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "-" : value,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        };

        valueBlock.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        stack.Children.Add(labelBlock);
        stack.Children.Add(valueBlock);

        border.Child = stack;
        return border;
    }

    private StackPanel CreateDecorationFlow(List<string> steps)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0)
        };

        if (steps.Count == 0)
        {
            stack.Children.Add(CreateArticleBadge("Dekorace nerozpoznĂˇna"));
            return stack;
        }

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            stack.Children.Add(CreateDecorationStep(step));

            if (i < steps.Count - 1)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "â†’",
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0),
                    Foreground = new SolidColorBrush(Color.FromRgb(207, 230, 255))
                });
            }
        }

        return stack;
    }

    private Border CreateDecorationStep(string code)
    {
        var border = new Border
        {
            Width = 130,
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(31, 42, 53)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(92, 125, 155))
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };

        stack.Children.Add(new TextBlock
        {
            Text = code,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(230, 242, 255))
        });

        stack.Children.Add(new TextBlock
        {
            Text = GetDecorationName(code),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 205, 220))
        });

        border.Child = stack;
        return border;
    }

    private static Border CreateArticleLinkTile(string title, string transaction, string description)
    {
        var border = new Border
        {
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 10, 10),
            MinHeight = 85,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(31, 42, 53)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(73, 94, 115))
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = $"{transaction} - {title}",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(230, 242, 255))
        });

        stack.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 13,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(175, 195, 215))
        });

        border.Child = stack;
        return border;
    }

    private static Border CreateArticleWarning(string title, string message)
    {
        var border = new Border
        {
            Padding = new Thickness(14),
            Margin = new Thickness(0, 12, 0, 0),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(68, 48, 35)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(150, 105, 70))
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 225, 190))
        });

        stack.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 14,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 240, 220))
        });

        border.Child = stack;
        return border;
    }

    private static Border CreateMaterialHeaderCard(SapMaterial material, string title)
    {
        var border = new Border
        {
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 18),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1)
        };

        border.SetResourceReference(Border.BackgroundProperty, "DmsPanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

        var stack = new StackPanel();

        var smallTitle = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };

        smallTitle.SetResourceReference(TextBlock.ForegroundProperty, "DmsMutedForegroundBrush");

        var number = new TextBlock
        {
            Text = material.MaterialNumber,
            FontSize = 28,
            FontWeight = FontWeights.Bold
        };

        number.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        var description = new TextBlock
        {
            Text = material.Description,
            FontSize = 17,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        description.SetResourceReference(TextBlock.ForegroundProperty, "DmsMutedForegroundBrush");

        var badgePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 14, 0, 0)
        };

        badgePanel.Children.Add(CreateArticleBadge(material.MaterialKind));
        badgePanel.Children.Add(CreateArticleBadge($"Status {NullDash(material.MaterialStatus)}"));

        if (!string.IsNullOrWhiteSpace(material.TransactionPrefix))
        {
            badgePanel.Children.Add(CreateArticleBadge($"Prefix {material.TransactionPrefix}"));
        }

        if (!string.IsNullOrWhiteSpace(material.ToolFixtureKind))
        {
            badgePanel.Children.Add(CreateArticleBadge(material.ToolFixtureKind));
        }

        stack.Children.Add(smallTitle);
        stack.Children.Add(number);
        stack.Children.Add(description);
        stack.Children.Add(badgePanel);

        border.Child = stack;
        return border;
    }

    private static (string? MaterialKind, string TitleKey, string SubtitleKey)? GetSelectionConfig(string transactionCode)
    {
        return transactionCode.ToUpperInvariant() switch
        {
            "SAP03" => (
                null,
                "ArticleSelection.Transaction.SAP03.Title",
                "ArticleSelection.Transaction.SAP03.Subtitle"),

            "MAT03" => (
                null,
                "ArticleSelection.Transaction.MAT03.Title",
                "ArticleSelection.Transaction.MAT03.Subtitle"),

            "TEC03" => (
                nameof(SapMaterialKind.GlassArticle),
                "ArticleSelection.Transaction.TEC03.Title",
                "ArticleSelection.Transaction.TEC03.Subtitle"),

            "ART03" => (
                nameof(SapMaterialKind.GlassArticle),
                "ArticleSelection.Transaction.ART03.Title",
                "ArticleSelection.Transaction.ART03.Subtitle"),

            "DOC01" => (
                nameof(SapMaterialKind.GlassArticle),
                "ArticleSelection.Transaction.DOC01.Title",
                "ArticleSelection.Transaction.DOC01.Subtitle"),

            "DOC02" => (
                nameof(SapMaterialKind.GlassArticle),
                "ArticleSelection.Doc02.Title",
                "ArticleSelection.Doc02.Subtitle"),

            "DOC03" => (
                nameof(SapMaterialKind.GlassArticle),
                "ArticleSelection.Transaction.DOC03.Title",
                "ArticleSelection.Transaction.DOC03.Subtitle"),

            "REC03" => (
                nameof(SapMaterialKind.Recipe),
                "ArticleSelection.Transaction.REC03.Title",
                "ArticleSelection.Transaction.REC03.Subtitle"),

            "QA01" => (
                null,
                "ArticleSelection.Transaction.QA01.Title",
                "ArticleSelection.Transaction.QA01.Subtitle"),

            "QA02" => (
                null,
                "ArticleSelection.Transaction.QA02.Title",
                "ArticleSelection.Transaction.QA02.Subtitle"),

            "QA03" => (
                null,
                "ArticleSelection.Transaction.QA03.Title",
                "ArticleSelection.Transaction.QA03.Subtitle"),

            "QO01" => (
                nameof(SapMaterialKind.GlassArticle),
                "ArticleSelection.Transaction.QO01.Title",
                "ArticleSelection.Transaction.QO01.Subtitle"),
            _ => null
        };
    }


    private void BtnToggleLeftPanel_Click(object sender, RoutedEventArgs e)
    {
        ToggleLeftPanel();
    }

    private void EnsureLeftPanelVisibleOnStartup()
    {
        _isLeftPanelVisible = true;

        LeftPanelColumn.MinWidth = 350;
        LeftPanelColumn.Width = new GridLength(283);
        LeftPanelSplitterColumn.Width = new GridLength(5);

        LeftMenuPanel.Visibility = Visibility.Visible;
        LeftPanelSplitter.Visibility = Visibility.Visible;

        BtnToggleLeftPanel.Content = "â°";
        BtnToggleLeftPanel.ToolTip = T("Shell.HideLeftPanel");
    }

    private void ToggleLeftPanel()
    {
        if (_isLeftPanelVisible)
        {
            if (LeftPanelColumn.Width.Value > 0)
            {
                _lastLeftPanelWidth = LeftPanelColumn.Width;
            }

            LeftMenuPanel.Visibility = Visibility.Collapsed;
            LeftPanelSplitter.Visibility = Visibility.Collapsed;

            LeftPanelColumn.MinWidth = 0;
            LeftPanelColumn.Width = new GridLength(0);
            LeftPanelSplitterColumn.Width = new GridLength(0);

            _isLeftPanelVisible = false;

            BtnToggleLeftPanel.Content = "â°";
            BtnToggleLeftPanel.ToolTip = T("Shell.ShowLeftPanel");

            return;
        }

        LeftPanelColumn.MinWidth = 320;
        LeftPanelColumn.Width = _lastLeftPanelWidth.Value > 0
            ? _lastLeftPanelWidth
            : new GridLength(283);

        LeftPanelSplitterColumn.Width = new GridLength(5);

        LeftMenuPanel.Visibility = Visibility.Visible;
        LeftPanelSplitter.Visibility = Visibility.Visible;

        _isLeftPanelVisible = true;

        BtnToggleLeftPanel.Content = "â°";
        BtnToggleLeftPanel.ToolTip = T("Shell.HideLeftPanel");
    }

    private void LeftMenuScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(
            scrollViewer.VerticalOffset - e.Delta);

        e.Handled = true;
    }

    private void LeftMenuChild_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        LeftMenuScrollViewer.ScrollToVerticalOffset(
            LeftMenuScrollViewer.VerticalOffset - e.Delta);

        e.Handled = true;
    }

    private void UpdateCurrentTransactionText(string? transactionText)
    {
        if (TxtCurrentTransaction is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(transactionText))
        {
            TxtCurrentTransaction.Text = T("Shell.NoTransaction");
            return;
        }

        TxtCurrentTransaction.Text = T("Shell.CurrentTransaction", transactionText);
    }

    private string T(string key)
    {
        return _localizationService?.Translate(key) ?? key;
    }

    private string T(string key, params object[] args)
    {
        return _localizationService?.Translate(key, args) ?? key;
    }

    private void ApplyLocalization()
    {
        TxtAppTitle.Text = T("App.Title");
        TxtAppSubtitle.Text = T("App.Subtitle");

        TxtTransactionLabel.Text = T("Shell.TransactionLabel");

        BtnBack.ToolTip = T("Shell.Back");
        BtnForward.ToolTip = T("Shell.Forward");
        BtnRefreshTransaction.ToolTip = T("Shell.Refresh");
        BtnToggleLeftPanel.ToolTip = _isLeftPanelVisible
            ? T("Shell.HideLeftPanel")
            : T("Shell.ShowLeftPanel");

        TxtFavoritesTitle.Text = T("Menu.Favorites");
        TxtModulesTitle.Text = T("Menu.Modules");
        TxtModuleTransactionsTitle.Text = T("Menu.ModuleTransactions");

        MnuRemoveFavorite.Header = T("Menu.RemoveFavorite");

        StatusDatabase.Content = T("Status.DatabaseLocalMode");
        StatusSap.Content = T("Status.SapTestDisconnected");
        StatusMes.Content = T("Status.MesImportNotReady");
        StatusSso.Content = T("Status.SsoWindowsLogin");
        StatusVersion.Content = T("Status.Version", "0.1");

        TxtWelcomeTitle.Text = T("Welcome.Title");
        TxtWelcomeSubtitle.Text = T("Welcome.Subtitle");

        UpdateCurrentUserText();
        UpdateCurrentTransactionText(_currentTransactionCommand);
    }

    private string GetConfigPath(string fileName)
    {
        return Path.Combine(
            _appSettings.ConfigurationRootPath,
            fileName);
    }

    private string GetDmsDataRootPath()
    {
        return Path.GetFullPath(
            Path.Combine(_appSettings.ConfigurationRootPath, ".."));
    }
    private string GetDataPath(string fileName)
    {
        return Path.Combine(
            GetDmsDataRootPath(),
            "Data",
            fileName);
    }
    private static string TryGetTransactionCode(string input)
    {
        try
        {
            return TransactionParser.Parse(input).Code;
        }
        catch
        {
            return "UNKNOWN";
        }
    }
    private void ReloadLocalizationFromUserSettings()
    {
        _localizationService.Load(
            _userSettings.LanguageMode,
            _userSettings.CultureName);

        ApplyLocalization();
        RefreshLocalizedTransactionNavigation();

        UpdateCurrentUserText();
        UpdateCurrentTransactionText(_currentTransactionCommand);
    }

}



