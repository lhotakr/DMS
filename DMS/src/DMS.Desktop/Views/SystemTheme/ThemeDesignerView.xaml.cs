using DMS.Core.Security;
using DMS.Core.Transactions;
using DMS.Desktop.Configuration.Modules;
using DMS.Desktop.Logging;
using DMS.Desktop.Theming;
using DMS.Desktop.UI;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.SystemTheme;

public partial class ThemeDesignerView : UserControl
{
    private readonly DmsUiProfileService _service;
    private readonly Func<IReadOnlyList<DmsModuleDefinition>> _loadModules;
    private readonly Func<IReadOnlyList<TransactionDefinition>> _loadTransactions;
    private readonly Func<string, string> _translate;
    private readonly Action<DmsUiProfile> _applyLive;
    private readonly Action _reloadActive;
    private readonly Action<string> _executeTransaction;
    private readonly DmsLogger _logger;
    private readonly DmsUserContext _user;

    private readonly DmsUiProfileRuntime _previewRuntime = new();
    private readonly ObservableCollection<DmsUiResourceRow> _resourceRows = new();
    private readonly ObservableCollection<DmsUiPropertyOverride> _propertyRows = new();

    private List<DmsModuleDefinition> _modules = new();
    private List<TransactionDefinition> _transactions = new();
    private List<DmsUiScopeOption> _scopes = new();

    private DmsUiProfile? _profile;
    private DmsUiProfile? _loadedSnapshot;
    private DmsUiScopeOption? _scope;
    private bool _suppressSelectionEvents;

    public ThemeDesignerView(
        DmsUiProfileService service,
        Func<IReadOnlyList<DmsModuleDefinition>> loadModules,
        Func<IReadOnlyList<TransactionDefinition>> loadTransactions,
        Func<string, string> translate,
        Action<DmsUiProfile> applyLive,
        Action reloadActive,
        Action<string> executeTransaction,
        DmsLogger logger,
        DmsUserContext user)
    {
        InitializeComponent();

        _service = service;
        _loadModules = loadModules;
        _loadTransactions = loadTransactions;
        _translate = translate;
        _applyLive = applyLive;
        _reloadActive = reloadActive;
        _executeTransaction = executeTransaction;
        _logger = logger;
        _user = user;

        GridResources.ItemsSource = _resourceRows;
        GridProperties.ItemsSource = _propertyRows;

        PreviewGrid.ItemsSource = new[]
        {
            new DmsUiPreviewRow { Code = "SYS14", Name = "Theme & UI Designer", Status = "Active" },
            new DmsUiPreviewRow { Code = "MESDPM", Name = "Data point monitor", Status = "Preview" },
            new DmsUiPreviewRow { Code = "FW11", Name = "Release health", Status = "Ready" }
        };

        ApplyLocalization();
        ReloadAll();
    }

    private string T(string key, string fallback)
    {
        var value = _translate(key);

        return string.IsNullOrWhiteSpace(value) ||
               value.StartsWith("[[", StringComparison.Ordinal)
            ? fallback
            : value;
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("SYS14.Title", "SYS14 — Theme & UI Designer");
        TxtSubtitle.Text = T(
            "SYS14.Description",
            "Administrator editor for distributable global, module and transaction UI overrides.");

        LblProfile.Text = T("SYS14.Profile", "Profile");
        LblScope.Text = T("SYS14.Scope", "Scope");

        BtnNewProfile.Content = T("SYS14.NewProfile", "New");
        BtnCloneProfile.Content = T("SYS14.CloneProfile", "Clone");
        BtnSaveProfile.Content = T("Common.Save", "Save");
        BtnActivateProfile.Content = T("SYS14.Activate", "Activate");
        BtnImport.Content = T("SYS14.Import", "Import");
        BtnExport.Content = T("SYS14.Export", "Export");
        BtnReload.Content = T("Common.Refresh", "Refresh");
        BtnPreview.Content = T("SYS14.Preview", "Preview");
        BtnApplyLive.Content = T("SYS14.ApplyLive", "Apply live");

        TabResources.Header = T("SYS14.Tab.Resources", "Resources");
        TabProperties.Header = T("SYS14.Tab.Properties", "UI overrides");
        TabAdvanced.Header = T("SYS14.Tab.Advanced", "Advanced XAML");

        TxtResourcesHelp.Text = T(
            "SYS14.ResourcesHelp",
            "Override ResourceDictionary values. Empty override means inherit from the previous level.");

        TxtPropertiesHelp.Text = T(
            "SYS14.PropertiesHelp",
            "TYPE selects every matching WPF control type; NAME selects a specific x:Name. Common properties include FontSize, Height, Padding, Margin, Background, RowHeight and Visibility.");

        TxtAdvancedHelp.Text = T(
            "SYS14.AdvancedHelp",
            "Self-contained ResourceDictionary for Styles and ControlTemplates. External Source, clr-namespace, x:Class and event handlers are intentionally blocked.");

        ColResourceKey.Header = T("SYS14.Column.Resource", "Resource");
        ColResourceType.Header = T("SYS14.Column.Type", "Type");
        ColInherited.Header = T("SYS14.Column.Inherited", "Inherited");
        ColOverride.Header = T("SYS14.Column.Override", "Override");
        ColEffective.Header = T("SYS14.Column.Effective", "Effective");
        ColSource.Header = T("SYS14.Column.Source", "Source");

        ColPropertyActive.Header = T("SYS14.Column.Active", "Active");
        ColSelectorKind.Header = T("SYS14.Column.SelectorKind", "Selector kind");
        ColSelector.Header = T("SYS14.Column.Selector", "Selector");
        ColProperty.Header = T("SYS14.Column.Property", "Property");
        ColValue.Header = T("SYS14.Column.Value", "Value");

        BtnAddProperty.Content = T("SYS14.AddOverride", "Add override");
        BtnDeleteProperty.Content = T("Common.Delete", "Delete");
        BtnValidateXaml.Content = T("SYS14.ValidateXaml", "Validate XAML");
        BtnInsertTemplate.Content = T("SYS14.InsertTemplate", "Insert template");
        BtnRestoreScope.Content = T("SYS14.RestoreInherited", "Restore inherited");

        TxtPreviewTitle.Text = T("SYS14.LivePreview", "Live preview");
        TxtPreviewBody.Text = T(
            "SYS14.LivePreviewHelp",
            "The preview uses the currently edited scope without changing the active system profile.");

        BtnOpenLog.Content = T("SYS14.OpenLog", "Open LOG03");
    }

    private void ReloadAll(
        string? preferredProfileCode = null,
        string? preferredScopeKey = null)
    {
        CommitEditorToCurrentLayer();
        _suppressSelectionEvents = true;

        try
        {
            _modules = _loadModules()
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Code)
                .ToList();

            _transactions = _loadTransactions()
                .OrderBy(x => x.Code)
                .ToList();

            BuildScopes();

            var profileSummaries = _service.GetProfiles().ToList();

            if (profileSummaries.Count == 0)
            {
                var created = _service.EnsureDefaultProfile(_user.DisplayName);
                profileSummaries = _service.GetProfiles().ToList();
                preferredProfileCode = created.Code;
            }

            CmbProfiles.ItemsSource = profileSummaries;

            var desiredProfile =
                preferredProfileCode
                ?? _profile?.Code
                ?? _service.GetActiveProfileCode()
                ?? profileSummaries.FirstOrDefault()?.Code;

            CmbProfiles.SelectedItem = profileSummaries.FirstOrDefault(x =>
                string.Equals(x.Code, desiredProfile, StringComparison.OrdinalIgnoreCase))
                ?? profileSummaries.FirstOrDefault();

            if (CmbProfiles.SelectedItem is DmsUiProfileSummary selectedProfile)
            {
                LoadProfile(selectedProfile.Code);
            }

            CmbScope.ItemsSource = _scopes;

            var desiredScope = preferredScopeKey ?? _scope?.ScopeKey ?? "GLOBAL";

            CmbScope.SelectedItem = _scopes.FirstOrDefault(x =>
                string.Equals(x.ScopeKey, desiredScope, StringComparison.OrdinalIgnoreCase))
                ?? _scopes.FirstOrDefault();

            _scope = CmbScope.SelectedItem as DmsUiScopeOption;
            LoadCurrentLayerToEditor();
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        UpdateStatus();
        PreviewCurrentLayer();
    }

    private void BuildScopes()
    {
        var result = new List<DmsUiScopeOption>
        {
            new(
                "GLOBAL",
                "GLOBAL",
                T("SYS14.Scope.Global", "GLOBAL — whole client"))
        };

        var configuredByCode = _modules
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        var configuredByName = _modules
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var module in _modules)
        {
            result.Add(new DmsUiScopeOption(
                "MODULE",
                module.Code,
                $"MODULE — {module.Code} — {module.Name}"));
        }

        var discoveredModules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transaction in _transactions)
        {
            var moduleCode = ResolveModuleCode(transaction.Module, configuredByCode, configuredByName);

            if (!configuredByCode.ContainsKey(moduleCode) &&
                !discoveredModules.ContainsKey(moduleCode))
            {
                discoveredModules[moduleCode] = transaction.Module;
            }
        }

        foreach (var pair in discoveredModules.OrderBy(x => x.Key))
        {
            result.Add(new DmsUiScopeOption(
                "MODULE",
                pair.Key,
                $"MODULE — {pair.Key} — {pair.Value} ({T("SYS14.Discovered", "discovered")})"));
        }

        foreach (var transaction in _transactions.OrderBy(x => x.Code))
        {
            var moduleCode = ResolveModuleCode(transaction.Module, configuredByCode, configuredByName);

            result.Add(new DmsUiScopeOption(
                "TRANSACTION",
                transaction.Code,
                $"TRANSACTION — {transaction.Code} — {transaction.Name}",
                moduleCode));
        }

        _scopes = result
            .GroupBy(x => x.ScopeKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private static string ResolveModuleCode(
        string? rawModule,
        IReadOnlyDictionary<string, DmsModuleDefinition> byCode,
        IReadOnlyDictionary<string, DmsModuleDefinition> byName)
    {
        if (string.IsNullOrWhiteSpace(rawModule))
        {
            return "UNASSIGNED";
        }

        var value = rawModule.Trim();

        if (byCode.TryGetValue(value, out var codeMatch))
        {
            return codeMatch.Code;
        }

        if (byName.TryGetValue(value, out var nameMatch))
        {
            return nameMatch.Code;
        }

        var normalized = DmsUiProfileService.NormalizeCode(value);
        return string.IsNullOrWhiteSpace(normalized) ? "UNASSIGNED" : normalized;
    }

    private void ProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents ||
            CmbProfiles.SelectedItem is not DmsUiProfileSummary summary)
        {
            return;
        }

        CommitEditorToCurrentLayer();
        LoadProfile(summary.Code);
        LoadCurrentLayerToEditor();
        UpdateStatus();
        PreviewCurrentLayer();
    }

    private void ScopeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents)
        {
            return;
        }

        CommitEditorToCurrentLayer();
        _scope = CmbScope.SelectedItem as DmsUiScopeOption;
        LoadCurrentLayerToEditor();
        UpdateStatus();
        PreviewCurrentLayer();
    }

    private void LoadProfile(string code)
    {
        _profile = _service.LoadProfile(code);
        _loadedSnapshot = _profile.Clone();
    }

    private DmsUiLayer? GetCurrentLayer(bool create)
    {
        if (_profile is null || _scope is null)
        {
            return null;
        }

        if (string.Equals(_scope.Kind, "GLOBAL", StringComparison.OrdinalIgnoreCase))
        {
            return _profile.Global;
        }

        if (string.Equals(_scope.Kind, "MODULE", StringComparison.OrdinalIgnoreCase))
        {
            if (_profile.Modules.TryGetValue(_scope.Code, out var layer))
            {
                return layer;
            }

            if (!create)
            {
                return null;
            }

            layer = new DmsUiLayer();
            _profile.Modules[_scope.Code] = layer;
            return layer;
        }

        if (_profile.Transactions.TryGetValue(_scope.Code, out var transactionLayer))
        {
            return transactionLayer;
        }

        if (!create)
        {
            return null;
        }

        transactionLayer = new DmsUiLayer();
        _profile.Transactions[_scope.Code] = transactionLayer;
        return transactionLayer;
    }

    private void LoadCurrentLayerToEditor()
    {
        _resourceRows.Clear();
        _propertyRows.Clear();

        if (_profile is null || _scope is null)
        {
            TxtAdvancedXaml.Text = string.Empty;
            return;
        }

        var layer = GetCurrentLayer(create: false) ?? new DmsUiLayer();
        var inventory = DmsUiProfileRuntime.GetApplicationResourceInventory();

        var keys = inventory
            .Select(x => x.Key)
            .Concat(layer.Resources.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var inventoryMap = inventory.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var key in keys)
        {
            inventoryMap.TryGetValue(key, out var descriptor);

            var inherited = GetInheritedResourceValue(
                key,
                descriptor?.CurrentValue ?? string.Empty,
                out var inheritedSource);

            layer.Resources.TryGetValue(key, out var ownOverride);

            var effective = !string.IsNullOrWhiteSpace(ownOverride)
                ? ownOverride
                : inherited;

            _resourceRows.Add(new DmsUiResourceRow
            {
                Key = key,
                ResourceType = descriptor?.ResourceType ?? "Custom",
                InheritedValue = inherited,
                OverrideValue = ownOverride ?? string.Empty,
                EffectiveValue = effective,
                Source = !string.IsNullOrWhiteSpace(ownOverride)
                    ? _scope.Kind
                    : inheritedSource
            });
        }

        foreach (var rule in layer.Properties)
        {
            _propertyRows.Add(rule.Clone());
        }

        TxtAdvancedXaml.Text = layer.AdvancedXaml ?? string.Empty;
        TxtPreviewScope.Text = _scope.DisplayName;
    }

    private string GetInheritedResourceValue(
        string key,
        string factoryValue,
        out string source)
    {
        source = "Factory";

        if (_profile is null || _scope is null)
        {
            return factoryValue;
        }

        var value = factoryValue;

        if (!string.Equals(_scope.Kind, "GLOBAL", StringComparison.OrdinalIgnoreCase) &&
            _profile.Global.Resources.TryGetValue(key, out var global))
        {
            value = global;
            source = "GLOBAL";
        }

        if (string.Equals(_scope.Kind, "TRANSACTION", StringComparison.OrdinalIgnoreCase) &&
            _profile.Modules.TryGetValue(_scope.ModuleCode, out var moduleLayer) &&
            moduleLayer.Resources.TryGetValue(key, out var moduleValue))
        {
            value = moduleValue;
            source = $"MODULE:{_scope.ModuleCode}";
        }

        return value;
    }

    private void CommitEditorToCurrentLayer()
    {
        if (_profile is null || _scope is null)
        {
            return;
        }

        GridResources.CommitEdit(DataGridEditingUnit.Cell, true);
        GridResources.CommitEdit(DataGridEditingUnit.Row, true);
        GridProperties.CommitEdit(DataGridEditingUnit.Cell, true);
        GridProperties.CommitEdit(DataGridEditingUnit.Row, true);

        var layer = GetCurrentLayer(create: true)!;
        layer.Resources.Clear();

        foreach (var row in _resourceRows)
        {
            if (!string.IsNullOrWhiteSpace(row.OverrideValue))
            {
                layer.Resources[row.Key] = row.OverrideValue.Trim();
            }
        }

        layer.Properties = _propertyRows.Select(x => x.Clone()).ToList();
        layer.AdvancedXaml = TxtAdvancedXaml.Text ?? string.Empty;
        RemoveEmptyLayerIfNeeded();
    }

    private void RemoveEmptyLayerIfNeeded()
    {
        if (_profile is null || _scope is null ||
            string.Equals(_scope.Kind, "GLOBAL", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var layer = GetCurrentLayer(create: false);

        if (layer is null)
        {
            return;
        }

        var empty = layer.Resources.Count == 0 &&
                    layer.Properties.Count == 0 &&
                    string.IsNullOrWhiteSpace(layer.AdvancedXaml);

        if (!empty)
        {
            return;
        }

        if (string.Equals(_scope.Kind, "MODULE", StringComparison.OrdinalIgnoreCase))
        {
            _profile.Modules.Remove(_scope.Code);
        }
        else
        {
            _profile.Transactions.Remove(_scope.Code);
        }
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var code = DmsTextPromptDialog.Show(
            owner,
            T("SYS14.NewProfile", "New profile"),
            T("SYS14.ProfileCodePrompt", "Profile code:"),
            "CUSTOM");

        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var name = DmsTextPromptDialog.Show(
            owner,
            T("SYS14.NewProfile", "New profile"),
            T("SYS14.ProfileNamePrompt", "Profile name:"),
            code);

        if (name is null)
        {
            return;
        }

        try
        {
            var profile = _service.CreateProfile(code, name, _user.DisplayName);

            _logger.AuditCreated(
                "SYS14",
                "UiProfile",
                profile.Code,
                _user.DisplayName,
                $"Name={profile.Name}; Version={profile.Version}");

            ReloadAll(profile.Code, "GLOBAL");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void CloneProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null)
        {
            return;
        }

        CommitEditorToCurrentLayer();
        var owner = Window.GetWindow(this);

        var code = DmsTextPromptDialog.Show(
            owner,
            T("SYS14.CloneProfile", "Clone profile"),
            T("SYS14.ProfileCodePrompt", "Profile code:"),
            $"{_profile.Code}_COPY");

        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var name = DmsTextPromptDialog.Show(
            owner,
            T("SYS14.CloneProfile", "Clone profile"),
            T("SYS14.ProfileNamePrompt", "Profile name:"),
            $"{_profile.Name} Copy");

        if (name is null)
        {
            return;
        }

        try
        {
            var clone = _service.CloneProfile(
                _profile,
                code,
                name,
                _user.DisplayName);

            _logger.AuditCreated(
                "SYS14",
                "UiProfile",
                clone.Code,
                _user.DisplayName,
                $"ClonedFrom={_profile.Code}; Name={clone.Name}");

            ReloadAll(clone.Code, "GLOBAL");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e) =>
        SaveCurrentProfile();

    private bool SaveCurrentProfile()
    {
        if (_profile is null)
        {
            return false;
        }

        CommitEditorToCurrentLayer();
        var issues = DmsUiProfileValidator.Validate(_profile);

        if (issues.Any(x => x.Severity == "ERROR"))
        {
            TxtStatus.Text = string.Join(
                Environment.NewLine,
                issues.Where(x => x.Severity == "ERROR")
                    .Take(10)
                    .Select(x => $"{x.Scope}: {x.Details}"));
            return false;
        }

        try
        {
            var before = _loadedSnapshot?.Clone();
            _profile.Version = Math.Max(1, _profile.Version + 1);
            _profile.ModifiedBy = _user.DisplayName;
            _service.SaveProfile(_profile);
            WriteAuditDiff(before, _profile);
            _loadedSnapshot = _profile.Clone();

            TxtStatus.Text = string.Format(
                T("SYS14.Status.Saved", "Saved {0} v{1}."),
                _profile.Code,
                _profile.Version);

            ReloadProfileSummariesKeepSelection();
            return true;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            return false;
        }
    }

    private void ActivateProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null || !SaveCurrentProfile())
        {
            return;
        }

        try
        {
            var old = _service.GetActiveProfileCode();
            _service.SetActiveProfile(_profile.Code);

            _logger.AuditChange(
                "SYS14",
                "UiActiveProfile",
                "ACTIVE",
                "ProfileCode",
                old,
                _profile.Code,
                _user.DisplayName);

            _logger.AdminAction(
                "SYS14",
                "PROFILE_ACTIVATED",
                _user.DisplayName,
                $"Profile={_profile.Code}; Version={_profile.Version}");

            _reloadActive();

            TxtStatus.Text = string.Format(
                T("SYS14.Status.Activated", "Active profile: {0}."),
                _profile.Code);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "DMS UI profile (*.dms-ui.zip;*.zip)|*.dms-ui.zip;*.zip|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var profile = _service.ImportProfile(dialog.FileName, _user.DisplayName);

            _logger.AuditCreated(
                "SYS14",
                "UiProfile",
                profile.Code,
                _user.DisplayName,
                $"ImportedFrom={dialog.FileName}; Version={profile.Version}");

            ReloadAll(profile.Code, "GLOBAL");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null)
        {
            return;
        }

        CommitEditorToCurrentLayer();

        var dialog = new SaveFileDialog
        {
            Filter = "DMS UI profile (*.dms-ui.zip)|*.dms-ui.zip|ZIP (*.zip)|*.zip",
            FileName = $"{_profile.Code}-v{_profile.Version}.dms-ui.zip"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _service.ExportProfile(_profile, dialog.FileName);

            _logger.AdminAction(
                "SYS14",
                "PROFILE_EXPORTED",
                _user.DisplayName,
                $"Profile={_profile.Code}; Version={_profile.Version}; File={dialog.FileName}");

            TxtStatus.Text = string.Format(
                T("SYS14.Status.Exported", "Exported: {0}"),
                dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e) =>
        ReloadAll(_profile?.Code, _scope?.ScopeKey);

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        CommitEditorToCurrentLayer();
        PreviewCurrentLayer();
    }

    private void ApplyLive_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null)
        {
            return;
        }

        CommitEditorToCurrentLayer();
        var issues = DmsUiProfileValidator.Validate(_profile);

        if (issues.Any(x => x.Severity == "ERROR"))
        {
            TxtStatus.Text = string.Join(
                Environment.NewLine,
                issues.Where(x => x.Severity == "ERROR")
                    .Take(10)
                    .Select(x => $"{x.Scope}: {x.Details}"));
            return;
        }

        _applyLive(_profile);

        _logger.AdminAction(
            "SYS14",
            "PROFILE_PREVIEW_APPLIED",
            _user.DisplayName,
            $"Profile={_profile.Code}; Version={_profile.Version}; Scope={_scope?.ScopeKey}");

        TxtStatus.Text = T(
            "SYS14.Status.LiveApplied",
            "Unsaved profile applied to the current client for testing.");
    }

    private void PreviewCurrentLayer()
    {
        var layers = GetEffectivePreviewLayers();
        var issues = _previewRuntime.ApplyPreview(PreviewRoot, layers);

        TxtPreviewIssues.Text = issues.Count == 0
            ? T("SYS14.Preview.Ok", "Preview: OK")
            : string.Join(
                Environment.NewLine,
                issues.Take(8).Select(x => $"{x.Selector}.{x.Property}: {x.Details}"));
    }

    private IReadOnlyList<DmsUiLayer> GetEffectivePreviewLayers()
    {
        if (_profile is null || _scope is null)
        {
            return Array.Empty<DmsUiLayer>();
        }

        var result = new List<DmsUiLayer>
        {
            _profile.Global
        };

        if (string.Equals(_scope.Kind, "MODULE", StringComparison.OrdinalIgnoreCase) &&
            _profile.Modules.TryGetValue(_scope.Code, out var moduleLayer))
        {
            result.Add(moduleLayer);
        }

        if (string.Equals(_scope.Kind, "TRANSACTION", StringComparison.OrdinalIgnoreCase))
        {
            if (_profile.Modules.TryGetValue(_scope.ModuleCode, out var parentModuleLayer))
            {
                result.Add(parentModuleLayer);
            }

            if (_profile.Transactions.TryGetValue(_scope.Code, out var transactionLayer))
            {
                result.Add(transactionLayer);
            }
        }

        return result;
    }

    private void AddProperty_Click(object sender, RoutedEventArgs e)
    {
        _propertyRows.Add(new DmsUiPropertyOverride
        {
            SelectorKind = "TYPE",
            Selector = "DataGrid",
            Property = "RowHeight",
            Value = "32",
            IsActive = true
        });
    }

    private void DeleteProperty_Click(object sender, RoutedEventArgs e)
    {
        if (GridProperties.SelectedItem is DmsUiPropertyOverride row)
        {
            _propertyRows.Remove(row);
        }
    }

    private void ValidateXaml_Click(object sender, RoutedEventArgs e)
    {
        var issues = DmsUiXamlValidator.Validate(TxtAdvancedXaml.Text);

        TxtStatus.Text = issues.Count == 0
            ? T(
                "SYS14.Xaml.Valid",
                "Advanced XAML is valid and safe for DMS override loading.")
            : string.Join(Environment.NewLine, issues);
    }

    private void InsertTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TxtAdvancedXaml.Text))
        {
            return;
        }

        TxtAdvancedXaml.Text =
            "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\r\n" +
            "                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\r\n" +
            "    <!-- Example: override a named style without editing factory Theme.xaml. -->\r\n" +
            "    <Style x:Key=\"MyCustomButtonStyle\"\r\n" +
            "           TargetType=\"{x:Type Button}\">\r\n" +
            "        <Setter Property=\"Height\" Value=\"38\"/>\r\n" +
            "        <Setter Property=\"FontSize\" Value=\"14\"/>\r\n" +
            "    </Style>\r\n" +
            "</ResourceDictionary>";
    }

    private void RestoreScope_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null || _scope is null)
        {
            return;
        }

        if (string.Equals(_scope.Kind, "GLOBAL", StringComparison.OrdinalIgnoreCase))
        {
            _profile.Global = new DmsUiLayer();
        }
        else if (string.Equals(_scope.Kind, "MODULE", StringComparison.OrdinalIgnoreCase))
        {
            _profile.Modules.Remove(_scope.Code);
        }
        else
        {
            _profile.Transactions.Remove(_scope.Code);
        }

        LoadCurrentLayerToEditor();
        PreviewCurrentLayer();
        TxtStatus.Text = T(
            "SYS14.Status.ScopeRestored",
            "Current scope now inherits from its parent.");
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e) =>
        _executeTransaction("LOG03");

    private void ReloadProfileSummariesKeepSelection()
    {
        _suppressSelectionEvents = true;

        try
        {
            var summaries = _service.GetProfiles().ToList();
            CmbProfiles.ItemsSource = summaries;
            CmbProfiles.SelectedItem = summaries.FirstOrDefault(x =>
                string.Equals(x.Code, _profile?.Code, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _suppressSelectionEvents = false;
        }
    }

    private void UpdateStatus()
    {
        var active = _service.GetActiveProfileCode();

        TxtStatus.Text = string.Format(
            T(
                "SYS14.Status.Current",
                "Profile={0}; Active={1}; Scope={2}; Modules={3}; Transactions={4}"),
            _profile?.Code ?? "-",
            string.IsNullOrWhiteSpace(active) ? T("SYS14.None", "none") : active,
            _scope?.ScopeKey ?? "-",
            _modules.Count,
            _transactions.Count);
    }

    private void WriteAuditDiff(DmsUiProfile? before, DmsUiProfile after)
    {
        if (before is null)
        {
            _logger.AuditCreated(
                "SYS14",
                "UiProfile",
                after.Code,
                _user.DisplayName,
                $"Name={after.Name}; Version={after.Version}");
            return;
        }

        AuditLayer("GLOBAL", before.Global, after.Global);

        var moduleKeys = before.Modules.Keys
            .Concat(after.Modules.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var key in moduleKeys)
        {
            before.Modules.TryGetValue(key, out var oldLayer);
            after.Modules.TryGetValue(key, out var newLayer);
            AuditLayer($"MODULE:{key}", oldLayer, newLayer);
        }

        var transactionKeys = before.Transactions.Keys
            .Concat(after.Transactions.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var key in transactionKeys)
        {
            before.Transactions.TryGetValue(key, out var oldLayer);
            after.Transactions.TryGetValue(key, out var newLayer);
            AuditLayer($"TRANSACTION:{key}", oldLayer, newLayer);
        }
    }

    private void AuditLayer(string scope, DmsUiLayer? before, DmsUiLayer? after)
    {
        before ??= new DmsUiLayer();
        after ??= new DmsUiLayer();

        var resourceKeys = before.Resources.Keys
            .Concat(after.Resources.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var key in resourceKeys)
        {
            before.Resources.TryGetValue(key, out var oldValue);
            after.Resources.TryGetValue(key, out var newValue);

            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                _logger.AuditChange(
                    "SYS14",
                    "UiResourceOverride",
                    $"{_profile?.Code}:{scope}:{key}",
                    "Value",
                    oldValue,
                    newValue,
                    _user.DisplayName);
            }
        }

        var oldProperties = before.Properties.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var newProperties = after.Properties.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var id in oldProperties.Keys
                     .Concat(newProperties.Keys)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            oldProperties.TryGetValue(id, out var oldRule);
            newProperties.TryGetValue(id, out var newRule);

            if (oldRule is null && newRule is not null)
            {
                _logger.AuditCreated(
                    "SYS14",
                    "UiPropertyOverride",
                    id,
                    _user.DisplayName,
                    $"Profile={_profile?.Code}; Scope={scope}; Selector={newRule.SelectorKind}:{newRule.Selector}; Property={newRule.Property}; Value={newRule.Value}");
                continue;
            }

            if (oldRule is not null && newRule is null)
            {
                _logger.AuditDeleted(
                    "SYS14",
                    "UiPropertyOverride",
                    id,
                    _user.DisplayName,
                    $"Profile={_profile?.Code}; Scope={scope}; Selector={oldRule.SelectorKind}:{oldRule.Selector}; Property={oldRule.Property}; Value={oldRule.Value}");
                continue;
            }

            var oldText = SerializeRule(oldRule!);
            var newText = SerializeRule(newRule!);

            if (!string.Equals(oldText, newText, StringComparison.Ordinal))
            {
                _logger.AuditChange(
                    "SYS14",
                    "UiPropertyOverride",
                    id,
                    "Definition",
                    oldText,
                    newText,
                    _user.DisplayName);
            }
        }

        var oldHash = HashText(before.AdvancedXaml);
        var newHash = HashText(after.AdvancedXaml);

        if (!string.Equals(oldHash, newHash, StringComparison.Ordinal))
        {
            _logger.AuditChange(
                "SYS14",
                "UiAdvancedXaml",
                $"{_profile?.Code}:{scope}",
                "SHA256",
                oldHash,
                newHash,
                _user.DisplayName);
        }
    }

    private static string SerializeRule(DmsUiPropertyOverride rule) =>
        $"Active={rule.IsActive}; Selector={rule.SelectorKind}:{rule.Selector}; Property={rule.Property}; Value={rule.Value}";

    private static string HashText(string? value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes);
    }

    private void ShowError(string message)
    {
        DmsMessage.Show(
            message,
            T("SYS14.Title", "SYS14 — Theme & UI Designer"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
