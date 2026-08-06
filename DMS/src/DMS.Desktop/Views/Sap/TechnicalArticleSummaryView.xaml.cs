using DMS.Core.Sap;
using DMS.Core.Sap.Validation;
using DMS.Desktop.Logging;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DMS.Desktop.Views.Sap;

public partial class TechnicalArticleSummaryView : UserControl
{
    private readonly string _articleNumber;
    private readonly SapStoragePaths _storagePaths;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    public TechnicalArticleSummaryView(
        string articleNumber,
        SapStoragePaths storagePaths,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _articleNumber = NormalizeMaterialNumber(articleNumber);
        _storagePaths = storagePaths;
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName) ? "UNKNOWN" : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Render();

    // ── Render ────────────────────────────────────────────────────────────────

    private void Render()
    {
        RootPanel.Children.Clear();
        RootPanel.Children.Add(CreateTitle(TF("TEC03.Title", _articleNumber)));

        try
        {
            var materials = new JsonSapMaterialRepository(_storagePaths.SapMaterialsFilePath).LoadAll();
            var boms = new JsonSapBomRepository(_storagePaths.SapBomSnapshotsFilePath).LoadAll();
            var routings = new JsonSapRoutingRepository(_storagePaths.SapRoutingSnapshotsFilePath).LoadAll();
            var workCenters = new JsonSapWorkCenterRepository(_storagePaths.SapWorkCentersFilePath).LoadAll();

            var validationRulesPath = Path.Combine(_storagePaths.ConfigDirectory, "sap-validation-rules.json");
            var validationRules = new JsonSapValidationRuleRepository(validationRulesPath).Load();

            var service = new SapTechnicalArticleSummaryService(
                materials, boms, routings, workCenters, validationRules);

            var summary = service.Build(_articleNumber);

            _logger?.Info(
                $"TEC03: summary built; ArticleNumber={_articleNumber}; " +
                $"Boms9200={summary.Boms9200.Count}; Boms2000={summary.Boms2000.Count}; " +
                $"Routings9200={summary.Routings9200.Count}; Routings2000={summary.Routings2000.Count}; " +
                $"Errors={summary.CriticalErrors.Count}; Warnings={summary.Warnings.Count}; " +
                $"User={_currentUserName}");

            RootPanel.Children.Add(CreateStatusCard(summary));
            RootPanel.Children.Add(CreateMaterialCard(summary));
            RootPanel.Children.Add(CreateSectionTitle(T("TEC03.Section.Variants")));

            var variantsByPlant = summary.Variants
                .OrderBy(v => v.Plant)
                .ThenBy(v => v.Alternative)
                .GroupBy(v => v.Plant)
                .ToList();

            if (variantsByPlant.Count == 0)
                RootPanel.Children.Add(CreateMutedText(T("TEC03.NoVariants")));
            else
                foreach (var plantGroup in variantsByPlant)
                    RootPanel.Children.Add(CreatePlantPanel(service, plantGroup.Key, plantGroup.ToList()));

            RootPanel.Children.Add(CreateMessagesPanel(summary));
        }
        catch (Exception ex)
        {
            _logger?.Error(
                $"TEC03: render failed; ArticleNumber={_articleNumber}; User={_currentUserName}",
                ex);

            RootPanel.Children.Add(CreateCard(T("TEC03.LoadFailed"), ex.Message));
        }
    }

    // ── Cards ─────────────────────────────────────────────────────────────────

    private UIElement CreateStatusCard(SapTechnicalArticleSummary summary)
    {
        var statusLabel = summary.HasCriticalError
            ? T("TEC03.Status.Critical")
            : summary.HasWarning
                ? T("TEC03.Status.Warning")
                : T("TEC03.Status.Ready");

        var detail =
            $"{T("TEC03.Field.Article")}: {summary.ArticleNumber}\n" +
            $"{T("TEC03.Field.CriticalErrors")}: {summary.CriticalErrors.Count}\n" +
            $"{T("TEC03.Field.Warnings")}: {summary.Warnings.Count}";

        return CreateCard(TF("TEC03.StatusCard.Title", statusLabel), detail);
    }

    private UIElement CreateMaterialCard(SapTechnicalArticleSummary summary)
    {
        if (summary.Material is null)
            return CreateCard(T("TEC03.Section.Material"), T("TEC03.MaterialNotFound"));

        var m = summary.Material;
        var text =
            $"{T("TEC03.Field.SapNumber")}: {m.MaterialNumber}\n" +
            $"{T("TEC03.Field.Description")}: {m.Description}\n" +
            $"{T("TEC03.Field.OldNumber")}: {m.OldMaterialNumber}\n" +
            $"{T("TEC03.Field.Status")}: {m.MaterialStatus}\n" +
            $"{T("TEC03.Field.Kind")}: {m.MaterialKind}";

        return CreateCard(T("TEC03.Section.Material"), text);
    }

    private UIElement CreateMessagesPanel(SapTechnicalArticleSummary summary)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        panel.Children.Add(CreateSectionTitle(T("TEC03.Section.Checks")));

        if (summary.CriticalErrors.Count == 0 && summary.Warnings.Count == 0)
        {
            panel.Children.Add(CreateCard(T("TEC03.Checks.ResultTitle"), T("TEC03.Checks.NoIssues")));
            return panel;
        }

        if (summary.CriticalErrors.Count > 0)
            panel.Children.Add(CreateCard(
                T("TEC03.Checks.CriticalErrors"),
                string.Join("\n", summary.CriticalErrors.Select(TranslateFinding))));

        if (summary.Warnings.Count > 0)
            panel.Children.Add(CreateCard(
                T("TEC03.Checks.Warnings"),
                string.Join("\n", summary.Warnings.Select(TranslateFinding))));

        return panel;
    }

    private string TranslateFinding(SapValidationFinding finding)
    {
        if (string.IsNullOrWhiteSpace(finding.RuleId))
            return finding.Message;

        var key = $"Validation.{finding.RuleId}";
        var translated = T(key);
        return IsMissing(translated, key) ? finding.Message : translated;
    }

    // ── Plant / Variant panely ────────────────────────────────────────────────

    private UIElement CreatePlantPanel(
        SapTechnicalArticleSummaryService service,
        string plant,
        IReadOnlyList<SapTechnicalVariantSummary> variants)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 18),
            BorderThickness = new Thickness(1)
        };
        border.SetResourceReference(Border.BackgroundProperty, "DmsPanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

        var panel = new StackPanel();
        panel.Children.Add(CreateSectionTitle(TF("TEC03.PlantTitle", plant)));

        foreach (var variant in variants.OrderBy(v => NormalizeSortKey(v.Alternative)))
            panel.Children.Add(CreateVariantExpander(service, variant));

        border.Child = panel;
        return border;
    }

    private UIElement CreateVariantExpander(
        SapTechnicalArticleSummaryService service,
        SapTechnicalVariantSummary variant)
    {
        var altText = string.IsNullOrWhiteSpace(variant.Alternative)
            ? T("TEC03.NoAlternative")
            : variant.Alternative;

        var expander = new Expander
        {
            Header = TF("TEC03.VariantHeader", altText, variant.Boms.Count, variant.Routings.Count),
            IsExpanded = true,
            Margin = new Thickness(0, 0, 0, 12)
        };
        expander.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");
        expander.SetResourceReference(Control.BackgroundProperty, "DmsBackgroundBrush");

        var outer = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 8, 0, 0),
            BorderThickness = new Thickness(1)
        };
        outer.SetResourceReference(Border.BackgroundProperty, "DmsBackgroundBrush");
        outer.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

        var panel = new StackPanel();

        if (variant.Boms.Count == 0)
        {
            panel.Children.Add(CreateMutedText(T("TEC03.NoBom")));
        }
        else
        {
            foreach (var bom in variant.Boms
                         .OrderBy(b => b.BomNumber)
                         .ThenBy(b => NormalizeSortKey(b.Alternative)))
            {
                panel.Children.Add(CreateSubTitle(
                    TF("TEC03.BomSubTitle", variant.Plant, bom.BomNumber, NormalizeAltDisplay(bom.Alternative))));
                panel.Children.Add(CreateBomHeaderCard(bom, variant.Plant));
                panel.Children.Add(CreateBomGrid(
                    service.BuildBomRows(new List<SapBom> { bom }, variant.Plant),
                    variant.Plant));
            }
        }

        if (variant.Routings.Count == 0)
        {
            panel.Children.Add(CreateMutedText(T("TEC03.NoRouting")));
        }
        else
        {
            foreach (var routing in variant.Routings
                         .OrderBy(r => r.GroupNumber)
                         .ThenBy(r => NormalizeSortKey(r.Alternative)))
            {
                panel.Children.Add(CreateSubTitle(
                    TF("TEC03.RoutingSubTitle", variant.Plant, routing.GroupNumber, NormalizeAltDisplay(routing.Alternative))));
                panel.Children.Add(CreateRoutingGrid(
                    service.BuildRoutingRows(new List<SapRouting> { routing }, variant.Plant),
                    variant.Plant));
            }
        }

        outer.Child = panel;
        expander.Content = outer;
        return expander;
    }

    // ── BOM header card ───────────────────────────────────────────────────────

    private UIElement CreateBomHeaderCard(SapBom bom, string plant)
    {
        var baseQtyText = bom.BaseQuantity?.ToString("0.##") ?? string.Empty;
        var info =
            $"{T("TEC03.BomField.Number")}: {bom.BomNumber}\n" +
            $"{T("TEC03.BomField.Alternative")}: {NormalizeAltDisplay(bom.Alternative)}\n" +
            $"{T("TEC03.BomField.Usage")}: {bom.BomUsage}\n" +
            $"{T("TEC03.BomField.BaseQty")}: {baseQtyText} {bom.BaseUnit}\n" +
            $"{T("TEC03.BomField.ItemCount")}: {bom.Items.Count}";

        if (plant == "2000" && bom.BaseQuantity != 10000m)
            info += $"\n⚠ {T("TEC03.BomField.BaseQtyWarning")}";

        return CreateCard(TF("TEC03.BomHeaderTitle", plant), info);
    }

    // ── Gridy ─────────────────────────────────────────────────────────────────

    private UIElement CreateBomGrid(IReadOnlyList<SapTechnicalBomItemRow> rows, string plant)
    {
        if (rows.Count == 0)
            return CreateMutedText(T("TEC03.NoBomItems"));

        var grid = CreateBaseGrid();
        grid.Columns.Add(CreateCol(T("TEC03.Col.Position"), "Position", 80));
        grid.Columns.Add(CreateCol(T("TEC03.Col.ItemCategory"), "ItemCategory", 60));
        grid.Columns.Add(CreateCol(T("TEC03.Col.CompDesc"), "ComponentDescription", 280));
        grid.Columns.Add(CreateCol(T("TEC03.Col.CompNumber"), "ComponentNumber", 130));
        grid.Columns.Add(CreateCol(T("TEC03.Col.Quantity"), "Quantity", 100));

        // bool → lokalizovaný text přes converter
        grid.Columns.Add(CreateBoolCol(
            T("TEC03.Col.FixedQty"),
            "IsFixedQuantity",
            trueText: T("TEC03.Bool.Yes"),
            falseText: string.Empty,
            width: 120));

        if (plant == "9200")
            grid.Columns.Add(CreateCol(T("TEC03.Col.Scrap"), "ScrapPercent", 120));

        grid.Columns.Add(CreateCol(T("TEC03.Col.Unit"), "Unit", 80));
        grid.ItemsSource = rows;
        return grid;
    }

    private UIElement CreateRoutingGrid(IReadOnlyList<SapTechnicalRoutingOperationRow> rows, string plant)
    {
        if (rows.Count == 0)
            return CreateMutedText(T("TEC03.NoRoutingOps"));

        var grid = CreateBaseGrid();
        grid.Columns.Add(CreateCol(T("TEC03.Col.Operation"), "OperationNumber", 80));
        grid.Columns.Add(CreateCol(T("TEC03.Col.WorkCenter"), "WorkCenterDisplay", 260));
        grid.Columns.Add(CreateCol(T("TEC03.Col.Description"), "Description", 260));

        if (plant == "2000")
        {
            grid.Columns.Add(CreateCol(T("TEC03.Col.Scrap"), "ScrapPercent", 120));
            grid.Columns.Add(CreateCol(T("TEC03.Col.Setup"), "SetupTime", 150));
            grid.Columns.Add(CreateCol(T("TEC03.Col.ShiftTakt"), "ShiftTakt", 120));
            grid.Columns.Add(CreateCol(T("TEC03.Col.Personnel"), "PersonnelCount", 130));
        }
        else
        {
            grid.Columns.Add(CreateCol(T("TEC03.Col.ShiftTakt"), "ShiftTakt", 120));
            grid.Columns.Add(CreateCol(T("TEC03.Col.InfoRecord"), "InfoRecord", 140));
        }

        grid.ItemsSource = rows;
        return grid;
    }

    // ── UI stavební bloky ─────────────────────────────────────────────────────

    private TextBlock CreateTitle(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 16)
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");
        return tb;
    }

    private UIElement CreateSectionTitle(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 18, 0, 10)
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");
        return tb;
    }

    private UIElement CreateSubTitle(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 12, 0, 8)
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");
        return tb;
    }

    private UIElement CreateMutedText(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "DmsMutedForegroundBrush");
        return tb;
    }

    private UIElement CreateCard(string title, string body)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12),
            BorderThickness = new Thickness(1)
        };
        border.SetResourceReference(Border.BackgroundProperty, "DmsBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

        var panel = new StackPanel();

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        var bodyBox = new TextBox
        {
            Text = body,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        bodyBox.SetResourceReference(TextBox.BackgroundProperty, "DmsBackgroundBrush");
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
            MinHeight = 120,
            MaxHeight = 360,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true
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
        => new()
        {
            Header = header,
            Binding = new Binding(binding),
            Width = new DataGridLength(width)
        };

    /// <summary>
    /// Sloupec pro bool hodnotu — zobrazí lokalizovaný text místo True/False.
    /// </summary>
    private static DataGridTextColumn CreateBoolCol(
        string header,
        string binding,
        string trueText,
        string falseText,
        double width)
        => new()
        {
            Header = header,
            Binding = new Binding(binding)
            {
                Converter = new BoolToTextConverter(trueText, falseText)
            },
            Width = new DataGridLength(width)
        };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string NormalizeMaterialNumber(string value)
    {
        value = value.Trim();
        return string.IsNullOrWhiteSpace(value) ? string.Empty
            : value.All(char.IsDigit) ? value.PadLeft(10, '0') : value;
    }

    private static string NormalizeAltDisplay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Trim();
        return int.TryParse(text, out var n) ? n.ToString("00") : text;
    }

    private static string NormalizeSortKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "9999";
        var text = value.Trim();
        return int.TryParse(text, out var n) ? n.ToString("0000") : text;
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

    // ── Converter ─────────────────────────────────────────────────────────────

    private sealed class BoolToTextConverter : IValueConverter
    {
        private readonly string _trueText;
        private readonly string _falseText;

        public BoolToTextConverter(string trueText, string falseText)
        {
            _trueText = trueText;
            _falseText = falseText;
        }

        public object Convert(
            object value, Type targetType,
            object parameter, CultureInfo culture)
            => value is true ? _trueText : _falseText;

        public object ConvertBack(
            object value, Type targetType,
            object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}