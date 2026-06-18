using DMS.Desktop.Views.Sap;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderSapCockpit()
    {
        WorkspacePanel.Children.Clear();

        var root = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(0)
        };

        var header = CreateSapCockpitHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var tabs = new TabControl
        {
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        tabs.SetResourceReference(
            Control.BackgroundProperty,
            "DmsPanelBrush");

        tabs.SetResourceReference(
            Control.ForegroundProperty,
            "DmsForegroundBrush");

        tabs.Items.Add(new TabItem
        {
            Header = "Materiály",
            Content = CreateMaterialImportTab()
        });

        tabs.Items.Add(new TabItem
        {
            Header = "Kusovníky",
            Content = new SapBomImportView()
        });

        tabs.Items.Add(new TabItem
        {
            Header = "Pracovní postupy",
            Content = new SapRoutingImportView()
        });

        tabs.Items.Add(new TabItem
        {
            Header = "Pracoviště",
            Content = new SapWorkCenterImportView()
        });

        tabs.Items.Add(new TabItem
        {
            Header = "Importní dávky",
            Content = new SapCacheStatusView()
        });

        root.Children.Add(tabs);
        WorkspacePanel.Children.Add(root);

        ResetWorkspaceScroll();
    }

    private UIElement CreateSapCockpitHeader()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        panel.Children.Add(CreateTitle("SAP import cockpit"));

        var subtitle = new TextBlock
        {
            Text = "Importní centrum pro SAP materiály, kusovníky, pracovní postupy a pracoviště.",
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };

        subtitle.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsMutedForegroundBrush");

        panel.Children.Add(subtitle);

        var info = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 4, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(1)
        };

        info.SetResourceReference(
            Border.BackgroundProperty,
            "DmsBackgroundBrush");

        info.SetResourceReference(
            Border.BorderBrushProperty,
            "DmsBorderBrush");

        var infoText = new TextBlock
        {
            Text =
                "SAP00 slouží jako provozní importní cockpit. " +
                "Customizace SAP-DMS pravidel byla přesunuta do transakce SAPSET.",
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        };

        infoText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        info.Child = infoText;

        panel.Children.Add(info);

        return panel;
    }

    private UIElement CreateMaterialImportTab()
    {
        try
        {
            return new SapImportView();
        }
        catch (Exception ex)
        {
            return CreateSapPlaceholderTab(
                "Import materiálů",
                $"Nepodařilo se otevřít SapImportView.\n\n{ex.Message}");
        }
    }

    private UIElement CreateSapPlaceholderTab(
        string title,
        string description)
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(16)
        };

        panel.Children.Add(CreateSectionTitle(title));

        var text = new TextBlock
        {
            Text = description,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };

        text.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        panel.Children.Add(text);

        panel.Children.Add(CreateSapCockpitCard(
            "Stav",
            "Zatím připravený placeholder. Další krok bude napojení importních služeb a zobrazení dat v tabulce."));

        scrollViewer.Content = panel;

        return scrollViewer;
    }

    private UIElement CreateSapCockpitCard(
        string title,
        string body)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 8, 0, 8),
            BorderThickness = new Thickness(1)
        };

        border.SetResourceReference(
            Border.BackgroundProperty,
            "DmsPanelBrush");

        border.SetResourceReference(
            Border.BorderBrushProperty,
            "DmsBorderBrush");

        var panel = new StackPanel();

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        };

        titleBlock.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        panel.Children.Add(titleBlock);

        var bodyBlock = new TextBlock
        {
            Text = body,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        };

        bodyBlock.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsMutedForegroundBrush");

        panel.Children.Add(bodyBlock);

        border.Child = panel;

        return border;
    }
}