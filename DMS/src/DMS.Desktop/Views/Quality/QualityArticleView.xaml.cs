using DMS.Core.Quality;
using DMS.Core.Sap;
using DMS.Desktop.UI;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DMS.Desktop.Views.Quality;

public partial class QualityArticleView : UserControl
{
    private readonly QualityArticleOverviewService _service;

    public event Action<string>? TransactionRequested;

    public QualityArticleView(string query)
    {
        InitializeComponent();

        const string basePath = @"Z:\SAP\DMS-db\DEV";

        var sapStoragePaths = new SapStoragePaths(basePath);
        sapStoragePaths.EnsureDirectories();

        var sapMaterials =
            new JsonSapMaterialRepository(
                    sapStoragePaths.SapMaterialsFilePath)
                .LoadAll();

        var qualityPaths = new QualityStoragePaths(basePath);
        qualityPaths.EnsureDirectories();

        var qualityRepository =
            new JsonQualityRepository(qualityPaths);

        var statusRulesPath = Path.Combine(
            basePath,
            "Config",
            "sap-material-status-rules.json");

        var statusRules =
            new SapMaterialStatusRuleLoader()
                .LoadFromJson(statusRulesPath);

        var statusRuleService =
            new SapMaterialStatusRuleService(statusRules);

        _service = new QualityArticleOverviewService(
            sapMaterials,
            qualityRepository.LoadArticles(),
            qualityRepository.LoadPrintVersions(),
            qualityRepository.LoadOrders(),
            statusRuleService);

        Render(query);
    }

    private void Render(string query)
    {
        RootPanel.Children.Clear();

        var overview = _service.BuildOverview(query);

        RootPanel.Children.Add(
            DmsUiFactory.CreateTitle(
                $"QA03 - Quality karta {overview.Query}"));

        if (!overview.HasData)
        {
            RootPanel.Children.Add(
                DmsUiFactory.CreateWarning(
                    "Nenalezena žádná quality data",
                    "Pro zadaný dotaz nebyla nalezena SAP ani quality data.\n\n" +
                    "Zkus SAP číslo, celé číslo tiskové verze " +
                    "nebo historické sedmimístné číslo artiklu."));

            return;
        }

        RootPanel.Children.Add(
            CreateActionBar(overview));

        RootPanel.Children.Add(
            CreateSapSection(overview));

        RootPanel.Children.Add(
            CreateQualityArticleSection(overview));

        RootPanel.Children.Add(
            CreatePrintVersionsSection(overview));

        RootPanel.Children.Add(
            CreateTasksSection(overview));

        RootPanel.Children.Add(
            CreateOrdersSection(overview));

        if (overview.Messages.Count > 0)
        {
            RootPanel.Children.Add(
                DmsUiFactory.CreateInfoCard(
                    "Hlášky",
                    string.Join(
                        Environment.NewLine,
                        overview.Messages)));
        }
    }

    // ============================================================
    // ACTION BAR
    // ============================================================

    private UIElement CreateActionBar(
        QualityArticleOverview overview)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14)
        };

        if (!string.IsNullOrWhiteSpace(
                overview.SapMaterialNumber))
        {
            panel.Children.Add(
                DmsUiFactory.CreateActionButton(
                    "QA02",
                    () => RequestTransaction(
                        "QA02",
                        overview.SapMaterialNumber)));

            panel.Children.Add(
                DmsUiFactory.CreateActionButton(
                    "SAP03",
                    () => RequestTransaction(
                        "SAP03",
                        overview.SapMaterialNumber)));

            panel.Children.Add(
                DmsUiFactory.CreateActionButton(
                    "TEC03",
                    () => RequestTransaction(
                        "TEC03",
                        overview.SapMaterialNumber)));

            panel.Children.Add(
                DmsUiFactory.CreateActionButton(
                    "DOC03",
                    () => RequestTransaction(
                        "DOC03",
                        overview.SapMaterialNumber)));
        }

        panel.Children.Add(
            DmsUiFactory.CreateActionButton(
                "Aktualizovat",
                () => Render(overview.Query)));

        return panel;
    }

    private void RequestTransaction(
        string transactionCode,
        string parameter)
    {
        if (string.IsNullOrWhiteSpace(transactionCode) ||
            string.IsNullOrWhiteSpace(parameter))
        {
            return;
        }

        TransactionRequested?.Invoke(
            $"{transactionCode} {parameter.Trim()}");
    }

    // ============================================================
    // SAP ZÁKLAD
    // ============================================================

    private UIElement CreateSapSection(
        QualityArticleOverview overview)
    {
        var section =
            DmsDisplayFactory.CreateSection("SAP základ");

        if (overview.SapMaterial is null)
        {
            section.Children.Add(
                DmsUiFactory.CreateMutedText(
                    "SAP materiál nebyl nalezen."));

            return section;
        }

        var material = overview.SapMaterial;

        section.Children.Add(
            DmsDisplayFactory.CreateFieldGrid(
                3,
                new[]
                {
                    new DmsDisplayField(
                        "Staré číslo materiálu",
                        material.OldMaterialNumber),

                    new DmsDisplayField(
                        "Status",
                        overview.FormattedMaterialStatus),

                    new DmsDisplayField(
                        "Popisek (KTEXT)",
                        material.Description)
                }));

        return section;
    }

    // ============================================================
    // INFORMACE O ARTIKLU
    // ============================================================

    private UIElement CreateQualityArticleSection(
        QualityArticleOverview overview)
    {
        var section =
            DmsDisplayFactory.CreateSection(
                "Informace o artiklu");

        if (overview.QualityArticle is null)
        {
            section.Children.Add(
                DmsUiFactory.CreateMutedText(
                    "K artiklu nejsou uložené lokální quality informace."));

            return section;
        }

        section.Children.Add(
            DmsDisplayFactory.CreateFieldGrid(
                2,
                new[]
                {
                    new DmsDisplayField(
                        "Důležité info",
                        overview.QualityArticle.ImportantInfo),

                    new DmsDisplayField(
                        "Poznámky",
                        overview.QualityArticle.Notes)
                }));

        return section;
    }

    // ============================================================
    // TISKOVÉ VERZE
    // ============================================================

    private UIElement CreatePrintVersionsSection(
        QualityArticleOverview overview)
    {
        var section =
            DmsDisplayFactory.CreateSection(
                $"Tiskové verze ({overview.PrintVersions.Count})");

        if (overview.PrintVersions.Count == 0)
        {
            section.Children.Add(
                DmsUiFactory.CreateMutedText(
                    "Nejsou evidované žádné tiskové verze."));

            return section;
        }

        foreach (var printVersion in overview.PrintVersions
                     .OrderByDescending(
                         item => item.FullPrintVersionNumber))
        {
            section.Children.Add(
                CreatePrintVersionCard(printVersion));
        }

        return section;
    }

    private UIElement CreatePrintVersionCard(
        QualityPrintVersion item)
    {
        var root = new StackPanel();

        root.Children.Add(
            DmsDisplayFactory.CreateFieldGrid(
                5,
                new[]
                {
                    new DmsDisplayField(
                        "Název",
                        item.Title),

                    new DmsDisplayField(
                        "Zákazník",
                        item.Customer),

                    new DmsDisplayField(
                        "HD číslo",
                        item.HdNumber),

                    new DmsDisplayField(
                        "Umístění vzorků",
                        item.SampleLocation),

                    new DmsDisplayField(
                        "Měrka",
                        BuildGaugeText(item))
                }));

        root.Children.Add(
            DmsDisplayFactory.CreateFieldGrid(
                7,
                new[]
                {
                    new DmsDisplayField(
                        "Dekorace",
                        item.DecorationCode),

                    new DmsDisplayField(
                        "Úprava skla",
                        item.GlassTreatment),

                    new DmsDisplayField(
                        "Barva",
                        item.ColorType),

                    new DmsDisplayField(
                        "Reklamace",
                        ToYesNo(item.HasComplaint)),

                    new DmsDisplayField(
                        "Úkoly splněny",
                        ToYesNo(
                            ArePrintVersionTasksCompleted(item))),

                    new DmsDisplayField(
                        "Vzorky na kameru",
                        ToYesNo(item.SamplesOnCamera)),

                    new DmsDisplayField(
                        "Umístění prkna",
                        item.BoardLocation)
                }));

        root.Children.Add(
            DmsDisplayFactory.CreateFieldGrid(
                1,
                new[]
                {
                    new DmsDisplayField(
                        "Poznámky",
                        item.Notes)
                }));

        return DmsDisplayFactory.CreateExpanderCard(
            item.FullPrintVersionNumber,
            root);
    }

    private static bool ArePrintVersionTasksCompleted(
        QualityPrintVersion item)
    {
        var realTasks = item.Tasks
            .Where(task =>
                !string.IsNullOrWhiteSpace(task.Text))
            .ToList();

        return realTasks.Count == 0 ||
               realTasks.All(task =>
                   task.CompletedAt.HasValue);
    }

    private static string BuildGaugeText(
        QualityPrintVersion item)
    {
        if (!item.HasGauge &&
            string.IsNullOrWhiteSpace(
                item.GaugeLocation))
        {
            return "Ne";
        }

        if (string.IsNullOrWhiteSpace(
                item.GaugeLocation))
        {
            return ToYesNo(item.HasGauge);
        }

        return item.HasGauge
            ? $"Ano – {item.GaugeLocation}"
            : item.GaugeLocation;
    }

    // ============================================================
    // QUALITY ÚKOLY
    // ============================================================

    private UIElement CreateTasksSection(
        QualityArticleOverview overview)
    {
        var section =
            DmsDisplayFactory.CreateSection(
                $"Quality úkoly ({overview.Tasks.Count})");

        if (overview.Tasks.Count == 0)
        {
            section.Children.Add(
                DmsUiFactory.CreateMutedText(
                    "Nejsou evidované žádné quality úkoly."));

            return section;
        }

        var grid =
            DmsUiFactory.CreateDataGrid(
                ForwardMouseWheelToOuterScroll);

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Číslo",
                "Number",
                70));

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Úkol",
                "Text",
                520));

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Autor úkolu",
                "CreatedBy",
                140));

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Termín splnění",
                "DueDateText",
                125));

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Stav",
                "CompletedText",
                120));

        grid.ItemsSource = overview.Tasks;

        section.Children.Add(grid);

        return section;
    }

    // ============================================================
    // ZAKÁZKY
    // ============================================================

    private UIElement CreateOrdersSection(
        QualityArticleOverview overview)
    {
        var section =
            DmsDisplayFactory.CreateSection(
                $"Zakázky ({overview.Orders.Count})");

        if (overview.Orders.Count == 0)
        {
            section.Children.Add(
                DmsUiFactory.CreateMutedText(
                    "Nejsou evidované žádné zakázky."));

            return section;
        }

        var rows = overview.Orders
            .Select(CreateOrderDisplayRow)
            .ToList();

        var grid =
            DmsUiFactory.CreateDataGrid(
                ForwardMouseWheelToOuterScroll);

        grid.MouseDoubleClick += (_, _) =>
        {
            if (grid.SelectedItem is not
                QualityOrderDisplayRow row)
            {
                return;
            }

            RequestTransaction(
                "QO03",
                row.OrderNumber);
        };

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Zakázka",
                nameof(QualityOrderDisplayRow.OrderNumber),
                100));

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Stroje",
                nameof(QualityOrderDisplayRow.Machines),
                150));

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Od",
                nameof(QualityOrderDisplayRow.ProductionStartText),
                105));

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Do",
                nameof(QualityOrderDisplayRow.ProductionEndText),
                105));

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Objednáno",
                nameof(QualityOrderDisplayRow.OrderedQuantity),
                100));

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Odbaveno",
                nameof(QualityOrderDisplayRow.ProducedQuantity),
                100));

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Lab. zakázka",
                nameof(QualityOrderDisplayRow.LabOrderNumber),
                125));

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "L'Oréal",
                nameof(QualityOrderDisplayRow.LorealText),
                75));

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Třída",
                nameof(QualityOrderDisplayRow.QualityClass),
                90));

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Třídění v HD",
                nameof(QualityOrderDisplayRow.SortingInHdText),
                105));

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Zůstává v HD",
                nameof(QualityOrderDisplayRow.StaysInHdText),
                110));

        grid.Columns.Add(
            DmsUiFactory.CreateTextColumn(
                "Poznámky",
                nameof(QualityOrderDisplayRow.Notes),
                360));

        grid.ItemsSource = rows;

        section.Children.Add(
            DmsUiFactory.CreateSmallHint(
                "Dvojklik otevře zakázku v QO03."));

        section.Children.Add(grid);

        return section;
    }

    private static QualityOrderDisplayRow CreateOrderDisplayRow(
        QualityOrder order)
    {
        return new QualityOrderDisplayRow
        {
            OrderNumber = order.OrderNumber,

            Machines = NormalizeMultiValue(
                order.Machine),

            ProductionStartText =
                order.ProductionStart?
                    .ToString("dd.MM.yyyy")
                ?? string.Empty,

            ProductionEndText =
                order.ProductionEnd?
                    .ToString("dd.MM.yyyy")
                ?? string.Empty,

            OrderedQuantity =
                order.OrderedQuantity,

            ProducedQuantity =
                order.ProducedQuantity,

            LabOrderNumber =
                order.LabOrderNumber,

            LorealText =
                ToYesNo(order.Loreal),

            QualityClass =
                order.QualityClass,

            SortingInHdText =
                ToYesNo(order.SortingInHd),

            StaysInHdText =
                ToYesNo(order.StaysInHd),

            Notes =
                order.Notes,

            Source =
                order
        };
    }

    // ============================================================
    // SCROLL
    // ============================================================

    private void ForwardMouseWheelToOuterScroll(
        object sender,
        MouseWheelEventArgs e)
    {
        if (RootScrollViewer is null)
        {
            return;
        }

        e.Handled = true;

        RootScrollViewer.ScrollToVerticalOffset(
            RootScrollViewer.VerticalOffset - e.Delta);
    }

    // ============================================================
    // DISPLAY HELPERS SPECIFICKÉ PRO QUALITY
    // ============================================================

    private static string NormalizeMultiValue(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            ", ",
            value
                .Split(
                    new[] { ";#" },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase));
    }

    private static string ToYesNo(bool value)
    {
        return value ? "Ano" : "Ne";
    }

    // ============================================================
    // DISPLAY MODEL PRO ZAKÁZKY
    // ============================================================

    private sealed class QualityOrderDisplayRow
    {
        public string OrderNumber { get; init; } =
            string.Empty;

        public string Machines { get; init; } =
            string.Empty;

        public string ProductionStartText { get; init; } =
            string.Empty;

        public string ProductionEndText { get; init; } =
            string.Empty;

        public int? OrderedQuantity { get; init; }

        public int? ProducedQuantity { get; init; }

        public string LabOrderNumber { get; init; } =
            string.Empty;

        public string LorealText { get; init; } =
            string.Empty;

        public string QualityClass { get; init; } =
            string.Empty;

        public string SortingInHdText { get; init; } =
            string.Empty;

        public string StaysInHdText { get; init; } =
            string.Empty;

        public string Notes { get; init; } =
            string.Empty;

        public QualityOrder Source { get; init; } = null!;
    }
}