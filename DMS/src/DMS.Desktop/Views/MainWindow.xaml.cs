using DMS.Core.Sap;
using DMS.Core.Security;
using DMS.Core.Transactions;
using DMS.Core.Transactions.Handlers;
using DMS.Desktop.Configuration;
using DMS.Desktop.Logging;
using DMS.Desktop.Models;
using DMS.Desktop.Repositories;
using DMS.Desktop.Services;
using DMS.Desktop.Settings;
using DMS.Desktop.Views.Admin;
using DMS.Desktop.Views.Articles;
using DMS.Desktop.Views.Dialogs;
using DMS.Desktop.Views.Documents;
using DMS.Desktop.Views.Settings;
using DMS.Desktop.Views.SystemRoles;
using DMS.Desktop.Views.SystemSettings;
using DMS.Desktop.Views.SystemTransactions;
using DMS.Desktop.Views.SystemModules;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DMS.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _isLeftPanelVisible = true;
    private GridLength _lastLeftPanelWidth = new(350);
    private TransactionDispatcher _transactionDispatcher = null!;
    private readonly DmsUserSettingsService _settingsService = new();
    private readonly DmsAppSettingsService _appSettingsService = new();
    private readonly DmsLogger _logger = new(@"Z:\SAP\DMS-db\DEV\Logs");
    private readonly DmsLogReader _logReader = new();
    private SapDecorationRuleService _decorationRuleService = null!;

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
  

    public MainWindow()
    {
        InitializeComponent();
        _appSettings = _appSettingsService.Load();

        var articlesFilePath = string.IsNullOrWhiteSpace(_appSettings.ArticlesDataPath)
            ? Path.Combine(_appSettings.ConfigurationRootPath, "..", "Data", "articles.json")
            : _appSettings.ArticlesDataPath;

        articlesFilePath = Path.GetFullPath(articlesFilePath);

        _articleRepository = new JsonArticleRepository(articlesFilePath);

        _logger.Info($"Articles repository path: {articlesFilePath}; Exists: {File.Exists(articlesFilePath)}");

        _logger = new DmsLogger(_appSettings.LogsRootPath);
        _logger.Info("DMS klient spuštěn.");

        var decorationRulesPath = Path.Combine(AppContext.BaseDirectory, "Config", "sap-decoration-rules.json");

        var decorationRules = new SapDecorationRulesLoader()
            .LoadFromJson(decorationRulesPath);

        _decorationRuleService = new SapDecorationRuleService(decorationRules);

        EnsureLeftPanelVisibleOnStartup();
        InitializeCurrentUser();
        InitializeTransactions();
        LoadUserSettings();
        ApplyTheme();

        TxtTransaction.Focus();
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
            accentBrush = CreateBrushFromHex(_userSettings.AccentColor, "#0B2A4A");
            onAccentBrush = CreateBrushFromHex("#FFFFFF", "#FFFFFF");
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
            accentBrush = CreateBrushFromHex(_userSettings.AccentColor, "#0B2A4A");
            onAccentBrush = CreateBrushFromHex("#FFFFFF", "#FFFFFF");
        }

        SetApplicationBrush("DmsBackgroundBrush", backgroundBrush);
        SetApplicationBrush("DmsPanelBrush", panelBrush);
        SetApplicationBrush("DmsForegroundBrush", foregroundBrush);
        SetApplicationBrush("DmsMutedForegroundBrush", mutedForegroundBrush);
        SetApplicationBrush("DmsBorderBrush", borderBrush);
        SetApplicationBrush("DmsAccentBrush", accentBrush);
        SetApplicationBrush("DmsOnAccentBrush", onAccentBrush);
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
    private static void ApplyForegroundToChildren(DependencyObject parent, Brush foreground)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            switch (child)
            {
                case TextBlock textBlock:
                    textBlock.Foreground = foreground;
                    break;

                case Label label:
                    label.Foreground = foreground;
                    break;

                case CheckBox checkBox:
                    checkBox.Foreground = foreground;
                    break;
            }

            ApplyForegroundToChildren(child, foreground);
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

    [SupportedOSPlatform("windows")]
    private void InitializeCurrentUser()
    {
        //var windowsLogin = WindowsIdentity.GetCurrent()?.Name ?? string.Empty;
        var windowsLogin = WindowsIdentity.GetCurrent()?.Name ?? string.Empty;
        _usersConfigPath = Path.Combine(AppContext.BaseDirectory, "Config", "users.json");

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
                Roles = new[] { "DMS_READONLY" }
            };

            UpdateCurrentUserText();

            MessageBox.Show(
                $"Uživatel není založený v DMS.\n\nWindows login:\n{windowsLogin}\n\nBude použit režim DMS_READONLY.",
                "DMS - uživatel nenalezen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        _currentUser = new DmsUserContext
        {
            WindowsLogin = user.WindowsLogin,
            DisplayName = user.DisplayName,
            Roles = user.Roles
        };
        _logger.Info($"Aktuální uživatel: {_currentUser.WindowsLogin}; DMS jméno: {_currentUser.DisplayName}; Role: {string.Join(", ", _currentUser.Roles)}");
        UpdateCurrentUserText();
    }

    private void UpdateCurrentUserText()
    {
        TxtCurrentUser.Text =
            $"Uživatel: {_currentUser.DisplayName} ({string.Join(", ", _currentUser.Roles)})";
    }

    private void InitializeTransactions()
    {
        var configPath = Path.Combine(
            AppContext.BaseDirectory,
            "Config",
            "transactions.json");

        var loader = new TransactionDefinitionLoader();
        var definitions = loader.LoadFromJson(configPath);

        if (definitions.Count == 0)
        {
            MessageBox.Show(
                $"Nenačetly se žádné transakce.\n\nOčekávaná cesta:\n{configPath}",
                "DMS - konfigurace transakcí",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        var handlers = new ITransactionHandler[]
        {
            // DMS set
            new SettingsTransactionHandler(() => _userSettings.MaxTransactionHistoryItems),
            new SimpleMessageTransactionHandler("ClientSettings", "Nastavení klienta"),
            new SimpleMessageTransactionHandler("SystemSettings", "Nastavení systému DMS"),
            new SimpleMessageTransactionHandler("SystemDisplay", "Náhled systému DMS"),
            new SimpleMessageTransactionHandler("TransactionManagement", "Správa transakcí"),
            new SimpleMessageTransactionHandler("RoleManagement", "Správa rolí"),
            new SimpleMessageTransactionHandler("ModuleManagement", "Správa modulů"),
            new SimpleMessageTransactionHandler("LogViewer", "Log aplikace"),
            // DMS app
            new SimpleMessageTransactionHandler("TechnicalArticleSummary","Technologický souhrn artiklu"),
            new SimpleMessageTransactionHandler("RecipeOverview","Technologický souhrn receptur"),
            new SimpleMessageTransactionHandler("SimpleMessage", "Transakce"),
            // misc
            new ArticleCardTransactionHandler(),
            new ArticleDocumentsTransactionHandler(),
            new ArticleScreensTransactionHandler(),
            new SimpleMessageTransactionHandler("ScreenPreparationQueue", "Fronta přípravy sít"),
            new SimpleMessageTransactionHandler("OrderOverview", "Přehled zakázek"),
            new HelpTransactionHandler(() => _transactionDispatcher.GetDefinitions()),
            new SimpleMessageTransactionHandler("UserManagement", "Správa uživatelů"),
            new ArticleCreateTransactionHandler(),
            new ArticleChangeTransactionHandler(),
            // SAP
            new SimpleMessageTransactionHandler("SapSettings","SAP nastavení"),
            new SimpleMessageTransactionHandler("SapMaterialDisplay", "Náhled SAP materiálu"),
            new SimpleMessageTransactionHandler("SapCockpit", "SAP import cockpit"),
            new SimpleMessageTransactionHandler("SapMaterialCreate", "Ruční založení materiálu"),
            new SimpleMessageTransactionHandler("SapMaterialEdit", "Ruční editace materiálu"),
            new SimpleMessageTransactionHandler("SapPurchasedPartDisplay", "Náhled nakupovaného dílu"),
            new SimpleMessageTransactionHandler("SapRecipeDisplay", "Náhled receptury"),
            new SimpleMessageTransactionHandler("SapAssemblyPartDisplay", "Náhled kompletačního dílu"),
            new SimpleMessageTransactionHandler("SapToolFixtureDisplay", "Náhled přípravku"),
            new SimpleMessageTransactionHandler("SapPackagingDisplay", "Náhled obalového materiálu"),
            // Quality Assurance Department
            new SimpleMessageTransactionHandler("QualitySettings","Quality nastavení"),
            new SimpleMessageTransactionHandler("QualityCockpit", "Quality cockpit"),
            new SimpleMessageTransactionHandler("QualityArticleDisplay", "Quality karta"),
            new SimpleMessageTransactionHandler("QualityPrintVersionList", "Přehled tiskových verzí"),
            new SimpleMessageTransactionHandler("QualityArticleEdit", "Změna quality dat"),
            new SimpleMessageTransactionHandler("QualityArticleCreate", "Založení quality dat"),
            new SimpleMessageTransactionHandler("QualityTasksOverview","Quality úkoly"),


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

        RefreshTransactionHistoryList();
        RefreshFavoritesList();
        RefreshModulesList();
        RefreshModuleTransactionsList("Vše");
    }

    private void RefreshFavoritesList()
    {
        LstFavorites.Items.Clear();

        foreach (var transactionCode in _userSettings.FavoriteTransactions)
        {
            var definition = _transactionDispatcher.FindDefinition(transactionCode);

            if (definition is null)
            {
                continue;
            }

            LstFavorites.Items.Add(new FavoriteTransactionItem
            {
                Code = definition.Code,
                Name = definition.Name
            });
        }
    }

    private void BtnTransactionHistory_Click(object sender, RoutedEventArgs e)
    {
        PopupTransactionHistory.IsOpen = !PopupTransactionHistory.IsOpen;
    }

    private void LstTransactionHistory_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (LstTransactionHistory.SelectedItem is not string transaction)
        {
            return;
        }

        TxtTransaction.Text = transaction;
        PopupTransactionHistory.IsOpen = false;

        TxtTransaction.Focus();
        TxtTransaction.SelectAll();
    }

    private string GetSelectedModuleName()
    {
        if (LstModules.SelectedItem is ModuleMenuItem module)
        {
            return module.Name;
        }

        return "Vše";
    }

    private bool UserCanSeeTransaction(TransactionDefinition definition)
    {
        if (definition.Roles.Count == 0)
        {
            return true;
        }

        return _currentUser.HasAnyRole(definition.Roles);
    }

    private IReadOnlyList<TransactionDefinition> GetVisibleTransactionDefinitions()
    {
        return _transactionDispatcher
            .GetDefinitions()
            .Where(UserCanSeeTransaction)
            .OrderBy(definition => definition.Module)
            .ThenBy(definition => definition.Code)
            .ToList();
    }

    private void RefreshModulesList()
    {
        LstModules.Items.Clear();

        LstModules.Items.Add(new ModuleMenuItem
        {
            Name = "Vše"
        });

        var modules = GetVisibleTransactionDefinitions()
            .Select(definition => definition.Module)
            .Where(module => !string.IsNullOrWhiteSpace(module))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(module => module)
            .ToList();

        foreach (var module in modules)
        {
            LstModules.Items.Add(new ModuleMenuItem
            {
                Name = module
            });
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

        if (!string.Equals(selectedModule, "Vše", StringComparison.OrdinalIgnoreCase))
        {
            definitions = definitions
                .Where(definition =>
                    string.Equals(definition.Module, selectedModule, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var definition in definitions)
        {
            LstModuleTransactions.Items.Add(new TransactionMenuItem
            {
                Code = definition.Code,
                Name = definition.Name,
                Module = definition.Module,
                RequiresArticleNumber = definition.RequiresArticleNumber,
                IsFavorite = IsFavoriteTransaction(definition.Code)
            });
        }
    }

    private void BtnExecuteModuleTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var transactionCode = button.Tag?.ToString();

        if (string.IsNullOrWhiteSpace(transactionCode))
        {
            return;
        }

        ExecuteTransaction(transactionCode);
    }

    private void LstModules_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstModules.SelectedItem is not ModuleMenuItem module)
        {
            return;
        }

        RefreshModuleTransactionsList(module.Name);
    }
    private void BtnAddFavorite_Click(object sender, RoutedEventArgs e)
    {
        var command = TransactionParser.Parse(TxtTransaction.Text);

        if (string.IsNullOrWhiteSpace(command.Code))
        {
            RenderTransactionResult(TransactionResult.Fail(
                "",
                "Nejdřív zadej transakci, kterou chceš přepnout v oblíbených."));
            return;
        }

        ToggleFavoriteTransaction(command.Code);
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

    private void BtnAddModuleTransactionToFavorites_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var transactionCode = button.Tag?.ToString();

        if (string.IsNullOrWhiteSpace(transactionCode))
        {
            return;
        }

        AddFavoriteTransaction(transactionCode);

        // Zabrání tomu, aby klik na hvězdičku zároveň spustil transakci v ListBoxItem.
        e.Handled = true;
    }

    private void BtnToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var transactionCode = button.Tag?.ToString();

        if (string.IsNullOrWhiteSpace(transactionCode))
        {
            return;
        }

        ToggleFavoriteTransaction(transactionCode);

        // důležité: klik na hvězdu nesmí zároveň spustit transakci
        e.Handled = true;
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
    private void RefreshTransactionHistoryList()
    {
        LstTransactionHistory.Items.Clear();

        foreach (var transaction in _userSettings.TransactionHistory)
        {
            LstTransactionHistory.Items.Add(transaction);
        }
    }

    private void RemoveFavoriteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var item = _favoriteContextMenuItem;

        if (item is null)
        {
            RenderSimplePage(
                "Oblíbené transakce",
                "Nejdřív klikni pravým tlačítkem na transakci, kterou chceš odebrat.");
            return;
        }

        _userSettings.FavoriteTransactions.RemoveAll(code =>
            string.Equals(code, item.Code, StringComparison.OrdinalIgnoreCase));

        _settingsService.Save(_userSettings);
        RefreshFavoritesList();

        RenderSimplePage(
            "Oblíbené transakce",
            $"Transakce {item.Code} byla odebrána z oblíbených.");

        _favoriteContextMenuItem = null;
    }
    private void TxtTransaction_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;

        if (string.IsNullOrWhiteSpace(TxtTransaction.Text))
        {
            return;
        }

        ExecuteTransaction(TxtTransaction.Text);
    }

    private void ExecuteTransaction(string input)
    {
        var command = TransactionParser.Parse(input);

        _logger.Transaction(input, _currentUser.DisplayName);

        if (!TryCompleteMissingParameter(command, out var completedCommand))
        {
            return;
        }

        if (!UserCanExecuteTransaction(completedCommand.Code, out var authorizationMessage))
        {
            _logger.Warning($"Zamítnuté spuštění transakce {completedCommand.Code}: {authorizationMessage}");

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
            return;
        }

        var result = _transactionDispatcher.Dispatch(completedCommand);
        RenderTransactionResult(result);
    }

    private bool UserCanExecuteTransaction(string transactionCode, out string message)
    {
        message = string.Empty;

        var definition = _transactionDispatcher.FindDefinition(transactionCode);

        if (definition is null)
        {
            message = $"Neznámá transakce: {transactionCode}";
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
            $"Nemáte oprávnění ke spuštění transakce {transactionCode}.\n\n" +
            $"Požadované role: {string.Join(", ", definition.Roles)}\n" +
            $"Vaše role: {string.Join(", ", _currentUser.Roles)}";

        return false;
    }

    private void AddFavoriteTransaction(string transactionCode)
    {
        var definition = _transactionDispatcher.FindDefinition(transactionCode);

        if (definition is null)
        {
            RenderTransactionResult(TransactionResult.Fail(
                transactionCode,
                $"Transakce {transactionCode} neexistuje, nelze ji přidat do oblíbených."));
            return;
        }

        if (_userSettings.FavoriteTransactions.Any(item =>
                string.Equals(item, definition.Code, StringComparison.OrdinalIgnoreCase)))
        {
            RenderSimplePage(
                "Oblíbené transakce",
                $"Transakce {definition.Code} už je v oblíbených.");
            return;
        }

        _userSettings.FavoriteTransactions.Add(definition.Code);
        _settingsService.Save(_userSettings);

        RefreshFavoritesList();
        RefreshModuleTransactionsList(GetSelectedModuleName());

        RenderSimplePage(
            "Oblíbené transakce",
            $"Transakce {definition.Code} byla přidána do oblíbených.");
    }
    private void AddTransactionToHistory(string transactionText)
    {
        if (string.IsNullOrWhiteSpace(transactionText))
        {
            return;
        }

        transactionText = transactionText.Trim();

        // Pokud už transakce v historii existuje,
        // odstraníme ji, aby se vložila znovu nahoru jako poslední použitá.
        _userSettings.TransactionHistory.RemoveAll(
            item => string.Equals(item, transactionText, StringComparison.OrdinalIgnoreCase));

        // Nejnovější transakce bude vždy nahoře.
        _userSettings.TransactionHistory.Insert(0, transactionText);

        var maxItems = _userSettings.MaxTransactionHistoryItems;

        if (maxItems <= 0)
        {
            maxItems = 10;
        }

        // Mazání nejstarších položek:
        // pokud je historie delší než limit, odstraňujeme položky od konce seznamu.
        while (_userSettings.TransactionHistory.Count > maxItems)
        {
            var lastIndex = _userSettings.TransactionHistory.Count - 1;
            _userSettings.TransactionHistory.RemoveAt(lastIndex);
        }

        _settingsService.Save(_userSettings);
        RefreshTransactionHistoryList();
    }
    private void ClearTransactionInput()
    {
        TxtTransaction.Text = string.Empty;
        TxtTransaction.Focus();
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

        var dialog = new ArticleNumberPromptWindow(
            selectionConfig.Value.MaterialKind,
            selectionConfig.Value.Title,
            selectionConfig.Value.Subtitle)
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
            Parameter = dialog.ArticleNumber
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

        if (string.IsNullOrWhiteSpace(command.Parameter))
        {
            return $"{prefix}{command.Code}";
        }

        return $"{prefix}{command.Code} {command.Parameter}";
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
            Parameter = command.Parameter
        };

        newWindow.TxtTransaction.Text = BuildTransactionText(commandForNewWindow);

        var result = newWindow._transactionDispatcher.Dispatch(commandForNewWindow);
        newWindow.RenderTransactionResult(result);
    }

    private static string? GetDisplayTransactionForMaterialKind(string? materialKind)
    {
        return materialKind switch
        {
            nameof(SapMaterialKind.GlassArticle) => "ART03",
            nameof(SapMaterialKind.PurchasedPart) => "KUP03",
            nameof(SapMaterialKind.Packaging) => "BAL03",
            nameof(SapMaterialKind.Recipe) => "REC03",
            nameof(SapMaterialKind.AssemblyPart) => "KOM03",
            nameof(SapMaterialKind.ToolFixture) => "PRIP03",
            _ => null
        };
    }

    private static string GetMaterialKindDisplayName(string? materialKind)
    {
        return materialKind switch
        {
            nameof(SapMaterialKind.GlassArticle) => "skleněný artikl / flakon",
            nameof(SapMaterialKind.PurchasedPart) => "nakupovaný díl",
            nameof(SapMaterialKind.Packaging) => "obalový materiál",
            nameof(SapMaterialKind.Recipe) => "receptura",
            nameof(SapMaterialKind.AssemblyPart) => "kompletační díl",
            nameof(SapMaterialKind.ToolFixture) => "přípravek",
            nameof(SapMaterialKind.Ignored) => "ignorovaný SAP materiál",
            _ => "neznámý typ materiálu"
        };
    }

    private static string GetPackagingKindDisplayName(string? packagingKind)
    {
        return packagingKind switch
        {
            "PackagingSetOldReference" => "Balicí sada - vazba podle starého čísla",
            "PackagingSetSapReference" => "Balicí sada - vazba podle SAP čísla",
            "PackagingComponent" => "Komponenta balicí sady",
            _ => "Neznámý typ obalu"
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
                RenderArticleEdit(result.Parameter!);
                break;

            case "ART03":
                RenderArticleCard(result.Parameter!);
                break;

            case "DOC03":
                RenderArticleDocuments(result.Parameter ?? string.Empty);
                break;

            case "SCR03":
                RenderSimplePage("Síta artiklu", result.Message);
                break;

            case "SCR10":
                RenderSimplePage("Fronta přípravy sít", result.Message);
                break;

            case "ORD10":
                RenderSimplePage("Přehled zakázek", result.Message);
                break;

            case "WHOAMI":
                RenderSimplePage("Aktuální uživatel", result.Message);
                break;

            case "HELP":
                RenderHelp(result.Message);
                break;

            case "CLSET":
                RenderClientSettings();
                break;

            case "SYS01":
                RenderSystemSettings();
                break;

            case "SYS03":
                RenderSystemDisplay();
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

            case "USR01":
                RenderUserManagement();
                break;

            case "LOG03":
                RenderLogViewer();
                break;

            case "QASET":
                RenderQualitySettings();
                break;

            case "QA00":
                RenderQualityCockpit();
                break;

            case "QA05":
                RenderQualityPrintVersions();
                break;

            case "QA03":
                RenderQualityArticle(result.Parameter ?? string.Empty);
                break;

            case "QA02":
                RenderQualityArticleEdit(result.Parameter ?? string.Empty);
                break;

            case "QA01":
                RenderQualityArticleCreate(result.Parameter ?? string.Empty);
                break;

            case "QATASK":
                RenderQualityTasksOverview();
                break;

            case "SAPSET":
                RenderSapSettings();
                break;

            case "SAP00":
                RenderSapCockpit();
                break;

            case "SAP01":
                RenderSimplePage("Ruční založení materiálu", result.Message);
                break;

            case "SAP02":
                RenderSimplePage("Ruční editace materiálu", result.Message);
                break;

            case "SAP03":
                RenderSapMaterialDisplay(result.Parameter ?? string.Empty);
                break;

            case "KUP03":
                RenderTypedSapMaterialDisplay(
                    result.Parameter!,
                    nameof(SapMaterialKind.PurchasedPart),
                    "KUP03 - Nakupovaný díl");
                break;

            case "MAT03":
                RenderMaterialUsage(result.Parameter ?? string.Empty);
                break;

            case "REC03":
                RenderRecipeOverview(result.Parameter ?? string.Empty);
                break;

            case "KOM03":
                RenderTypedSapMaterialDisplay(
                    result.Parameter!,
                    nameof(SapMaterialKind.AssemblyPart),
                    "KOM03 - Kompletační díl");
                break;

            case "PRIP03":
                RenderTypedSapMaterialDisplay(
                    result.Parameter!,
                    nameof(SapMaterialKind.ToolFixture),
                    "PRIP03 - Přípravek");
                break;

            case "BAL03":
                RenderTypedSapMaterialDisplay(
                    result.Parameter!,
                    nameof(SapMaterialKind.Packaging),
                    "BAL03 - Obalový materiál");
                break;

            case "TEC03":
                RenderTechnicalArticleSummary(result.Parameter ?? string.Empty);
                break;

            default:
                RenderSimplePage(result.TransactionCode, result.Message);
                break;
        }
        ResetWorkspaceScroll();
    }
    private void RenderLogViewer()
    {
        WorkspacePanel.Children.Clear();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        WorkspacePanel.Children.Add(root);

        var title = CreateTitle("Log aplikace");
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var filterPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };

        Grid.SetRow(filterPanel, 1);
        root.Children.Add(filterPanel);

        var datePicker = new DatePicker
        {
            SelectedDate = DateTime.Today,
            Width = 140,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var txtTimeFrom = new TextBox
        {
            Width = 70,
            Text = "00:00",
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Čas od, například 08:00"
        };

        var txtTimeTo = new TextBox
        {
            Width = 70,
            Text = "23:59",
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Čas do, například 16:30"
        };

        var txtUser = new TextBox
        {
            Width = 180,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Filtr uživatele, například Radek"
        };

        var cmbLevel = new ComboBox
        {
            Width = 130,
            Margin = new Thickness(0, 0, 8, 0)
        };

        cmbLevel.Items.Add("Vše");
        cmbLevel.Items.Add("INFO");
        cmbLevel.Items.Add("WARN");
        cmbLevel.Items.Add("ERROR");
        cmbLevel.Items.Add("TRANSACTION");
        cmbLevel.Items.Add("DOCUMENT");
        cmbLevel.SelectedIndex = 0;

        var btnRefresh = new Button
        {
            Content = "Filtrovat",
            Width = 90,
            Height = 28
        };

        filterPanel.Children.Add(CreateFilterLabel("Den:"));
        filterPanel.Children.Add(datePicker);
        filterPanel.Children.Add(CreateFilterLabel("Od:"));
        filterPanel.Children.Add(txtTimeFrom);
        filterPanel.Children.Add(CreateFilterLabel("Do:"));
        filterPanel.Children.Add(txtTimeTo);
        filterPanel.Children.Add(CreateFilterLabel("Uživatel:"));
        filterPanel.Children.Add(txtUser);
        filterPanel.Children.Add(CreateFilterLabel("Úroveň:"));
        filterPanel.Children.Add(cmbLevel);
        filterPanel.Children.Add(btnRefresh);

        var logTextBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 420,
            FontFamily = new FontFamily("Consolas")
        };

        logTextBox.SetResourceReference(TextBox.BackgroundProperty, "DmsBackgroundBrush");
        logTextBox.SetResourceReference(TextBox.ForegroundProperty, "DmsForegroundBrush");
        logTextBox.SetResourceReference(TextBox.BorderBrushProperty, "DmsBorderBrush");

        Grid.SetRow(logTextBox, 2);
        root.Children.Add(logTextBox);

        void RefreshLog()
        {
            var day = datePicker.SelectedDate ?? DateTime.Today;

            var entries = _logReader.ReadDay(_appSettings.LogsRootPath, day);

            if (TryParseTime(txtTimeFrom.Text, out var timeFrom))
            {
                entries = entries
                    .Where(entry => entry.Timestamp.TimeOfDay >= timeFrom)
                    .ToList();
            }

            if (TryParseTime(txtTimeTo.Text, out var timeTo))
            {
                entries = entries
                    .Where(entry => entry.Timestamp.TimeOfDay <= timeTo)
                    .ToList();
            }

            var userFilter = txtUser.Text.Trim();

            if (!string.IsNullOrWhiteSpace(userFilter))
            {
                entries = entries
                    .Where(entry =>
                        entry.User.Contains(userFilter, StringComparison.OrdinalIgnoreCase) ||
                        entry.Message.Contains(userFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var selectedLevel = cmbLevel.SelectedItem?.ToString() ?? "Vše";

            if (!string.Equals(selectedLevel, "Vše", StringComparison.OrdinalIgnoreCase))
            {
                entries = entries
                    .Where(entry => string.Equals(entry.Level, selectedLevel, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var lines = entries
                .OrderBy(entry => entry.Timestamp)
                .Select(entry => entry.DisplayText)
                .ToList();

            if (lines.Count == 0)
            {
                var logFilePath = Path.Combine(
                    _appSettings.LogsRootPath,
                    $"dms-{day:yyyy-MM-dd}.log");

                logTextBox.Text =
                    "Nenalezeny žádné záznamy pro zadaný filtr." +
                    Environment.NewLine +
                    Environment.NewLine +
                    $"Soubor: {logFilePath}";
                return;
            }

            logTextBox.Text = string.Join(Environment.NewLine, lines);
            logTextBox.ScrollToEnd();
        }

        btnRefresh.Click += (_, _) => RefreshLog();

        datePicker.SelectedDateChanged += (_, _) => RefreshLog();
        cmbLevel.SelectionChanged += (_, _) => RefreshLog();

        RefreshLog();
        ResetWorkspaceScroll();
    }
    private void RenderHelp(string message)
    {
        var panel = CreateWorkspaceStack();

        panel.Children.Add(CreateTitle("Nápověda transakcí"));

        var textBox = new TextBox
        {
            Text = message,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 420,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14
        };

        textBox.SetResourceReference(TextBox.BackgroundProperty, "DmsBackgroundBrush");
        textBox.SetResourceReference(TextBox.ForegroundProperty, "DmsForegroundBrush");
        textBox.SetResourceReference(TextBox.BorderBrushProperty, "DmsBorderBrush");

        panel.Children.Add(textBox);

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
            var storagePaths = new SapStoragePaths(@"Z:\SAP\DMS-db\DEV");
            var repository = new JsonSapMaterialRepository(storagePaths.SapMaterialsFilePath);

            var material = repository.FindByMaterialNumber(materialNumber);

            if (material is null)
            {
                panel.Children.Add(CreateArticleWarning(
                    "Materiál nenalezen",
                    $"SAP materiál {materialNumber} nebyl nalezen v SAP mirror cache.\n\n" +
                    $"Soubor:\n{storagePaths.SapMaterialsFilePath}\n\n" +
                    "Nejdřív proveď import přes SAP00."));
                return;
            }

            if (material.PackagingInfo is not null)
            {
                panel.Children.Add(CreateArticleSectionTitle("Balicí vazba"));

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
                        "Vazba na staré číslo artiklu",
                        material.PackagingInfo.LinkedArticleOldNumber));
                }
            }

            if (!string.Equals(material.MaterialKind, expectedMaterialKind, StringComparison.OrdinalIgnoreCase))
            {
                var correctTransaction = GetDisplayTransactionForMaterialKind(material.MaterialKind);

                if (!string.IsNullOrWhiteSpace(correctTransaction))
                {
                    panel.Children.Add(CreateArticleWarning(
                        "Přesměrování na správnou transakci",
                        $"Zadaný materiál {material.MaterialNumber} není typ " +
                        $"{GetMaterialKindDisplayName(expectedMaterialKind)}, ale {GetMaterialKindDisplayName(material.MaterialKind)}.\n\n" +
                        $"Otevírám správnou transakci: {correctTransaction} {material.MaterialNumber}"));

                    ExecuteTransaction($"{correctTransaction} {material.MaterialNumber}");
                    return;
                }

                panel.Children.Add(CreateArticleWarning(
                    "Nesprávný typ materiálu",
                    $"Zadaný materiál {material.MaterialNumber} má typ {material.MaterialKind}, " +
                    $"který nemá přiřazenou náhledovou transakci.\n\n" +
                    "Pro obecný náhled použij SAP03."));
                return;
            }

            panel.Children.Add(CreateMaterialHeaderCard(material, title));

            panel.Children.Add(CreateArticleSectionTitle("SAP základ"));
            panel.Children.Add(CreateArticleTwoColumnLine("SAP číslo", material.MaterialNumber, "Status", NullDash(material.MaterialStatus)));
            panel.Children.Add(CreateArticleTwoColumnLine("Staré číslo", NullDash(material.OldMaterialNumber), "Typ v DMS", material.MaterialKind));
            panel.Children.Add(CreateArticleTwoColumnLine("Prefix", NullDash(material.TransactionPrefix), "Importováno", material.ImportedAt.ToString("dd.MM.yyyy HH:mm:ss")));
            panel.Children.Add(CreateArticleFullLine("Označení", material.Description));

            if (!string.IsNullOrWhiteSpace(material.ToolFixtureKind))
            {
                panel.Children.Add(CreateArticleSectionTitle("Klasifikace přípravku"));
                panel.Children.Add(CreateArticleFullLine("Druh přípravku", material.ToolFixtureKind));
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
                    linksGrid.Children.Add(CreateArticleLinkTile("Použití v kusovnících", "BOM", "Kde je díl použitý"));
                    linksGrid.Children.Add(CreateArticleLinkTile("Dokumentace", "DOC03", "Technické listy, specifikace"));
                    linksGrid.Children.Add(CreateArticleLinkTile("Poznámky", "DMS", "Lokální poznámky k dílu"));
                    break;

                case nameof(SapMaterialKind.Recipe):
                    linksGrid.Children.Add(CreateArticleLinkTile("Použití receptury", "REC", "Artikly používající recepturu"));
                    linksGrid.Children.Add(CreateArticleLinkTile("Dokumentace", "DOC03", "Receptura, schválení, verze"));
                    linksGrid.Children.Add(CreateArticleLinkTile("Kusovníky", "BOM", "Výskyt v SAP kusovnících"));
                    break;

                case nameof(SapMaterialKind.AssemblyPart):
                    linksGrid.Children.Add(CreateArticleLinkTile("Použití v kompletaci", "KOM", "Vazby na lepení/kompletaci"));
                    linksGrid.Children.Add(CreateArticleLinkTile("Kusovníky", "BOM", "Výskyt v SAP kusovnících"));
                    linksGrid.Children.Add(CreateArticleLinkTile("Dokumentace", "DOC03", "Výkresy, schválení, specifikace"));
                    break;

                case nameof(SapMaterialKind.ToolFixture):
                    linksGrid.Children.Add(CreateArticleLinkTile("Použití přípravku", "PRIP", "Artikly a operace používající přípravek"));
                    linksGrid.Children.Add(CreateArticleLinkTile("Dokumentace", "DOC03", "Výkresy, údržba, nastavení"));
                    linksGrid.Children.Add(CreateArticleLinkTile("Pracovní postupy", "RTG", "Vazby na operace"));
                    break;
            }

            panel.Children.Add(linksGrid);
        }
        catch (Exception ex)
        {
            panel.Children.Add(CreateArticleWarning(
                $"{title} se nepodařilo načíst",
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

    private void RenderSapMaterialDisplay(string materialNumber)
    {
        var panel = CreateWorkspaceStack();

        panel.Children.Add(CreateTitle("SAP03 - Náhled SAP materiálu"));
        panel.Children.Add(CreateSapLikeInfoBar($"Zobrazení materiálu {materialNumber} ze SAP mirror cache"));

        try
        {
            var storagePaths = new SapStoragePaths(@"Z:\SAP\DMS-db\DEV");
            var repository = new JsonSapMaterialRepository(storagePaths.SapMaterialsFilePath);

            var material = repository.FindByMaterialNumber(materialNumber);

            if (material is null)
            {
                panel.Children.Add(CreateBodyText(
                    $"SAP materiál {materialNumber} nebyl nalezen v SAP mirror cache.\n\n" +
                    $"Očekávaný soubor:\n{storagePaths.SapMaterialsFilePath}\n\n" +
                    "Nejdřív proveď import přes SAP00."));
                return;
            }

            panel.Children.Add(CreateSapLikeSectionHeader("Základní data"));

            panel.Children.Add(CreateSapLikeLine("Materiál", material.MaterialNumber));
            panel.Children.Add(CreateSapLikeLine("Označení", material.Description));
            panel.Children.Add(CreateSapLikeLine("Staré číslo", NullDash(material.OldMaterialNumber)));
            panel.Children.Add(CreateSapLikeLine("Status", NullDash(material.MaterialStatus)));
            panel.Children.Add(CreateSapLikeLine("Typ v DMS", material.MaterialKind));
            panel.Children.Add(CreateSapLikeLine("Transakční prefix", NullDash(material.TransactionPrefix)));

            if (!string.IsNullOrWhiteSpace(material.ToolFixtureKind))
            {
                panel.Children.Add(CreateSapLikeLine("Druh přípravku", material.ToolFixtureKind));
            }

            if (material.GlassInfo is not null)
            {
                panel.Children.Add(CreateSapLikeSeparator());
                panel.Children.Add(CreateSapLikeSectionHeader("Rozpad označení skla"));

                panel.Children.Add(CreateSapLikeLine("Forma", NullDash(material.GlassInfo.MoldNumber)));
                panel.Children.Add(CreateSapLikeLine("Typ skla", NullDash(material.GlassInfo.GlassTypeNumber)));
                panel.Children.Add(CreateSapLikeLine("Objem", FormatVolume(material.GlassInfo.VolumeMl)));
                panel.Children.Add(CreateSapLikeLine("Dekorační řetězec", NullDash(material.GlassInfo.DecorationChain)));
                panel.Children.Add(CreateSapLikeLine("Popis", NullDash(material.GlassInfo.RemainingDescription)));

                panel.Children.Add(CreateSapLikeSeparator());
                panel.Children.Add(CreateSapLikeSectionHeader("Dekorační kroky"));

                if (material.GlassInfo.DecorationSteps.Count == 0)
                {
                    panel.Children.Add(CreateSapLikeLine("Kroky", "Nerozpoznáno"));
                }
                else
                {
                    foreach (var step in material.GlassInfo.DecorationSteps)
                    {
                        panel.Children.Add(CreateSapLikeLine(step, GetDecorationName(step)));
                    }
                }
            }

            panel.Children.Add(CreateSapLikeSeparator());
            panel.Children.Add(CreateSapLikeSectionHeader("Technické info"));

            panel.Children.Add(CreateSapLikeLine("Importováno", material.ImportedAt.ToString("dd.MM.yyyy HH:mm:ss")));
            panel.Children.Add(CreateSapLikeLine("Soubor", storagePaths.SapMaterialsFilePath));
        }
        catch (Exception ex)
        {
            panel.Children.Add(CreateBodyText(
                "SAP03 se nepodařilo načíst.\n\n" +
                ex.Message));
        }
    }
    private static bool TryParseTime(string value, out TimeSpan time)
    {
        if (TimeSpan.TryParse(value.Trim(), out time))
        {
            return true;
        }

        time = TimeSpan.Zero;
        return false;
    }

    private void RenderSystemSettings()
    {
        WorkspacePanel.Children.Clear();

        var systemSettingsPath = Path.Combine(
            AppContext.BaseDirectory,
            "Config",
            "dms-system-settings.json");

        var sapMaterialsFilePath = Path.Combine(
            @"Z:\SAP\DMS-db\DEV",
            "Data",
            "sap-materials.json");

        WorkspacePanel.Children.Add(new SystemSettingsView(
            systemSettingsPath,
            sapMaterialsFilePath));

        ResetWorkspaceScroll();
    }

    private void RenderTransactionManagement()
    {
        WorkspacePanel.Children.Clear();

        var transactionsPath = Path.Combine(
            AppContext.BaseDirectory,
            "Config",
            "transactions.json");

        var rolesPath = Path.Combine(
            AppContext.BaseDirectory,
            "Config",
            "dms-roles.json");

        var modulesPath = Path.Combine(
            AppContext.BaseDirectory,
            "Config",
            "dms-modules.json");

        WorkspacePanel.Children.Add(new TransactionManagementView(
            transactionsPath,
            rolesPath,
            modulesPath,
            afterSave: ReloadTransactionsAfterManagementSave));

        ResetWorkspaceScroll();
    }
    private void RenderSystemDisplay()
    {
        var panel = CreateWorkspaceStack();

        panel.Children.Add(CreateTitle("Systémová konfigurace"));

        panel.Children.Add(CreateLine($"Prostředí: {_appSettings.Environment}"));
        panel.Children.Add(CreateLine($"Režim konfigurace: {_appSettings.ConfigurationMode}"));
        panel.Children.Add(CreateLine($"Konfigurace: {_appSettings.ConfigurationRootPath}"));
        panel.Children.Add(CreateLine($"Dokumenty: {_appSettings.DocumentsRootPath}"));
        panel.Children.Add(CreateLine($"Logy: {_appSettings.LogsRootPath}"));
        panel.Children.Add(CreateLine($"Výchozí testovací artikl: {_appSettings.DefaultTestArticleNumber}"));

        panel.Children.Add(new Separator
        {
            Margin = new Thickness(0, 16, 0, 16)
        });

        panel.Children.Add(CreateSectionTitle("Integrace"));
        panel.Children.Add(CreateLine($"SAP: {_appSettings.SapMode}"));
        panel.Children.Add(CreateLine($"MES: {_appSettings.MesMode}"));
        panel.Children.Add(CreateLine($"Databáze: {_appSettings.DatabaseMode}"));

        panel.Children.Add(CreateSectionTitle("Aktuální uživatel"));
        panel.Children.Add(CreateLine($"Windows login: {_currentUser.WindowsLogin}"));
        panel.Children.Add(CreateLine($"DMS jméno: {_currentUser.DisplayName}"));
        panel.Children.Add(CreateLine($"Role: {string.Join(", ", _currentUser.Roles)}"));

        ResetWorkspaceScroll();
    }
    private void RenderArticleCard(string articleNumber)
    {
        var panel = CreateWorkspaceStack();

        panel.Children.Add(CreateTitle("ART03 - Artikelmapa"));

        try
        {
            var storagePaths = new SapStoragePaths(@"Z:\SAP\DMS-db\DEV");
            var repository = new JsonSapMaterialRepository(storagePaths.SapMaterialsFilePath);

            var material = repository.FindByMaterialNumber(articleNumber);

            if (material is null)
            {
                panel.Children.Add(CreateArticleWarning(
                    "Artikl nenalezen",
                    $"SAP artikl {articleNumber} nebyl nalezen v SAP mirror cache.\n\n" +
                    $"Soubor:\n{storagePaths.SapMaterialsFilePath}\n\n" +
                    "Nejdřív proveď import přes SAP00."));
                return;
            }

            if (!string.Equals(material.MaterialKind, nameof(SapMaterialKind.GlassArticle), StringComparison.OrdinalIgnoreCase))
            {
                var correctTransaction = GetDisplayTransactionForMaterialKind(material.MaterialKind);

                if (!string.IsNullOrWhiteSpace(correctTransaction))
                {
                    panel.Children.Add(CreateArticleWarning(
                        "Přesměrování na správnou transakci",
                        $"Materiál {material.MaterialNumber} není skleněný artikl / flakon, " +
                        $"ale {GetMaterialKindDisplayName(material.MaterialKind)}.\n\n" +
                        $"Otevírám správnou transakci: {correctTransaction} {material.MaterialNumber}"));

                    ExecuteTransaction($"{correctTransaction} {material.MaterialNumber}");
                    return;
                }

                panel.Children.Add(CreateArticleWarning(
                    "Nejedná se o skleněný artikl",
                    $"Materiál {material.MaterialNumber} není skleněný artikl / flakon.\n\n" +
                    $"Typ v DMS: {material.MaterialKind}\n\n" +
                    "Pro obecný SAP náhled použij SAP03."));
                return;
            }

            panel.Children.Add(CreateArticleHeaderCard(material));

            panel.Children.Add(CreateArticleSectionTitle("SAP základ"));
            panel.Children.Add(CreateArticleTwoColumnLine("SAP číslo", material.MaterialNumber, "Status", NullDash(material.MaterialStatus)));
            panel.Children.Add(CreateArticleTwoColumnLine("Staré číslo", NullDash(material.OldMaterialNumber), "Typ v DMS", material.MaterialKind));
            panel.Children.Add(CreateArticleFullLine("Označení", material.Description));

            if (material.GlassInfo is not null)
            {
                panel.Children.Add(CreateArticleSectionTitle("Rozpad označení"));
                panel.Children.Add(CreateArticleTwoColumnLine("Forma", NullDash(material.GlassInfo.MoldNumber), "Typ skla", NullDash(material.GlassInfo.GlassTypeNumber)));
                panel.Children.Add(CreateArticleTwoColumnLine("Objem", FormatVolume(material.GlassInfo.VolumeMl), "Dekorace", NullDash(material.GlassInfo.DecorationChain)));
                panel.Children.Add(CreateArticleFullLine("Popis", NullDash(material.GlassInfo.RemainingDescription)));

                panel.Children.Add(CreateArticleSectionTitle("Dekorační tok"));
                panel.Children.Add(CreateDecorationFlow(material.GlassInfo.DecorationSteps));
            }
            else
            {
                panel.Children.Add(CreateArticleWarning(
                    "Označení se nepodařilo rozparsovat",
                    "Krátký text neodpovídá očekávanému formátu:\n" +
                    "<forma> <typ skla> <objem> <dekorace> <popis>"));
            }

            panel.Children.Add(CreateArticleSectionTitle("DMS vazby"));

            var linksGrid = new UniformGrid
            {
                Columns = 3,
                Margin = new Thickness(0, 4, 0, 0)
            };

            linksGrid.Children.Add(CreateArticleLinkTile("Dokumentace", "DOC03", "Výkresy, MB, tiskové oblasti"));
            linksGrid.Children.Add(CreateArticleLinkTile("Receptury", "REC03", "SAP receptury a DMS vazby"));
            linksGrid.Children.Add(CreateArticleLinkTile("Síta", "SCR03", "Síta a příprava sít"));
            linksGrid.Children.Add(CreateArticleLinkTile("Kusovník", "BOM03", "SAP snapshot kusovníku"));
            linksGrid.Children.Add(CreateArticleLinkTile("Postup", "RTG03", "SAP snapshot pracovního postupu"));
            linksGrid.Children.Add(CreateArticleLinkTile("Přípravky", "PRIP03", "Nástroje a přípravky"));

            panel.Children.Add(linksGrid);
        }
        catch (Exception ex)
        {
            panel.Children.Add(CreateArticleWarning(
                "ART03 se nepodařilo načíst",
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
    private void RenderArticleDocuments(string articleNumber)
    {
        WorkspacePanel.Children.Clear();

        var articleFolderPath = Path.Combine(
            _appSettings.DocumentsRootPath,
            "Articles",
            articleNumber);

        WorkspacePanel.Children.Add(new ArticleDocumentsView(
            articleNumber,
            articleFolderPath,
            filePath => _logger.OpenDocument(filePath, _currentUser.DisplayName)));

        ResetWorkspaceScroll();
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
                $"Artikl {articleNumber} nebyl nalezen v DMS. Použij ART01 pro založení.");
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

        _logger.Info($"Uložen artikl {article.SapArticleNumber}; uživatel: {_currentUser.DisplayName}");

        RenderArticleDetail(article.SapArticleNumber);
    }
    private void RenderSimplePage(string title, string message)
    {
        var panel = CreateWorkspaceStack();

        panel.Children.Add(CreateTitle(title));
        panel.Children.Add(CreateBodyText(message));
        ResetWorkspaceScroll();
    }

    private void RenderUserManagement()
    {
        var panel = CreateWorkspaceStack();

        panel.Children.Add(CreateTitle("Správa uživatelů"));

        panel.Children.Add(new UserManagementView(
            _usersConfigPath,
            _currentUser));
        ResetWorkspaceScroll();
    }
    private void RenderClientSettings()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(new ClientSettingsView(
            _userSettings,
            ApplyTheme,
            SaveUserSettings));
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
            stack.Children.Add(CreateArticleBadge("Dekorace nerozpoznána"));
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
                    Text = "→",
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

    private static (string? MaterialKind, string Title, string Subtitle)? GetSelectionConfig(string transactionCode)
    {
        return transactionCode.ToUpperInvariant() switch
        {
            "SAP03" => (
                null,
                "Výběr SAP materiálu",
                "Zobrazují se všechny importované SAP materiály ze SAP mirror cache."),

            "MAT03" => (
                null,
                "Výběr SAP materiálu",
                "Zobrazují se všechny importované SAP materiály ze SAP mirror cache."),

            "ART03" => (
                nameof(SapMaterialKind.GlassArticle),
                "Výběr artiklu",
                "Zobrazují se pouze skleněné artikly / flakony."),

            "DOC03" => (
                nameof(SapMaterialKind.GlassArticle),
                "Výběr artiklu pro dokumentaci",
                "Zobrazují se pouze skleněné artikly / flakony."),

            "KUP03" => (
                nameof(SapMaterialKind.PurchasedPart),
                "Výběr nakupovaného dílu",
                "Zobrazují se pouze nakupované díly."),

            "REC03" => (
                nameof(SapMaterialKind.Recipe),
                "Výběr receptury",
                "Zobrazují se pouze receptury."),

            "KOM03" => (
                nameof(SapMaterialKind.AssemblyPart),
                "Výběr kompletačního dílu",
                "Zobrazují se pouze kompletační díly."),

            "PRIP03" => (
                nameof(SapMaterialKind.ToolFixture),
                "Výběr přípravku",
                "Zobrazují se pouze přípravky."),

            "BAL03" => (
                nameof(SapMaterialKind.Packaging),
                "Výběr obalového materiálu",
                "Zobrazují se obalové materiály a balicí sady z okruhu 13*."),

            "QA03" => (
                null,
                "Výběr pro QA03",
                "Zadej SAP materiál, nebo ručně napiš celé číslo tiskové verze."),

            "QA02" => (
                null,
                "Výběr quality artiklu",
                "Zadej SAP materiál nebo celé číslo tiskové verze."),

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

        BtnToggleLeftPanel.Content = "☰";
        BtnToggleLeftPanel.ToolTip = "Skrýt levý panel";
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

            BtnToggleLeftPanel.Content = "☰";
            BtnToggleLeftPanel.ToolTip = "Zobrazit levý panel";

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

        BtnToggleLeftPanel.Content = "☰";
        BtnToggleLeftPanel.ToolTip = "Skrýt levý panel";
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

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (_navigationBackStack.Count == 0)
        {
            return;
        }

        var previousCommand = _navigationBackStack.Pop();

        if (!string.IsNullOrWhiteSpace(_currentTransactionCommand))
        {
            _navigationForwardStack.Push(_currentTransactionCommand);
        }

        NavigateWithoutRecording(previousCommand);
    }

    private void UpdateBackButtonState()
    {
        if (BtnBack is not null)
        {
            BtnBack.IsEnabled = _navigationBackStack.Count > 0;
        }
    }

    private void RegisterNavigation(string newCommandText)
    {
        if (string.IsNullOrWhiteSpace(newCommandText))
        {
            return;
        }

        newCommandText = newCommandText.Trim();

        // Důležité:
        // Pokud se pohybujeme přes Zpět/Vpřed/Aktualizovat,
        // nesmíme čistit forward stack ani zapisovat novou historii.
        if (_isNavigatingFromHistory)
        {
            _currentTransactionCommand = newCommandText;
            UpdateNavigationButtons();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_currentTransactionCommand) &&
            !string.Equals(_currentTransactionCommand, newCommandText, StringComparison.OrdinalIgnoreCase))
        {
            _navigationBackStack.Push(_currentTransactionCommand);

            // Forward se maže jen při úplně nové ruční navigaci.
            _navigationForwardStack.Clear();
        }

        _currentTransactionCommand = newCommandText;

        UpdateNavigationButtons();
    }

    private void UpdateNavigationButtons()
    {
        BtnBack.IsEnabled = _navigationBackStack.Count > 0;
        BtnForward.IsEnabled = _navigationForwardStack.Count > 0;
        BtnRefreshTransaction.IsEnabled = !string.IsNullOrWhiteSpace(_currentTransactionCommand);
    }

    private void BtnForward_Click(object sender, RoutedEventArgs e)
    {
        if (_navigationForwardStack.Count == 0)
        {
            return;
        }

        var nextCommand = _navigationForwardStack.Pop();

        if (!string.IsNullOrWhiteSpace(_currentTransactionCommand))
        {
            _navigationBackStack.Push(_currentTransactionCommand);
        }

        NavigateWithoutRecording(nextCommand);
    }

    private void BtnRefreshTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentTransactionCommand))
        {
            return;
        }

        NavigateWithoutRecording(_currentTransactionCommand);
    }

    private void NavigateWithoutRecording(string commandText)
    {
        _isNavigatingFromHistory = true;

        try
        {
            TxtTransaction.Text = commandText;
            ExecuteTransaction(commandText);
        }
        finally
        {
            _isNavigatingFromHistory = false;
            UpdateNavigationButtons();
        }
    }

    private void ReloadTransactionsAfterManagementSave()
    {
        InitializeTransactions();

        RefreshFavoritesList();
        RefreshModulesList();
        RefreshModuleTransactionsList(GetSelectedModuleName());
    }
    private void RenderRoleManagement()
    {
        WorkspacePanel.Children.Clear();

        var rolesPath = Path.Combine(
            AppContext.BaseDirectory,
            "Config",
            "dms-roles.json");

        WorkspacePanel.Children.Add(new RoleManagementView(rolesPath));

        ResetWorkspaceScroll();
    }

    private void RenderModuleManagement()
    {
        WorkspacePanel.Children.Clear();

        var modulesPath = Path.Combine(
            AppContext.BaseDirectory,
            "Config",
            "dms-modules.json");

        WorkspacePanel.Children.Add(new ModuleManagementView(modulesPath));

        ResetWorkspaceScroll();
    }
}
