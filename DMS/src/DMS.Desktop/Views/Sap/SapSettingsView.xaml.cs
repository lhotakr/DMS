using DMS.Core.Sap;
using DMS.Desktop.Logging;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Sap;

public partial class SapSettingsView : UserControl
{
    private readonly SapStoragePaths _storagePaths;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    public SapSettingsView(
        SapStoragePaths storagePaths,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _storagePaths = storagePaths;
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName) ? "UNKNOWN" : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        ApplyLocalization();
        BuildTabs();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("SAPSET.Title");
        TxtSubtitle.Text = T("SAPSET.Subtitle");
    }

    private void BuildTabs()
    {
        TabsSettings.Items.Clear();

        TabsSettings.Items.Add(CreateTab(
            T("SAPSET.Tab.MaterialRules"),
            () => new JsonRulesEditorView(
                T("SAPSET.Tab.MaterialRules"),
                _storagePaths.MaterialRangesFilePath,
                translate: key => T(key),
                logAction: (action, detail) => LogAction(action, detail))));

        TabsSettings.Items.Add(CreateTab(
            T("SAPSET.Tab.DecorationRules"),
            () => new JsonRulesEditorView(
                T("SAPSET.Tab.DecorationRules"),
                GetConfigPath("sap-decoration-rules.json"),
                translate: key => T(key),
                logAction: (action, detail) => LogAction(action, detail))));

        TabsSettings.Items.Add(CreateTab(
            T("SAPSET.Tab.StatusRules"),
            () => new JsonRulesEditorView(
                T("SAPSET.Tab.StatusRules"),
                GetConfigPath("sap-material-status-rules.json"),
                translate: key => T(key),
                logAction: (action, detail) => LogAction(action, detail))));

        TabsSettings.Items.Add(CreateTab(
            T("SAPSET.Tab.ValidationRules"),
            () => new SapValidationRulesEditorView(
                GetConfigPath("sap-validation-rules.json"),
                translate: key => T(key),
                logAction: (action, detail) => LogAction(action, detail))));

        TabsSettings.Items.Add(CreateTab(
            T("SAPSET.Tab.Paths"),
            () => CreatePathsTab()));
    }

    private TabItem CreateTab(string header, Func<UIElement> contentFactory)
    {
        UIElement content;
        try
        {
            content = contentFactory();
        }
        catch (Exception ex)
        {
            LogAction("OpenTabFailed", $"Tab={header}; Error={ex.Message}");
            content = CreateErrorPanel(header, ex.Message);
        }

        return new TabItem
        {
            Header = header,
            Content = content
        };
    }

    private UIElement CreatePathsTab()
    {
        var panel = new StackPanel { Margin = new Thickness(14) };

        var text = new TextBlock
        {
            Text = TF("SAPSET.Paths.Content",
                _storagePaths.RootDirectory,
                _storagePaths.ConfigDirectory,
                _storagePaths.SapMirrorDirectory),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        panel.Children.Add(text);
        return panel;
    }

    private UIElement CreateErrorPanel(string title, string errorMessage)
    {
        var panel = new StackPanel { Margin = new Thickness(14) };

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        var errorBlock = new TextBlock
        {
            Text = TF("SAPSET.TabOpenFailed", errorMessage),
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        };
        errorBlock.SetResourceReference(TextBlock.ForegroundProperty, "DmsMutedForegroundBrush");

        panel.Children.Add(titleBlock);
        panel.Children.Add(errorBlock);
        return panel;
    }

    private void LogAction(string action, string detail)
    {
        _logger?.AdminAction(
            "SAPSET",
            action,
            _currentUserName,
            detail);
    }

    private string GetConfigPath(string fileName)
        => System.IO.Path.Combine(_storagePaths.ConfigDirectory, fileName);

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;
        return IsMissing(value, key) ? key : value;
    }

    private string TF(string key, params object[] args)
    {
        var value = _translateFormat?.Invoke(key, args);
        if (!string.IsNullOrWhiteSpace(value) && !IsMissing(value, key))
            return value;

        var pattern = T(key);
        try { return string.Format(pattern, args); }
        catch { return pattern; }
    }

    private static bool IsMissing(string? value, string key)
        => string.IsNullOrWhiteSpace(value)
           || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
}