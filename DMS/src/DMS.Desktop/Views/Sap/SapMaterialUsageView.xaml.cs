using DMS.Core.Sap;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace DMS.Desktop.Views.Sap;

public partial class SapMaterialUsageView : UserControl
{
    private readonly SapMaterialUsageOverviewService _service;

    public event Action<string>? TransactionRequested;

    public SapMaterialUsageView(string materialNumber)
    {
        InitializeComponent();

        var basePath = @"Z:\SAP\DMS-db\DEV";

        var storagePaths = new SapStoragePaths(basePath);
        storagePaths.EnsureDirectories();

        var materials = new JsonSapMaterialRepository(storagePaths.SapMaterialsFilePath)
            .LoadAll();

        var bomsPath = Path.Combine(basePath, "Data", "sap-boms.json");

        var boms = new JsonSapBomRepository(bomsPath)
            .LoadAll();

        _service = new SapMaterialUsageOverviewService(materials, boms);

        Render(materialNumber);
    }

    private void Render(string materialNumber)
    {
        RootPanel.Children.Clear();

        var overview = _service.BuildOverview(materialNumber);

        RootPanel.Children.Add(CreateTitle($"MAT03 - Použití materiálu {overview.MaterialNumber}"));

        RootPanel.Children.Add(CreateInfoCard(
            "Materiál",
            $"SAP číslo: {overview.MaterialNumber}\n" +
            $"Popis: {NullDash(overview.Description)}\n" +
            $"Staré číslo: {NullDash(overview.OldMaterialNumber)}\n" +
            $"Typ v DMS: {NullDash(overview.MaterialKind)}\n" +
            $"Status: {NullDash(overview.MaterialStatus)}"));

        RootPanel.Children.Add(CreateActionBar(overview));

        if (overview.Messages.Count > 0)
        {
            RootPanel.Children.Add(CreateInfoCard(
                "Hlášky",
                string.Join("\n", overview.Messages)));
        }

        RenderUsedAsComponent(overview);
        RenderOwnBoms(overview);
    }

    private UIElement CreateActionBar(SapMaterialUsageOverview overview)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14)
        };

        if (string.Equals(overview.MaterialKind, nameof(SapMaterialKind.GlassArticle), StringComparison.OrdinalIgnoreCase))
        {
            panel.Children.Add(CreateActionButton(
                "Otevřít TEC03",
                () => TransactionRequested?.Invoke($"TEC03 {overview.MaterialNumber}")));
        }

        if (string.Equals(overview.MaterialKind, nameof(SapMaterialKind.Recipe), StringComparison.OrdinalIgnoreCase))
        {
            panel.Children.Add(CreateActionButton(
                "Otevřít REC03",
                () => TransactionRequested?.Invoke($"REC03 {overview.MaterialNumber}")));
        }

        panel.Children.Add(CreateActionButton(
            "Aktualizovat MAT03",
            () => Render(overview.MaterialNumber)));

        return panel;
    }

    private void RenderUsedAsComponent(SapMaterialUsageOverview overview)
    {
        RootPanel.Children.Add(CreateSectionTitle(
            $"Kde se materiál používá jako komponenta ({overview.UsedAsComponent.Count})"));

        if (overview.UsedAsComponent.Count == 0)
        {
            RootPanel.Children.Add(CreateMutedText("Materiál nebyl nalezen v žádném importovaném kusovníku."));
            return;
        }

        var grid = CreateBaseGrid();

        grid.PreviewMouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is not DependencyObject source)
            {
                return;
            }

            var rowElement = FindParent<DataGridRow>(source);

            if (rowElement?.Item is not SapMaterialUsedAsComponentRow row)
            {
                return;
            }

            e.Handled = true;

            OpenParentMaterial(row);
        };

        grid.Columns.Add(CreateTextColumn("Nadřazený materiál", "ParentMaterialNumber", 130));
        grid.Columns.Add(CreateTextColumn("Popis", "ParentDescription", 320));
        grid.Columns.Add(CreateTextColumn("Typ", "ParentMaterialKind", 150));
        grid.Columns.Add(CreateTextColumn("Závod", "Plant", 70));
        grid.Columns.Add(CreateTextColumn("Kusovník", "BomNumber", 110));
        grid.Columns.Add(CreateTextColumn("Alt.", "Alternative", 60));
        grid.Columns.Add(CreateTextColumn("Pol.", "Position", 70));
        grid.Columns.Add(CreateTextColumn("TpP", "ItemCategory", 55));
        grid.Columns.Add(CreateTextColumn("Množství", "Quantity", 90));
        grid.Columns.Add(CreateTextColumn("MJ", "Unit", 60));

        grid.ItemsSource = overview.UsedAsComponent;

        RootPanel.Children.Add(CreateSmallHint("Dvojklik na řádek otevře nadřazený materiál v odpovídající transakci."));
        RootPanel.Children.Add(grid);
    }

    private void OpenParentMaterial(SapMaterialUsedAsComponentRow row)
    {
        if (string.IsNullOrWhiteSpace(row.ParentMaterialNumber))
        {
            return;
        }

        if (string.Equals(row.ParentMaterialKind, nameof(SapMaterialKind.GlassArticle), StringComparison.OrdinalIgnoreCase))
        {
            TransactionRequested?.Invoke($"TEC03 {row.ParentMaterialNumber}");
            return;
        }

        if (string.Equals(row.ParentMaterialKind, nameof(SapMaterialKind.Recipe), StringComparison.OrdinalIgnoreCase))
        {
            TransactionRequested?.Invoke($"REC03 {row.ParentMaterialNumber}");
            return;
        }

        TransactionRequested?.Invoke($"MAT03 {row.ParentMaterialNumber}");
    }

    private void RenderOwnBoms(SapMaterialUsageOverview overview)
    {
        RootPanel.Children.Add(CreateSectionTitle(
            $"Vlastní kusovníky materiálu ({overview.OwnBomVariants.Count})"));

        if (overview.OwnBomVariants.Count == 0)
        {
            RootPanel.Children.Add(CreateMutedText("Materiál nemá v importované cache vlastní kusovník."));
            return;
        }

        foreach (var bom in overview.OwnBomVariants)
        {
            var expander = new Expander
            {
                Header = $"Závod {bom.Plant} / kusovník {bom.BomNumber} / alternativa {bom.Alternative} / položek: {bom.Items.Count}",
                IsExpanded = true,
                Margin = new Thickness(0, 0, 0, 12)
            };

            expander.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");

            var panel = new StackPanel();

            panel.Children.Add(CreateInfoCard(
                "Hlavička kusovníku",
                $"Kusovník: {bom.BomNumber}\n" +
                $"Alternativa: {bom.Alternative}\n" +
                $"Použití: {bom.BomUsage}\n" +
                $"Základní množství: {FormatDecimal(bom.BaseQuantity)} {bom.BaseUnit}"));

            var grid = CreateBaseGrid();

            grid.PreviewMouseDoubleClick += (_, e) =>
            {
                if (e.OriginalSource is not DependencyObject source)
                {
                    return;
                }

                var rowElement = FindParent<DataGridRow>(source);

                if (rowElement?.Item is not SapMaterialOwnBomItemRow row)
                {
                    return;
                }

                e.Handled = true;

                TransactionRequested?.Invoke($"MAT03 {row.ComponentNumber}");
            };

            grid.Columns.Add(CreateTextColumn("Pol.", "Position", 70));
            grid.Columns.Add(CreateTextColumn("TpP", "ItemCategory", 55));
            grid.Columns.Add(CreateTextColumn("Komponenta", "ComponentNumber", 130));
            grid.Columns.Add(CreateTextColumn("Popis", "ComponentDescription", 320));
            grid.Columns.Add(CreateTextColumn("Typ", "ComponentKind", 150));
            grid.Columns.Add(CreateTextColumn("Množství", "Quantity", 90));
            grid.Columns.Add(CreateTextColumn("MJ", "Unit", 60));
            grid.Columns.Add(CreateTextColumn("Pevné", "IsFixedQuantity", 70));
            grid.Columns.Add(CreateTextColumn("Odpad %", "ScrapPercent", 90));

            grid.ItemsSource = bom.Items;

            panel.Children.Add(CreateSmallHint("Dvojklik na komponentu otevře MAT03 dané komponenty."));
            panel.Children.Add(grid);

            expander.Content = panel;

            RootPanel.Children.Add(expander);
        }
    }

    private DataGrid CreateBaseGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Margin = new Thickness(0, 0, 0, 16),
            MinHeight = 110,
            MaxHeight = 340,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
            ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader
        };

        grid.SetResourceReference(Control.BackgroundProperty, "DmsBackgroundBrush");
        grid.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");
        grid.SetResourceReference(Control.BorderBrushProperty, "DmsBorderBrush");

        ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Auto);

        return grid;
    }

    private static DataGridTextColumn CreateTextColumn(string header, string binding, double width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(binding),
            Width = new DataGridLength(width)
        };
    }

    private UIElement CreateActionButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };

        button.SetResourceReference(Control.BackgroundProperty, "DmsAccentBrush");
        button.SetResourceReference(Control.ForegroundProperty, "DmsOnAccentBrush");

        button.Click += (_, _) => action();

        return button;
    }

    private UIElement CreateTitle(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 14)
        };

        block.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        return block;
    }

    private UIElement CreateSectionTitle(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 18, 0, 10)
        };

        block.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        return block;
    }

    private UIElement CreateMutedText(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };

        block.SetResourceReference(TextBlock.ForegroundProperty, "DmsMutedForegroundBrush");

        return block;
    }

    private UIElement CreateSmallHint(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };

        block.SetResourceReference(TextBlock.ForegroundProperty, "DmsMutedForegroundBrush");

        return block;
    }

    private UIElement CreateInfoCard(string title, string body)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            BorderThickness = new Thickness(1)
        };

        border.SetResourceReference(Border.BackgroundProperty, "DmsPanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

        var panel = new StackPanel();

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        };

        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        var bodyBlock = new TextBox
        {
            Text = body,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        bodyBlock.SetResourceReference(TextBox.BackgroundProperty, "DmsPanelBrush");
        bodyBlock.SetResourceReference(TextBox.ForegroundProperty, "DmsMutedForegroundBrush");
        bodyBlock.SetResourceReference(TextBox.CaretBrushProperty, "DmsForegroundBrush");

        panel.Children.Add(titleBlock);
        panel.Children.Add(bodyBlock);

        border.Child = panel;

        return border;
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

    private static string FormatDecimal(decimal? value)
    {
        return value?.ToString("0.###") ?? string.Empty;
    }

    private static string NullDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "-"
            : value;
    }
}