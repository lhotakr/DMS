using DMS.Core.Security;
using DMS.Core.Transactions;
using DMS.Core.Transactions.Handlers;
using DMS.Desktop.Logging;
using DMS.Desktop.Configuration;
using DMS.Desktop.Models;
using DMS.Desktop.Services;
using DMS.Desktop.Settings;
using DMS.Desktop.Views.Admin;
using DMS.Desktop.Views.Dialogs;
using DMS.Desktop.Views.Documents;
using DMS.Desktop.Views.Settings;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DMS.Desktop.Views;

public partial class MainWindow : Window
{
    private TransactionDispatcher _transactionDispatcher = null!;
    private readonly DmsUserSettingsService _settingsService = new();
    private readonly DmsAppSettingsService _appSettingsService = new();
    private readonly DmsLogger _logger = new(@"Z:\SAP\DMS-db\DEV\Logs");
    private readonly DmsLogReader _logReader = new();

    private DmsAppSettings _appSettings = new();
    private DmsUserSettings _userSettings = new();
    private DmsUserContext _currentUser = new();
    private FavoriteTransactionItem? _favoriteContextMenuItem;
    private string _usersConfigPath = string.Empty;
    public MainWindow()
    {
        InitializeComponent();
        _appSettings = _appSettingsService.Load();
        _logger = new DmsLogger(_appSettings.LogsRootPath);
        _logger.Info("DMS klient spuštěn.");

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

    private void InitializeCurrentUser()
    {
        var windowsLogin = WindowsIdentity.GetCurrent()?.Name ?? string.Empty;
        _usersConfigPath = Path.Combine(AppContext.BaseDirectory,"Config","users.json");

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
            new SimpleMessageTransactionHandler("LogViewer", "Log aplikace"),
            new ArticleCardTransactionHandler(),
            new ArticleDocumentsTransactionHandler(),
            new ArticleScreensTransactionHandler(),
            new SimpleMessageTransactionHandler("ScreenPreparationQueue", "Fronta přípravy sít"),
            new SimpleMessageTransactionHandler("OrderOverview", "Přehled zakázek"),
            new SystemInfoTransactionHandler(() => _currentUser),
            new HelpTransactionHandler(() => _transactionDispatcher.GetDefinitions()),
            new SettingsTransactionHandler(() => _userSettings.MaxTransactionHistoryItems),
            new SimpleMessageTransactionHandler("UserManagement", "Správa uživatelů"),
            new SimpleMessageTransactionHandler("SystemConfiguration", "Systémová konfigurace")
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

        var dialog = new ArticleNumberPromptWindow
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
            case "ART03":
                RenderArticleCard(result.Parameter!);
                break;

            case "DOC03":
                RenderArticleDocuments(result.Parameter!);
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

            case "SET01":
                RenderClientSettings();
                break;

            case "USR01":
                RenderUserManagement();
                break;

            case "SYS01":
                RenderSystemConfiguration();
                break;

            case "LOG03":
                RenderLogViewer();
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

    private static bool TryParseTime(string value, out TimeSpan time)
    {
        if (TimeSpan.TryParse(value.Trim(), out time))
        {
            return true;
        }

        time = TimeSpan.Zero;
        return false;
    }
    private void RenderSystemConfiguration()
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

        ResetWorkspaceScroll();
    }
    private void RenderArticleCard(string articleNumber)
    {
        var panel = CreateWorkspaceStack();

        panel.Children.Add(CreateTitle("Karta artiklu"));

        panel.Children.Add(CreateLine($"SAP číslo: {articleNumber}"));
        panel.Children.Add(CreateLine("Název: Flakon 50 ml"));
        panel.Children.Add(CreateLine("Zákazník: Example Cosmetics"));
        panel.Children.Add(CreateLine("Stav: Připraveno"));

        panel.Children.Add(new Separator
        {
            Margin = new Thickness(0, 16, 0, 16)
        });

        panel.Children.Add(CreateSectionTitle("Dokumenty"));

        panel.Children.Add(CreateLine("✅ Výkres"));
        panel.Children.Add(CreateLine("✅ Tisková oblast"));
        panel.Children.Add(CreateLine("✅ Massblatt"));
        panel.Children.Add(CreateLine("✅ Balicí předpis"));
        panel.Children.Add(CreateLine("✅ Receptura"));
        ResetWorkspaceScroll();
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

}
