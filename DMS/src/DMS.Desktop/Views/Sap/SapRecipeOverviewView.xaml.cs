using DMS.Core.Sap;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DMS.Desktop.Views.Sap;

public partial class SapRecipeOverviewView : UserControl
{
    private readonly SapRecipeOverviewService _service;
    public event Action<string>? TransactionRequested;

    public SapRecipeOverviewView()
    : this(string.Empty)
{
    }

    public SapRecipeOverviewView(string initialRecipeFilter)
    {
        InitializeComponent();

        var basePath = @"Z:\SAP\DMS-db\DEV";

        var storagePaths = new SapStoragePaths(basePath);
        storagePaths.EnsureDirectories();

        var materialsPath = storagePaths.SapMaterialsFilePath;
        var bomsPath = Path.Combine(basePath, "Data", "sap-boms.json");

        var materials = new JsonSapMaterialRepository(materialsPath).LoadAll();
        var boms = new JsonSapBomRepository(bomsPath).LoadAll();

        _service = new SapRecipeOverviewService(materials, boms);

        if (!string.IsNullOrWhiteSpace(initialRecipeFilter))
        {
            TxtRecipeFilter.Text = NormalizeRecipeInput(initialRecipeFilter);
        }

        Search();
    }

    private void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        Search();
    }

    private void DgvRecipes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgvRecipes.SelectedItem is not SapRecipeSearchRow row)
        {
            return;
        }

        RenderRecipeDetail(row.RecipeNumber);
    }

    private void Search()
    {
        var rows = _service.SearchRecipes(
            TxtRecipeFilter.Text,
            TxtArticleFilter.Text);

        DgvRecipes.ItemsSource = rows;

        DetailPanel.Children.Clear();

        if (rows.Count > 0)
        {
            DgvRecipes.SelectedIndex = 0;
        }
        else
        {
            DetailPanel.Children.Add(CreateMutedText("Nebyla nalezena žádná receptura."));
        }
    }

    private void RenderRecipeDetail(string recipeNumber)
    {
        DetailPanel.Children.Clear();

        var overview = _service.BuildOverview(recipeNumber);

        DetailPanel.Children.Add(CreateTitle(
            $"Receptura {overview.RecipeNumber}"));

        DetailPanel.Children.Add(CreateInfoCard(
            "Základní informace",
            $"Číslo: {overview.RecipeNumber}\n" +
            $"Popis: {overview.RecipeDescription}\n" +
            $"Počet variant kusovníku: {overview.BomVariants.Count}\n" +
            $"Použití v artiklech: {overview.UsedInArticles.Count}\n" +
            $"Použití komponent v jiných recepturách: {overview.ComponentUsageInOtherRecipes.Count}"));

        if (overview.Messages.Count > 0)
        {
            DetailPanel.Children.Add(CreateInfoCard(
                "Hlášky",
                string.Join("\n", overview.Messages)));
        }

        RenderRecipeBomVariants(overview);
        RenderRecipeUsage(overview);
        RenderComponentUsage(overview);
    }

    private void RenderRecipeBomVariants(SapRecipeOverview overview)
    {
        DetailPanel.Children.Add(CreateSectionTitle("Kusovník receptury"));

        if (overview.BomVariants.Count == 0)
        {
            DetailPanel.Children.Add(CreateMutedText("Pro recepturu nebyl nalezen žádný kusovník."));
            return;
        }

        foreach (var variant in overview.BomVariants)
        {
            var expander = new Expander
            {
                Header = $"Závod {variant.Plant} / kusovník {variant.BomNumber} / alternativa {variant.Alternative} / položek: {variant.Items.Count}",
                IsExpanded = true,
                Margin = new Thickness(0, 0, 0, 12)
            };

            expander.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");
            expander.SetResourceReference(Control.BackgroundProperty, "DmsBackgroundBrush");

            var panel = new StackPanel();

            panel.Children.Add(CreateInfoCard(
                "Hlavička kusovníku",
                $"Kusovník: {variant.BomNumber}\n" +
                $"Alternativa: {variant.Alternative}\n" +
                $"Použití: {variant.BomUsage}\n" +
                $"Základní množství: {FormatDecimal(variant.BaseQuantity)} {variant.BaseUnit}"));

            panel.Children.Add(CreateRecipeBomGrid(variant.Items));

            expander.Content = panel;
            DetailPanel.Children.Add(expander);
        }
    }

    private void RenderRecipeUsage(SapRecipeOverview overview)
    {
        DetailPanel.Children.Add(CreateSectionTitle($"Kde se receptura používá ({overview.UsedInArticles.Count}) - dvojklik otevře TEC03"));

        if (overview.UsedInArticles.Count == 0)
        {
            DetailPanel.Children.Add(CreateMutedText("Receptura nebyla nalezena jako komponenta v artiklových kusovnících."));
            return;
        }

        DetailPanel.Children.Add(CreateRecipeUsageGrid(overview.UsedInArticles));
    }

    private void RenderComponentUsage(SapRecipeOverview overview)
    {
        DetailPanel.Children.Add(CreateSectionTitle(
    $"Kde se používají komponenty této receptury v jiných recepturách ({overview.ComponentUsageInOtherRecipes.Count}) - dvojklik otevře REC03"));

        if (overview.ComponentUsageInOtherRecipes.Count == 0)
        {
            DetailPanel.Children.Add(CreateMutedText("Komponenty receptury nebyly nalezeny v jiných recepturách."));
            return;
        }

        DetailPanel.Children.Add(CreateComponentUsageGrid(overview.ComponentUsageInOtherRecipes));
    }

    private DataGrid CreateRecipeBomGrid(IReadOnlyList<SapRecipeBomItemRow> rows)
    {
        var grid = CreateBaseGrid();

        grid.Columns.Add(CreateTextColumn("Pol.", "Position", 70));
        grid.Columns.Add(CreateTextColumn("Typ", "ItemCategory", 55));
        grid.Columns.Add(CreateTextColumn("Komponenta", "ComponentNumber", 120));
        grid.Columns.Add(CreateTextColumn("Popis komponenty", "ComponentDescription", 300));
        grid.Columns.Add(CreateTextColumn("Druh", "ComponentKind", 150));
        grid.Columns.Add(CreateTextColumn("Množství", "Quantity", 90));
        grid.Columns.Add(CreateTextColumn("MJ", "Unit", 60));
        grid.Columns.Add(CreateTextColumn("Pevné", "IsFixedQuantity", 70));
        grid.Columns.Add(CreateTextColumn("Odpad %", "ScrapPercent", 90));

        grid.ItemsSource = rows;

        return grid;
    }

    private DataGrid CreateRecipeUsageGrid(IReadOnlyList<SapRecipeUsageRow> rows)
    {
        var grid = CreateBaseGrid();

        grid.PreviewMouseDoubleClick += (sender, e) =>
        {
            if (e.OriginalSource is not DependencyObject source)
            {
                return;
            }

            var rowElement = FindParent<DataGridRow>(source);

            if (rowElement?.Item is not SapRecipeUsageRow row)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(row.ArticleNumber))
            {
                return;
            }

            e.Handled = true;

            TransactionRequested?.Invoke($"TEC03 {row.ArticleNumber}");
        };

        grid.Columns.Add(CreateTextColumn("Artikl", "ArticleNumber", 120));
        grid.Columns.Add(CreateTextColumn("Popis artiklu", "ArticleDescription", 320));
        grid.Columns.Add(CreateTextColumn("Závod", "Plant", 70));
        grid.Columns.Add(CreateTextColumn("Kusovník", "BomNumber", 110));
        grid.Columns.Add(CreateTextColumn("Alt.", "Alternative", 60));
        grid.Columns.Add(CreateTextColumn("Pol.", "Position", 70));
        grid.Columns.Add(CreateTextColumn("Množství", "Quantity", 90));
        grid.Columns.Add(CreateTextColumn("MJ", "Unit", 60));

        grid.ItemsSource = rows;

        return grid;
    }

    private DataGrid CreateComponentUsageGrid(IReadOnlyList<SapRecipeComponentUsageRow> rows)
    {
        var grid = CreateBaseGrid();

        grid.PreviewMouseDoubleClick += (sender, e) =>
        {
            if (e.OriginalSource is not DependencyObject source)
            {
                return;
            }

            var rowElement = FindParent<DataGridRow>(source);

            if (rowElement?.Item is not SapRecipeComponentUsageRow row)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(row.RecipeNumber))
            {
                return;
            }

            e.Handled = true;

            TransactionRequested?.Invoke($"REC03 {row.RecipeNumber}");
        };

        grid.Columns.Add(CreateTextColumn("Komponenta", "ComponentNumber", 120));
        grid.Columns.Add(CreateTextColumn("Popis komponenty", "ComponentDescription", 260));
        grid.Columns.Add(CreateTextColumn("Jiná receptura", "RecipeNumber", 120));
        grid.Columns.Add(CreateTextColumn("Popis receptury", "RecipeDescription", 260));
        grid.Columns.Add(CreateTextColumn("Závod", "Plant", 70));
        grid.Columns.Add(CreateTextColumn("Kusovník", "BomNumber", 110));
        grid.Columns.Add(CreateTextColumn("Alt.", "Alternative", 60));
        grid.Columns.Add(CreateTextColumn("Pol.", "Position", 70));
        grid.Columns.Add(CreateTextColumn("Množství", "Quantity", 90));
        grid.Columns.Add(CreateTextColumn("MJ", "Unit", 60));

        grid.ItemsSource = rows;

        return grid;
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
            MaxHeight = 320,
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
        ScrollViewer.SetCanContentScroll(grid, true);

        return grid;
    }

    private static DataGridTextColumn CreateTextColumn(
        string header,
        string binding,
        double width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(binding),
            Width = new DataGridLength(width)
        };
    }

    private UIElement CreateTitle(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
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

    private static string FormatDecimal(decimal? value)
    {
        return value?.ToString("0.###") ?? string.Empty;
    }

    private static string NormalizeRecipeInput(string value)
    {
        value = value.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.All(char.IsDigit)
            ? value.PadLeft(10, '0')
            : value;
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
}