using DMS.Core.Sap;
using DMS.Desktop.Logging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DMS.Desktop.Views.Sap;

public partial class SapMaterialUsageView : UserControl
{
    private readonly SapMaterialUsageOverviewService _service;
    private readonly SapMaterialStatusRuleService? _statusRuleService;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    public event Action<string>? TransactionRequested;

    // Designer / zpětná kompatibilita
    public SapMaterialUsageView(string materialNumber)
        : this(materialNumber,
               new SapStoragePaths(System.IO.Path.Combine(AppContext.BaseDirectory, "..")))
    {
    }

    public SapMaterialUsageView(
        string materialNumber,
        SapStoragePaths storagePaths,
        SapMaterialStatusRuleService? statusRuleService = null,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _statusRuleService = statusRuleService;
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName) ? "UNKNOWN" : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        var materials = new JsonSapMaterialRepository(storagePaths.SapMaterialsFilePath).LoadAll();
        var boms = new JsonSapBomRepository(storagePaths.SapBomSnapshotsFilePath).LoadAll();

        _service = new SapMaterialUsageOverviewService(materials, boms);

        _logger?.Info(
            $"MAT03: initialized; Materials={materials.Count}; Boms={boms.Count}; " +
            $"MaterialNumber={materialNumber}; User={_currentUserName}");

        Render(materialNumber);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    private void Render(string materialNumber)
    {
        RootPanel.Children.Clear();

        var overview = _service.BuildOverview(materialNumber);

        _logger?.Info(
            $"MAT03: rendered; MaterialNumber={materialNumber}; " +
            $"UsedAsComponent={overview.UsedAsComponent.Count}; " +
            $"OwnBoms={overview.OwnBomVariants.Count}; User={_currentUserName}");

        var formattedStatus = _statusRuleService is not null
            ? _statusRuleService.FormatStatus(overview.MaterialStatus)
            : overview.MaterialStatus;

        RootPanel.Children.Add(CreateTitle(TF("MAT03.Title", overview.MaterialNumber)));

        RootPanel.Children.Add(CreateInfoCard(
            T("MAT03.Section.Material"),
            $"{T("MAT03.Field.SapNumber")}: {overview.MaterialNumber}\n" +
            $"{T("MAT03.Field.Description")}: {NullDash(overview.Description)}\n" +
            $"{T("MAT03.Field.OldNumber")}: {NullDash(overview.OldMaterialNumber)}\n" +
            $"{T("MAT03.Field.Kind")}: {NullDash(overview.MaterialKind)}\n" +
            $"{T("MAT03.Field.Status")}: {NullDash(formattedStatus)}"));

        RootPanel.Children.Add(CreateActionBar(overview));

        if (overview.Messages.Count > 0)
            RootPanel.Children.Add(CreateInfoCard(
                T("MAT03.Section.Messages"),
                string.Join("\n", overview.Messages)));

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
            panel.Children.Add(CreateAccentButton(
                T("MAT03.Action.OpenTec03"),
                () => TransactionRequested?.Invoke($"TEC03 {overview.MaterialNumber}")));

        if (string.Equals(overview.MaterialKind, nameof(SapMaterialKind.Recipe), StringComparison.OrdinalIgnoreCase))
            panel.Children.Add(CreateAccentButton(
                T("MAT03.Action.OpenRec03"),
                () => TransactionRequested?.Invoke($"REC03 {overview.MaterialNumber}")));

        panel.Children.Add(CreateAccentButton(
            T("MAT03.Action.Refresh"),
            () => Render(overview.MaterialNumber)));

        return panel;
    }

    // ── Sekce ─────────────────────────────────────────────────────────────────

    private void RenderUsedAsComponent(SapMaterialUsageOverview overview)
    {
        RootPanel.Children.Add(CreateSectionTitle(
            TF("MAT03.Section.UsedAsComponent", overview.UsedAsComponent.Count)));

        if (overview.UsedAsComponent.Count == 0)
        {
            RootPanel.Children.Add(CreateMutedText(T("MAT03.NotUsedAsComponent")));
            return;
        }

        var grid = CreateBaseGrid();

        grid.PreviewMouseDoubleClick += (_, e) =>
        {
            var rowEl = FindParent<DataGridRow>((DependencyObject)e.OriginalSource);
            if (rowEl?.Item is not SapMaterialUsedAsComponentRow row) return;
            e.Handled = true;
            OpenParentMaterial(row);
        };

        grid.Columns.Add(CreateCol(T("MAT03.Col.ParentNumber"), "ParentMaterialNumber", 130));
        grid.Columns.Add(CreateCol(T("MAT03.Col.ParentDesc"), "ParentDescription", 320));
        grid.Columns.Add(CreateCol(T("MAT03.Col.ParentKind"), "ParentMaterialKind", 150));
        grid.Columns.Add(CreateCol(T("MAT03.Col.Plant"), "Plant", 70));
        grid.Columns.Add(CreateCol(T("MAT03.Col.BomNumber"), "BomNumber", 110));
        grid.Columns.Add(CreateCol(T("MAT03.Col.Alt"), "Alternative", 60));
        grid.Columns.Add(CreateCol(T("MAT03.Col.Position"), "Position", 70));
        grid.Columns.Add(CreateCol(T("MAT03.Col.ItemCat"), "ItemCategory", 55));
        grid.Columns.Add(CreateCol(T("MAT03.Col.Quantity"), "Quantity", 90));
        grid.Columns.Add(CreateCol(T("MAT03.Col.Unit"), "Unit", 60));

        grid.ItemsSource = overview.UsedAsComponent;

        RootPanel.Children.Add(CreateSmallHint(T("MAT03.UsageGrid.Hint")));
        RootPanel.Children.Add(grid);
    }

    private void OpenParentMaterial(SapMaterialUsedAsComponentRow row)
    {
        if (string.IsNullOrWhiteSpace(row.ParentMaterialNumber)) return;

        var tx = string.Equals(row.ParentMaterialKind, nameof(SapMaterialKind.GlassArticle), StringComparison.OrdinalIgnoreCase) ? "TEC03"
               : string.Equals(row.ParentMaterialKind, nameof(SapMaterialKind.Recipe), StringComparison.OrdinalIgnoreCase) ? "REC03"
               : "MAT03";

        TransactionRequested?.Invoke($"{tx} {row.ParentMaterialNumber}");
    }

    private void RenderOwnBoms(SapMaterialUsageOverview overview)
    {
        RootPanel.Children.Add(CreateSectionTitle(
            TF("MAT03.Section.OwnBoms", overview.OwnBomVariants.Count)));

        if (overview.OwnBomVariants.Count == 0)
        {
            RootPanel.Children.Add(CreateMutedText(T("MAT03.NoOwnBoms")));
            return;
        }

        foreach (var bom in overview.OwnBomVariants)
        {
            var expander = new Expander
            {
                Header = TF("MAT03.BomVariant.Header",
                    bom.Plant, bom.BomNumber, bom.Alternative, bom.Items.Count),
                IsExpanded = true,
                Margin = new Thickness(0, 0, 0, 12)
            };
            expander.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");

            var panel = new StackPanel();
            panel.Children.Add(CreateInfoCard(
                T("MAT03.BomVariant.HeaderTitle"),
                $"{T("MAT03.BomField.Number")}: {bom.BomNumber}\n" +
                $"{T("MAT03.BomField.Alternative")}: {bom.Alternative}\n" +
                $"{T("MAT03.BomField.Usage")}: {bom.BomUsage}\n" +
                $"{T("MAT03.BomField.BaseQty")}: {FormatDecimal(bom.BaseQuantity)} {bom.BaseUnit}"));

            var grid = CreateBaseGrid();
            grid.PreviewMouseDoubleClick += (_, e) =>
            {
                var rowEl = FindParent<DataGridRow>((DependencyObject)e.OriginalSource);
                if (rowEl?.Item is not SapMaterialOwnBomItemRow row) return;
                e.Handled = true;
                TransactionRequested?.Invoke($"MAT03 {row.ComponentNumber}");
            };

            grid.Columns.Add(CreateCol(T("MAT03.Col.Position"), "Position", 70));
            grid.Columns.Add(CreateCol(T("MAT03.Col.ItemCat"), "ItemCategory", 55));
            grid.Columns.Add(CreateCol(T("MAT03.Col.Component"), "ComponentNumber", 130));
            grid.Columns.Add(CreateCol(T("MAT03.Col.CompDesc"), "ComponentDescription", 320));
            grid.Columns.Add(CreateCol(T("MAT03.Col.CompKind"), "ComponentKind", 150));
            grid.Columns.Add(CreateCol(T("MAT03.Col.Quantity"), "Quantity", 90));
            grid.Columns.Add(CreateCol(T("MAT03.Col.Unit"), "Unit", 60));
            grid.Columns.Add(CreateCol(T("MAT03.Col.FixedQty"), "IsFixedQuantity", 70));
            grid.Columns.Add(CreateCol(T("MAT03.Col.Scrap"), "ScrapPercent", 90));

            grid.ItemsSource = bom.Items;

            panel.Children.Add(CreateSmallHint(T("MAT03.OwnBomGrid.Hint")));
            panel.Children.Add(grid);
            expander.Content = panel;
            RootPanel.Children.Add(expander);
        }
    }

    // ── UI stavební bloky ─────────────────────────────────────────────────────

    private UIElement CreateTitle(string text)
    {
        var b = new TextBlock { Text = text, FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 14) };
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

    /// <summary>
    /// Akcentní tlačítko — používá DmsAccentButtonStyle, který správně
    /// nastavuje DmsOnAccentBrush jako foreground ve všech stavech (incl. hover).
    /// </summary>
    private UIElement CreateAccentButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)Application.Current.FindResource("DmsAccentButtonStyle")
        };
        button.Click += (_, _) => action();
        return button;
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

        // ŽÁDNÝ CellStyle zde — řeší globální styl v Theme.xaml
        ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
        ScrollViewer.SetCanContentScroll(grid, true);
        return grid;
    }

    private static DataGridTextColumn CreateCol(string header, string binding, double width)
        => new() { Header = header, Binding = new Binding(binding), Width = new DataGridLength(width) };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatDecimal(decimal? value) => value?.ToString("0.###") ?? string.Empty;
    private static string NullDash(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

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