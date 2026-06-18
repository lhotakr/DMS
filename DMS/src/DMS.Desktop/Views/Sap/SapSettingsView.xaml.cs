using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Sap;

public partial class SapSettingsView : UserControl
{
    private readonly string _basePath;
    private readonly string _configPath;

    public SapSettingsView()
    {
        InitializeComponent();

        _basePath = @"Z:\SAP\DMS-db\DEV";
        _configPath = Path.Combine(_basePath, "Config");

        BuildTabs();
    }

    private void BuildTabs()
    {
        TabsSettings.Items.Add(new TabItem
        {
            Header = "Pravidla materiálů",
            Content = new JsonRulesEditorView(
                "Pravidla materiálů",
                GetConfigPath("sap-material-rules.json"))
        });

        TabsSettings.Items.Add(new TabItem
        {
            Header = "Pravidla dekorací",
            Content = new JsonRulesEditorView(
                "Pravidla dekorací",
                GetConfigPath("sap-decoration-rules.json"))
        });

        TabsSettings.Items.Add(new TabItem
        {
            Header = "Statusy materiálu",
            Content = new JsonRulesEditorView(
                "Statusy materiálu",
                GetConfigPath("sap-material-status-rules.json"))
        });

        TabsSettings.Items.Add(new TabItem
        {
            Header = "Pravidla validací",
            Content = new SapValidationRulesEditorView()
        });

        TabsSettings.Items.Add(new TabItem
        {
            Header = "Cesty",
            Content = CreatePathsTab()
        });
    }

    private UIElement CreatePathsTab()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(14)
        };

        var text = new TextBlock
        {
            Text =
                $"SAP-DMS base path: {_basePath}\n" +
                $"Config path: {_configPath}\n\n" +
                "Později sem můžeme přesunout editaci cest, kontrolu dostupnosti a importní mapování.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14
        };

        text.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        panel.Children.Add(text);

        return panel;
    }

    private string GetConfigPath(string fileName)
    {
        Directory.CreateDirectory(_configPath);

        return Path.Combine(
            _configPath,
            fileName);
    }
}