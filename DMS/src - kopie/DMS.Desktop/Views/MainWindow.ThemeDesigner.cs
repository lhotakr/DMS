using DMS.Desktop.Configuration.Modules;
using DMS.Desktop.Theming;
using DMS.Desktop.Services;
using DMS.Desktop.Views.SystemTheme;
using System.Windows.Controls;
using System.IO;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private DmsUiProfileRuntime? _uiProfileRuntime;
    private string _uiOverrideTransactionCode = string.Empty;

    private DmsUiProfileService CreateUiProfileService() =>
        new(Path.Combine(_appSettings.ConfigurationRootPath, "UI"));

    private DmsUiProfileRuntime GetUiProfileRuntime() =>
        _uiProfileRuntime ??= new DmsUiProfileRuntime();

    private DmsUiProfile? LoadActiveUiProfile() =>
        CreateUiProfileService().LoadActiveProfile();

    private void ApplyUiProfileGlobalOverrides()
    {
        try
        {
            GetUiProfileRuntime().ApplyGlobal(LoadActiveUiProfile());
            RebindShellThemeResources();
        }
        catch (Exception ex)
        {
            _logger.Warning($"SYS14 global UI profile could not be applied: {ex.Message}");
        }
    }


    private void RebindShellThemeResources()
    {
        RootPanel.SetResourceReference(Panel.BackgroundProperty, "DmsBackgroundBrush");
        TopBar.SetResourceReference(Border.BackgroundProperty, "DmsAccentBrush");
        TransactionBar.SetResourceReference(Border.BackgroundProperty, "DmsPanelBrush");
        LeftMenuPanel.SetResourceReference(Border.BackgroundProperty, "DmsPanelBrush");
        WorkspaceHost.SetResourceReference(Border.BackgroundProperty, "DmsPanelBrush");
        WorkspaceHost.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");
    }

    private void PrepareUiScopeForTransaction(string transactionCode)
    {
        _uiOverrideTransactionCode = transactionCode;

        try
        {
            var profile = LoadActiveUiProfile();
            var moduleCode = ResolveUiModuleCode(transactionCode);

            var issues = GetUiProfileRuntime().PrepareScope(
                WorkspacePanel,
                profile,
                moduleCode,
                transactionCode);

            foreach (var issue in issues)
            {
                _logger.Warning(
                    $"SYS14 UI scope issue: Scope={issue.Scope}; Selector={issue.Selector}; Property={issue.Property}; Detail={issue.Details}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(
                $"SYS14 transaction UI resources could not be applied: Code={transactionCode}; {ex.Message}");
        }
    }

    private void ApplyUiPropertyOverrides(string transactionCode)
    {
        ApplyUiPropertyOverridesCore(transactionCode);

        // A second pass after layout also sees ControlTemplate children such as
        // ButtonBorder, DataGridRow and ScrollViewer template elements.
        Dispatcher.BeginInvoke(() =>
        {
            if (string.Equals(
                    _uiOverrideTransactionCode,
                    transactionCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                ApplyUiPropertyOverridesCore(transactionCode);
            }
        });
    }

    private void ApplyUiPropertyOverridesCore(string transactionCode)
    {
        try
        {
            var profile = LoadActiveUiProfile();
            var moduleCode = ResolveUiModuleCode(transactionCode);

            var issues = GetUiProfileRuntime().ApplyProperties(
                RootPanel,
                WorkspacePanel,
                profile,
                moduleCode,
                transactionCode);

            foreach (var issue in issues)
            {
                _logger.Warning(
                    $"SYS14 UI property issue: Scope={issue.Scope}; Selector={issue.Selector}; Property={issue.Property}; Detail={issue.Details}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(
                $"SYS14 transaction UI properties could not be applied: Code={transactionCode}; {ex.Message}");
        }
    }

    private string ResolveUiModuleCode(string transactionCode)
    {
        var definition = _transactionDispatcher.FindDefinition(transactionCode);
        return ResolveModuleCode(definition?.Module);
    }

    private void ApplyUiProfileLive(DmsUiProfile profile)
    {
        try
        {
            GetUiProfileRuntime().ApplyGlobal(profile);
            RebindShellThemeResources();

            var moduleCode = ResolveUiModuleCode("SYS14");

            GetUiProfileRuntime().PrepareScope(
                WorkspacePanel,
                profile,
                moduleCode,
                "SYS14");

            GetUiProfileRuntime().ApplyProperties(
                RootPanel,
                WorkspacePanel,
                profile,
                moduleCode,
                "SYS14");

            DmsWindowChromeStyler.ApplyToAllOpenWindows();
        }
        catch (Exception ex)
        {
            _logger.Warning($"SYS14 live preview failed: {ex.Message}");
        }
    }

    private void ReloadActiveUiProfile()
    {
        ApplyTheme();
        PrepareUiScopeForTransaction("SYS14");
        ApplyUiPropertyOverrides("SYS14");
    }

    private void RenderThemeDesigner()
    {
        WorkspacePanel.Children.Clear();

        if (!_currentUser.HasRole("DMS_ADMIN"))
        {
            RenderSimplePage(
                T("SYS14.Title", "SYS14 — Theme & UI Designer"),
                T("SYS14.AdminOnly", "SYS14 is available only to DMS_ADMIN."));
            return;
        }

        var service = CreateUiProfileService();

        WorkspacePanel.Children.Add(new ThemeDesignerView(
            service,
            loadModules: () =>
                new DmsModuleManagementService(GetConfigPath("dms-modules.json"))
                    .LoadAll(),
            loadTransactions: () => _transactionDispatcher.GetDefinitions(),
            translate: key => T(key),
            applyLive: ApplyUiProfileLive,
            reloadActive: ReloadActiveUiProfile,
            executeTransaction: ExecuteTransaction,
            logger: _logger,
            user: _currentUser));

        ResetWorkspaceScroll();
    }
}
