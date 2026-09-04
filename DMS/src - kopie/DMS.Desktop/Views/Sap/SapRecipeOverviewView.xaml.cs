using DMS.Core.Sap;
using DMS.Desktop.Logging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DMS.Desktop.Views.Sap;

public partial class SapRecipeOverviewView : UserControl
{
    private readonly SapRecipeOverviewService _service;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    public event Action<string>? TransactionRequested;

    // Designer / zpětná kompatibilita
    public SapRecipeOverviewView()
        : this(string.Empty)
    {
    }

    public SapRecipeOverviewView(string initialRecipeFilter)
        : this(
            initialRecipeFilter,
            new SapStoragePaths(
                System.IO.Path.Combine(AppContext.BaseDirectory, "..")))
    {
    }

    public SapRecipeOverviewView(
        string initialRecipeFilter,
        SapStoragePaths storagePaths,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName) ? "UNKNOWN" : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        var materials = new JsonSapMaterialRepository(storagePaths.SapMaterialsFilePath).LoadAll();
        var boms = new JsonSapBomRepository(storagePaths.SapBomSnapshotsFilePath).LoadAll();

        _service = new SapRecipeOverviewService(materials, boms);

        _logger?.Info(
            $"REC03: initialized; Materials={materials.Count}; Boms={boms.Count}; " +
            $"User={_currentUserName}");

        ApplyLocalization();

        if (!string.IsNullOrWhiteSpace(initialRecipeFilter))
            TxtRecipeFilter.Text = NormalizeRecipeInput(initialRecipeFilter);

        Search();
    }

    // ── Lokalizace ────────────────────────────────────────────────────────────

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("REC03.Title");
        TxtSubtitle.Text = T("REC03.Subtitle");
        LblRecipeFilter.Text = T("REC03.Filter.Recipe");
        LblArticleFilter.Text = T("REC03.Filter.Article");
        BtnSearch.Content = T("REC03.Filter.Search");
        TxtResultsTitle.Text = T("REC03.Results.Title");
        ColRecipeNumber.Header = T("REC03.Col.RecipeNumber");
        ColDescription.Header = T("REC03.Col.Description");
        ColUsage.Header = T("REC03.Col.Usage");
        ColItems.Header = T("REC03.Col.Items");
    }

    // ── Hledání ───────────────────────────────────────────────────────────────

    private void BtnSearch_Click(object sender, RoutedEventArgs e) => Search();

    private void DgvRecipes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgvRecipes.SelectedItem is not SapRecipeSearchRow row)
            return;

        RenderRecipeDetail(row.RecipeNumber);
    }

    private void Search()
    {
        var recipeFilter = TxtRecipeFilter.Text;
        var articleFilter = TxtArticleFilter.Text;

        var rows = _service.SearchRecipes(recipeFilter, articleFilter);

        DgvRecipes.ItemsSource = rows;
        DetailPanel.Children.Clear();

        _logger?.Info(
            $"REC03: search; RecipeFilter={recipeFilter}; ArticleFilter={articleFilter}; " +
            $"Results={rows.Count}; User={_currentUserName}");

        if (rows.Count > 0)
            DgvRecipes.SelectedIndex = 0;
        else
            DetailPanel.Children.Add(CreateMutedText(T("REC03.NoResults")));
    }

    // ── Detail receptury ──────────────────────────────────────────────────────

    private void RenderRecipeDetail(string recipeNumber)
    {
        DetailPanel.Children.Clear();

        var overview = _service.BuildOverview(recipeNumber);

        _logger?.Info(
            $"REC03: detail opened; RecipeNumber={recipeNumber}; " +
            $"BomVariants={overview.BomVariants.Count}; UsedInArticles={overview.UsedInArticles.Count}; " +
            $"User={_currentUserName}");

        DetailPanel.Children.Add(CreateTitle(TF("REC03.Detail.Title", overview.RecipeNumber)));

        DetailPanel.Children.Add(CreateInfoCard(
            T("REC03.Detail.BasicInfo"),
            $"{T("REC03.Field.Number")}: {overview.RecipeNumber}\n" +
            $"{T("REC03.Field.Description")}: {overview.RecipeDescription}\n" +
            $"{T("REC03.Field.BomVariants")}: {overview.BomVariants.Count}\n" +
            $"{T("REC03.Field.UsedInArticles")}: {overview.UsedInArticles.Count}\n" +
            $"{T("REC03.Field.ComponentCrossUsage")}: {overview.ComponentUsageInOtherRecipes.Count}"));

        if (overview.Messages.Count > 0)
            DetailPanel.Children.Add(CreateInfoCard(
                T("REC03.Detail.Messages"),
                string.Join("\n", overview.Messages)));

        RenderBomVariants(overview);
        RenderUsageInArticles(overview);
        RenderComponentCrossUsage(overview);
    }

    private void RenderBomVariants(SapRecipeOverview overview)
    {
        DetailPanel.Children.Add(CreateSectionTitle(T("REC03.Section.Bom")));

        if (overview.BomVariants.Count == 0)
        {
            DetailPanel.Children.Add(CreateMutedText(T("REC03.NoBom")));
            return;
        }

        foreach (var variant in overview.BomVariants)
        {
            var expander = new Expander
            {
                Header = TF("REC03.BomVariant.Header",
                    variant.Plant, variant.BomNumber,
                    variant.Alternative, variant.Items.Count),
                IsExpanded = true,
                Margin = new Thickness(0, 0, 0, 12)
            };
            expander.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");
            expander.SetResourceReference(Control.BackgroundProperty, "DmsBackgroundBrush");

            var panel = new StackPanel();
            panel.Children.Add(CreateInfoCard(
                T("REC03.BomVariant.HeaderTitle"),
                $"{T("REC03.BomField.Number")}: {variant.BomNumber}\n" +
                $"{T("REC03.BomField.Alternative")}: {variant.Alternative}\n" +
                $"{T("REC03.BomField.Usage")}: {variant.BomUsage}\n" +
                $"{T("REC03.BomField.BaseQty")}: {FormatDecimal(variant.BaseQuantity)} {variant.BaseUnit}"));

            panel.Children.Add(CreateBomGrid(variant.Items));
            expander.Content = panel;
            DetailPanel.Children.Add(expander);
        }
    }

    private void RenderUsageInArticles(SapRecipeOverview overview)
    {
        DetailPanel.Children.Add(CreateSectionTitle(
            TF("REC03.Section.UsedInArticles", overview.UsedInArticles.Count)));

        if (overview.UsedInArticles.Count == 0)
        {
            DetailPanel.Children.Add(CreateMutedText(T("REC03.NotUsedInArticles")));
            return;
        }

        DetailPanel.Children.Add(CreateSmallHint(T("REC03.UsageGrid.Hint")));
        DetailPanel.Children.Add(CreateUsageGrid(overview.UsedInArticles));
    }

    private void RenderComponentCrossUsage(SapRecipeOverview overview)
    {
        DetailPanel.Children.Add(CreateSectionTitle(
            TF("REC03.Section.ComponentCrossUsage", overview.ComponentUsageInOtherRecipes.Count)));

        if (overview.ComponentUsageInOtherRecipes.Count == 0)
        {
            DetailPanel.Children.Add(CreateMutedText(T("REC03.NoComponentCrossUsage")));
            return;
        }

        DetailPanel.Children.Add(CreateSmallHint(T("REC03.CrossUsageGrid.Hint")));
        DetailPanel.Children.Add(CreateComponentCrossUsageGrid(overview.ComponentUsageInOtherRecipes));
    }

    // ── Gridy ─────────────────────────────────────────────────────────────────

    private DataGrid CreateBomGrid(IReadOnlyList<SapRecipeBomItemRow> rows)
    {
        var grid = CreateBaseGrid();
        grid.Columns.Add(CreateCol(T("REC03.Col.Position"), "Position", 70));
        grid.Columns.Add(CreateCol(T("REC03.Col.ItemCat"), "ItemCategory", 55));
        grid.Columns.Add(CreateCol(T("REC03.Col.Component"), "ComponentNumber", 120));
        grid.Columns.Add(CreateCol(T("REC03.Col.CompDesc"), "ComponentDescription", 300));
        grid.Columns.Add(CreateCol(T("REC03.Col.CompKind"), "ComponentKind", 150));
        grid.Columns.Add(CreateCol(T("REC03.Col.Quantity"), "Quantity", 90));
        grid.Columns.Add(CreateCol(T("REC03.Col.Unit"), "Unit", 60));
        grid.Columns.Add(CreateCol(T("REC03.Col.FixedQty"), "IsFixedQuantity", 70));
        grid.Columns.Add(CreateCol(T("REC03.Col.Scrap"), "ScrapPercent", 90));
        grid.ItemsSource = rows;
        return grid;
    }

    private DataGrid CreateUsageGrid(IReadOnlyList<SapRecipeUsageRow> rows)
    {
        var grid = CreateBaseGrid();

        grid.PreviewMouseDoubleClick += (_, e) =>
        {
            var rowElement = FindParent<DataGridRow>((DependencyObject)e.OriginalSource);
            if (rowElement?.Item is not SapRecipeUsageRow row || string.IsNullOrWhiteSpace(row.ArticleNumber))
                return;
            e.Handled = true;
            TransactionRequested?.Invoke($"TEC03 {row.ArticleNumber}");
        };

        grid.Columns.Add(CreateCol(T("REC03.Col.Article"), "ArticleNumber", 120));
        grid.Columns.Add(CreateCol(T("REC03.Col.ArticleDesc"), "ArticleDescription", 320));
        grid.Columns.Add(CreateCol(T("REC03.Col.Plant"), "Plant", 70));
        grid.Columns.Add(CreateCol(T("REC03.Col.BomNumber"), "BomNumber", 110));
        grid.Columns.Add(CreateCol(T("REC03.Col.Alt"), "Alternative", 60));
        grid.Columns.Add(CreateCol(T("REC03.Col.Position"), "Position", 70));
        grid.Columns.Add(CreateCol(T("REC03.Col.Quantity"), "Quantity", 90));
        grid.Columns.Add(CreateCol(T("REC03.Col.Unit"), "Unit", 60));
        grid.ItemsSource = rows;
        return grid;
    }

    private DataGrid CreateComponentCrossUsageGrid(IReadOnlyList<SapRecipeComponentUsageRow> rows)
    {
        var grid = CreateBaseGrid();

        grid.PreviewMouseDoubleClick += (_, e) =>
        {
            var rowElement = FindParent<DataGridRow>((DependencyObject)e.OriginalSource);
            if (rowElement?.Item is not SapRecipeComponentUsageRow row || string.IsNullOrWhiteSpace(row.RecipeNumber))
                return;
            e.Handled = true;
            TransactionRequested?.Invoke($"REC03 {row.RecipeNumber}");
        };

        grid.Columns.Add(CreateCol(T("REC03.Col.Component"), "ComponentNumber", 120));
        grid.Columns.Add(CreateCol(T("REC03.Col.CompDesc"), "ComponentDescription", 260));
        grid.Columns.Add(CreateCol(T("REC03.Col.OtherRecipe"), "RecipeNumber", 120));
        grid.Columns.Add(CreateCol(T("REC03.Col.RecipeDesc"), "RecipeDescription", 260));
        grid.Columns.Add(CreateCol(T("REC03.Col.Plant"), "Plant", 70));
        grid.Columns.Add(CreateCol(T("REC03.Col.BomNumber"), "BomNumber", 110));
        grid.Columns.Add(CreateCol(T("REC03.Col.Alt"), "Alternative", 60));
        grid.Columns.Add(CreateCol(T("REC03.Col.Position"), "Position", 70));
        grid.Columns.Add(CreateCol(T("REC03.Col.Quantity"), "Quantity", 90));
        grid.Columns.Add(CreateCol(T("REC03.Col.Unit"), "Unit", 60));
        grid.ItemsSource = rows;
        return grid;
    }

    // ── UI stavební bloky ─────────────────────────────────────────────────────

    private UIElement CreateTitle(string text)
    {
        var b = new TextBlock { Text = text, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) };
        b.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");
        return b;
    }

    private UIElement CreateSectionTitle(string text)
    {
        var b = new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 18, 0, 10) };
        b.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");
        return b;
    }

    private UIElement CreateMutedText(string text)
    {
        var b = new TextBlock { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        b.SetResourceReference(TextBlock.ForegroundProperty, "DmsMutedForegroundBrush");
        return b;
    }

    private UIElement CreateSmallHint(string text)
    {
        var b = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        b.SetResourceReference(TextBlock.ForegroundProperty, "DmsMutedForegroundBrush");
        return b;
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

        var titleBlock = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        var bodyBox = new TextBox
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
        bodyBox.SetResourceReference(TextBox.BackgroundProperty, "DmsPanelBrush");
        bodyBox.SetResourceReference(TextBox.ForegroundProperty, "DmsMutedForegroundBrush");
        bodyBox.SetResourceReference(TextBox.CaretBrushProperty, "DmsForegroundBrush");

        panel.Children.Add(titleBlock);
        panel.Children.Add(bodyBox);
        border.Child = panel;
        return border;
    }

    private static DataGrid CreateBaseGrid()
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

    private static DataGridTextColumn CreateCol(string header, string binding, double width)
        => new() { Header = header, Binding = new Binding(binding), Width = new DataGridLength(width) };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatDecimal(decimal? value) => value?.ToString("0.###") ?? string.Empty;

    private static string NormalizeRecipeInput(string value)
    {
        value = value.Trim();
        return string.IsNullOrWhiteSpace(value) ? string.Empty
            : value.All(char.IsDigit) ? value.PadLeft(10, '0') : value;
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is T typed) return typed;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;
        return IsMissing(value, key) ? key : value;
    }

    private string TF(string key, params object[] args)
    {
        var pattern = T(key);
        try { return string.Format(pattern, args); }
        catch { return pattern; }
    }

    private static bool IsMissing(string? value, string key)
        => string.IsNullOrWhiteSpace(value)
           || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
}