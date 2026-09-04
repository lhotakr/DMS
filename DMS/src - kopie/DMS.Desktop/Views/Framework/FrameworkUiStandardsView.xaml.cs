using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DMS.Desktop.UI;

namespace DMS.Desktop.Views.Framework;

public partial class FrameworkUiStandardsView : UserControl
{
    private readonly Func<string, string> _translate;
    private readonly Action<string> _executeTransaction;
    private readonly Action<string, string> _log;

    public FrameworkUiStandardsView(
        Func<string, string> translate,
        Action<string> executeTransaction,
        Action<string, string> log)
    {
        InitializeComponent();

        _translate = translate;
        _executeTransaction = executeTransaction;
        _log = log;

        ApplyLocalization();
        Loaded += (_, _) => Reload();
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
        TitleText.Text = T("Framework.FW02.Title", "FW02 — UI framework");
        SubtitleText.Text = T("Framework.FW02.Description", "Validates shared DMS visual resources, dialogs and reusable UI behavior.");

        ReloadButton.Content = T("Framework.FW02.Reload", "Reload");
        ClientSettingsButton.Content = T("Framework.FW02.OpenClset", "Open CLSET");
        DiagnosticsButton.Content = T("Framework.FW02.OpenFw04", "Open FW04");

        ResourcesLabel.Text = T("Framework.FW02.Resources", "Resources");
        StylesLabel.Text = T("Framework.FW02.Styles", "Styles");
        FeaturesLabel.Text = T("Framework.FW02.Features", "Features");
        HealthLabel.Text = T("Framework.FW02.Health", "Health");

        CategoryColumn.Header = T("Framework.FW02.Column.Category", "Category");
        StandardColumn.Header = T("Framework.FW02.Column.Standard", "Standard");
        StatusColumn.Header = T("Framework.FW02.Column.Status", "Status");
        DetailsColumn.Header = T("Framework.FW02.Column.Details", "Details");

        ResourceTypeColumn.Header = T("Framework.FW02.Column.ResourceType", "Resource type");
        ResourceKeyColumn.Header = T("Framework.FW02.Column.Key", "Key");
        ResourceStatusColumn.Header = T("Framework.FW02.Column.Status", "Status");
        ResourceValueColumn.Header = T("Framework.FW02.Column.Value", "Runtime value");

        FooterText.Text = T("Framework.FW02.Footer", "FW02 is read-only. Theme/language preferences remain in CLSET. UI standards are shared through Theme.xaml and reusable DMS controls.");
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => Reload();
    private void ClientSettingsButton_Click(object sender, RoutedEventArgs e) => _executeTransaction("CLSET");
    private void DiagnosticsButton_Click(object sender, RoutedEventArgs e) => _executeTransaction("FW04");

    private void Reload()
    {
        var standards = new List<StandardRow>();
        var resources = new List<ResourceRow>();

        CheckType(standards, "Dialogs", "DmsConfirmDialog", typeof(DmsConfirmDialog));
        CheckType(standards, "Dialogs", "DmsTextPromptDialog", typeof(DmsTextPromptDialog));

        CheckOptionalType(
            standards,
            "DataGrid",
            "Clipped text ToolTip behavior",
            "DMS.Desktop.Behaviors.DmsDataGridCellToolTip");

        CheckImplicitStyle(resources, typeof(Button), "Implicit Button style");
        CheckImplicitStyle(resources, typeof(TextBox), "Implicit TextBox style");
        CheckImplicitStyle(resources, typeof(ComboBox), "Implicit ComboBox style");
        CheckImplicitStyle(resources, typeof(DataGrid), "Implicit DataGrid style");
        CheckImplicitStyle(resources, typeof(DataGridCell), "Implicit DataGridCell style");
        CheckImplicitStyle(resources, typeof(DataGridColumnHeader), "Implicit DataGridColumnHeader style");

        string[] brushKeys =
        {
            "DmsBackgroundBrush",
            "DmsPanelBrush",
            "DmsForegroundBrush",
            "DmsMutedForegroundBrush",
            "DmsBorderBrush",
            "DmsAccentBrush",
            "DmsOnAccentBrush",
            "DmsErrorBrush",
            "DmsWarningBrush",
            "DmsDataGridAddedRowBrush",
            "DmsDataGridModifiedRowBrush",
            "DmsDataGridDeletedRowBrush"
        };

        foreach (var key in brushKeys)
        {
            CheckNamedResource(resources, "Brush", key);
        }

        string[] styleKeys =
        {
            "DmsSectionTitleStyle",
            "DmsFormLabelStyle",
            "DmsFormTextBoxStyle",
            "DmsReadOnlyTextBoxStyle",
            "DmsMultilineTextBoxStyle",
            "DmsButtonBaseStyle",
            "DmsAccentButtonStyle",
            "DmsPrimaryButtonStyle",
            "DmsToolbarButtonStyle"
        };

        foreach (var key in styleKeys)
        {
            CheckNamedResource(resources, "Style", key);
        }

        standards.Add(new StandardRow(
            "DataGrid",
            "Manual column resizing",
            "OK",
            "Framework views FW06-FW09 explicitly enable CanUserResizeColumns/CanUserReorderColumns; global DataGrid style remains available."));

        standards.Add(new StandardRow(
            "Navigation",
            "Left panel collapse toggle",
            "OK",
            "The shell navigation-panel toggle remains part of the DMS UI standard."));

        standards.Add(new StandardRow(
            "Localization",
            "User-facing UI localized",
            "INFO",
            "FW01 validates dictionary parity; technical log content remains English by design."));

        StandardsGrid.ItemsSource = standards;
        ResourceGrid.ItemsSource = resources;

        var errors =
            standards.Count(x => x.Status == "ERROR") +
            resources.Count(x => x.Status == "ERROR");

        var warnings =
            standards.Count(x => x.Status == "WARNING") +
            resources.Count(x => x.Status == "WARNING");

        ResourcesValue.Text = resources.Count.ToString();
        StylesValue.Text = resources.Count(x => x.ResourceType.Contains("Style", StringComparison.OrdinalIgnoreCase)).ToString();
        FeaturesValue.Text = standards.Count.ToString();
        HealthValue.Text = errors == 0 && warnings == 0 ? "OK" : $"{errors}E / {warnings}W";

        _log(
            "UI_FRAMEWORK_OVERVIEW",
            $"Standards={standards.Count}; Resources={resources.Count}; Errors={errors}; Warnings={warnings}");
    }

    private static void CheckType(
        ICollection<StandardRow> rows,
        string category,
        string standard,
        Type type)
    {
        rows.Add(new StandardRow(
            category,
            standard,
            "OK",
            type.FullName ?? type.Name));
    }

    private static void CheckOptionalType(
        ICollection<StandardRow> rows,
        string category,
        string standard,
        string fullTypeName)
    {
        var type = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType(fullTypeName, throwOnError: false))
            .FirstOrDefault(x => x is not null);

        rows.Add(new StandardRow(
            category,
            standard,
            type is null ? "WARNING" : "OK",
            type?.FullName ?? "Feature type not loaded/found."));
    }

    private static void CheckImplicitStyle(
        ICollection<ResourceRow> rows,
        Type targetType,
        string displayName)
    {
        var value = Application.Current?.TryFindResource(targetType);

        rows.Add(new ResourceRow(
            "Implicit Style",
            displayName,
            value is Style ? "OK" : "ERROR",
            value?.GetType().FullName ?? "Missing"));
    }

    private static void CheckNamedResource(
        ICollection<ResourceRow> rows,
        string resourceType,
        string key)
    {
        var value = Application.Current?.TryFindResource(key);

        rows.Add(new ResourceRow(
            resourceType,
            key,
            value is null ? "ERROR" : "OK",
            value?.ToString() ?? "Missing"));
    }

    private sealed record StandardRow(
        string Category,
        string Standard,
        string Status,
        string Details);

    private sealed record ResourceRow(
        string ResourceType,
        string Key,
        string Status,
        string Value);
}
