using DMS.Desktop.Configuration.SystemSettings;
using DMS.Desktop.Views.Admin;
using DMS.Desktop.Services.MasterData;
using DMS.Desktop.Views.MasterData;
using DMS.Desktop.Views.SystemModules;
using DMS.Desktop.Views.SystemRoles;
using DMS.Desktop.Views.SystemSettings;
using DMS.Desktop.Views.SystemTransactions;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private static bool TryParseTime(string? value, out TimeSpan time)
    {
        time = TimeSpan.Zero;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return TimeSpan.TryParse(value.Trim(), out time);
    }

    private void RenderSystemSettings()
    {
        WorkspacePanel.Children.Clear();

        var systemSettingsPath = GetConfigPath("dms-system-settings.json");

        var sapMaterialsFilePath = Path.Combine(
            GetDmsDataRootPath(),
            "Data",
            "sap-materials.json");

        var localizationRootPath = GetConfigPath("Localization");

        var tabs = new TabControl();

        var tabSystem = new TabItem
        {
            Header = T("SYS01.System.TabSystem"),
            Content = new SystemSettingsView(
                systemSettingsPath,
                sapMaterialsFilePath,
                afterSave: ReloadSystemSettingsAfterSave,
                translate: key => T(key),
                logSystemSettingsAction: (action, details) =>
                {
                    _logger.AdminAction(
                        "SYS01",
                        action,
                        _currentUser.DisplayName,
                        details);
                })
        };

        var tabLocalization = new TabItem
        {
            Header = T("SYS01.Localization"),
            Content = new LocalizationManagementView(
                     localizationRootPath,
                     _logger,
                     _currentUser.DisplayName,
                     afterSave: ReloadLocalizationFromUserSettings,
                     translate: key => T(key))
        };

        var masterDataRoot = Path.Combine(GetDmsDataRootPath(), "Data", "MasterData");
        var masterDataService = new DmsMasterDataService(masterDataRoot);

        tabs.Items.Add(new TabItem
        {
            Header = T("SYS01.MasterData.TabOrganization"),
            Content = new OrganizationUnitsView(masterDataService, _logger, _currentUser.DisplayName, key => T(key))
        });

        tabs.Items.Add(new TabItem
        {
            Header = T("SYS01.MasterData.TabPeople"),
            Content = new PeopleView(masterDataService, _logger, _currentUser.DisplayName, key => T(key))
        });

        tabs.Items.Add(new TabItem
        {
            Header = T("SYS01.MasterData.TabUnits"),
            Content = new UnitsView(masterDataService, _logger, _currentUser.DisplayName, key => T(key))
        });
        tabs.Items.Add(tabSystem);
        tabs.Items.Add(tabLocalization);

        WorkspacePanel.Children.Add(tabs);

        ResetWorkspaceScroll();
    }


    private void RenderUserManagement()
    {
        WorkspacePanel.Children.Clear();

        var rolesPath = GetConfigPath("dms-roles.json");

        WorkspacePanel.Children.Add(new UserManagementView(
            _usersConfigPath,
            rolesPath,
            _currentUser,
            _logger,
            afterSave: () =>
            {
                InitializeCurrentUser();
                RefreshFavoritesList();
                RefreshModulesList();
                RefreshModuleTransactionsList(GetSelectedModuleName());
                UpdateCurrentUserText();
            },
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args),
            masterDataRootPath: Path.Combine(
                GetDmsDataRootPath(),
                "Data",
                "MasterData")));

        _logger.AdminAction(
            "USR01",
            "OpenUserManagement",
            _currentUser.DisplayName,
            $"UsersPath={_usersConfigPath}; RolesPath={rolesPath}");

        ResetWorkspaceScroll();
    }


    private void RenderTransactionManagement()
    {
        WorkspacePanel.Children.Clear();

        var transactionsPath = GetConfigPath("transactions.json");
        var rolesPath = GetConfigPath("dms-roles.json");
        var modulesPath = GetConfigPath("dms-modules.json");

        WorkspacePanel.Children.Add(new TransactionManagementView(
            transactionsPath,
            rolesPath,
            modulesPath,
            _logger,
            _currentUser.DisplayName,
            afterSave: () =>
            {
                InitializeTransactions();
                RefreshFavoritesList();
                RefreshModulesList();
                RefreshModuleTransactionsList(GetSelectedModuleName());
            },
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args)));

        _logger.AdminAction(
            "SYS11",
            "OpenTransactionManagement",
            _currentUser.DisplayName,
            $"TransactionsPath={transactionsPath}; RolesPath={rolesPath}; ModulesPath={modulesPath}");

        ResetWorkspaceScroll();
    }

    private void RenderRoleManagement()
    {
        WorkspacePanel.Children.Clear();

        var rolesPath = GetConfigPath("dms-roles.json");

        WorkspacePanel.Children.Add(new RoleManagementView(
            rolesPath,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args)));

        _logger.AdminAction(
            "SYS12",
            "OpenRoleManagement",
            _currentUser.DisplayName,
            $"RolesPath={rolesPath}");

        ResetWorkspaceScroll();
    }


    private void RenderModuleManagement()
    {
        WorkspacePanel.Children.Clear();

        var modulesPath = GetConfigPath("dms-modules.json");
        var transactionsPath = GetConfigPath("transactions.json");

        WorkspacePanel.Children.Add(new ModuleManagementView(
            modulesPath,
            transactionsPath,
            _logger,
            _currentUser.DisplayName,
            afterSave: ReloadTransactionsAfterManagementSave,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }

    private void ReloadTransactionsAfterManagementSave()
    {
        InitializeTransactions();

        RefreshFavoritesList();
        RefreshModulesList();
        RefreshModuleTransactionsList(GetSelectedModuleName());
    }

    private void ReloadSystemSettingsAfterSave(DmsSystemSettings settings)
    {
        _systemSettings = settings;

        ApplyHeaderBranding();

        _logger.AdminAction(
            "SYS01",
            "SaveSystemSettings",
            _currentUser.DisplayName,
            $"DocumentsRootPath={settings.DocumentsRootPath}; ArticleFoldersRootPath={settings.ArticleFoldersRootPath}; HeaderLogo={settings.HeaderSecondaryLogoPath}; LogoMax={settings.HeaderSecondaryLogoMaxWidth}x{settings.HeaderSecondaryLogoMaxHeight}");
    }

    private void ReloadLocalizationAfterSave()
    {
        _localizationService.Load(
            _userSettings.LanguageMode,
            _userSettings.CultureName);

        ApplyLocalization();

        _logger.Info("Lokalizace byla znovu načtena po uložení v SYS01.");
    }
}