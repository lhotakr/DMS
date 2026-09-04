using DMS.Core.Quality;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DMS.Desktop.Views.QualityOrders;

public partial class QualityOrderView : UserControl
{
    private readonly QualityOrderMaintenanceService _service;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    public event Action<string>? TransactionRequested;

    public QualityOrderView(
        string query,
        string dmsRootPath,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName)
            ? "UNKNOWN"
            : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        var rootPath = string.IsNullOrWhiteSpace(dmsRootPath)
            ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."))
            : dmsRootPath;

        var paths = new QualityStoragePaths(rootPath);
        paths.EnsureDirectories();
        _service = new QualityOrderMaintenanceService(new JsonQualityRepository(paths));

        Render(query);
    }

    private void Render(string query)
    {
        RootPanel.Children.Clear();

        var model = _service.PrepareEdit(query);

        RootPanel.Children.Add(DmsUiFactory.CreateTitle(TF("QO03.Title", query)));

        if (model is null || model.OriginalOrder is null)
        {
            RootPanel.Children.Add(
                DmsUiFactory.CreateWarning(
                    T("QO03.NotFound.Title"),
                    TF("QO03.NotFound.Message", query)));

            RootPanel.Children.Add(
                CreateActionButton(
                    T("QO03.Action.Create"),
                    () => TransactionRequested?.Invoke($"QO01 {query}".Trim()),
                    primary: true));

            _logger?.AdminAction(
                "QO03",
                "QualityOrderNotFound",
                _currentUserName,
                $"Query={query}");
            return;
        }

        var order = model.OriginalOrder;

        RootPanel.Children.Add(CreateActionBar(order, model));
        RootPanel.Children.Add(CreateStatusBanner(order));
        RootPanel.Children.Add(CreateMasterSection(model));
        RootPanel.Children.Add(CreateArticleDataSection(model));
        RootPanel.Children.Add(CreateOpenTasksSection(model));
        RootPanel.Children.Add(CreateOrderSection(order));
        RootPanel.Children.Add(CreateReleaseChecklistSection(order));
        RootPanel.Children.Add(CreateNotesSection(model, order));

        _logger?.AdminAction(
            "QO03",
            "OpenQualityOrderDisplay",
            _currentUserName,
            $"Order={order.OrderNumber}; PrintVersion={order.PrintVersionNumber}; SapMaterial={order.SapMaterialNumber}; Released={order.Released}");
    }

    private UIElement CreateActionBar(QualityOrder order, QualityOrderFormModel model)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14)
        };

        panel.Children.Add(CreateActionButton(T("QO.Action.OpenQO02"), () => TransactionRequested?.Invoke($"QO02 {order.OrderNumber}"), primary: true));
        panel.Children.Add(CreateActionButton(T("QO.Action.OpenQO06"), () => TransactionRequested?.Invoke($"QO06 {order.OrderNumber}")));
        panel.Children.Add(CreateActionButton(T("QO.Action.OpenTEC03"), () => TransactionRequested?.Invoke($"TEC03 {TargetForTec03(model)}")));
        panel.Children.Add(CreateActionButton(T("QO.Action.OpenQO05"), () => TransactionRequested?.Invoke("QO05")));

        return panel;
    }

    private UIElement CreateStatusBanner(QualityOrder order)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            BorderThickness = new Thickness(1)
        };
        border.SetResourceReference(Border.BackgroundProperty, "DmsBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

        var scheduleCode = QualityOrderMaintenanceService.GetScheduleStatusCode(order);

        var text = new TextBlock
        {
            Text = $"{T("QO.Field.ReleaseState")}: {(order.Released ? T("QO.Release.Released") : T("QO.Release.Blocked"))}    |    {T("QO.Field.ScheduleStatus")}: {T($"QO.Status.{scheduleCode}")}",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        };
        text.Foreground = order.Released ? Brushes.LightGreen : Brushes.Orange;

        border.Child = text;
        return border;
    }

    private UIElement CreateMasterSection(QualityOrderFormModel model)
    {
        var section = DmsDisplayFactory.CreateSection(T("QO.Section.PrintVersion"));

        section.Children.Add(
            DmsDisplayFactory.CreateFieldGrid(
                4,
                new[]
                {
                    new DmsDisplayField(T("QO.Field.PrintVersion"), model.PrintVersionNumber),
                    new DmsDisplayField(T("QO.Field.SapId"), model.SapMaterialNumber),
                    new DmsDisplayField(T("QO.Field.Customer"), model.Customer),
                    new DmsDisplayField(T("QO.Field.Title"), model.Title),
                    new DmsDisplayField(T("QO.Field.SampleLocation"), model.SampleLocation),
                    new DmsDisplayField(T("QO.Field.BoardLocation"), model.BoardLocation),
                    new DmsDisplayField(T("QO.Field.HdNumber"), model.HdNumber),
                    new DmsDisplayField(T("QO.Field.Gauge"), BuildGaugeText(model)),
                    new DmsDisplayField(T("QO.Field.SamplesOnCamera"), ToYesNo(model.SamplesOnCamera)),
                    new DmsDisplayField(T("QO.Field.TasksCompleted"), model.AllTasksCompleted ? $"✓ {model.TaskSummary}" : $"⚠ {model.TaskSummary}")
                }));

        return section;
    }

    private UIElement CreateArticleDataSection(QualityOrderFormModel model)
    {
        var hasArticleData = !string.IsNullOrWhiteSpace(model.LegacyArticleNumber) ||
                             !string.IsNullOrWhiteSpace(model.ArticleTitle) ||
                             !string.IsNullOrWhiteSpace(model.ArticleImportantInfo) ||
                             !string.IsNullOrWhiteSpace(model.ArticleNotes);

        if (!hasArticleData)
        {
            return new Border { Visibility = Visibility.Collapsed };
        }

        var section = DmsDisplayFactory.CreateSection(T("QO.Section.ArticleData"));

        section.Children.Add(
            DmsDisplayFactory.CreateFieldGrid(
                3,
                new[]
                {
                    new DmsDisplayField(T("QO.Field.LegacyArticle"), model.LegacyArticleNumber),
                    new DmsDisplayField(T("QO.Field.ArticleTitle"), model.ArticleTitle),
                    new DmsDisplayField(T("QO.Field.ArticleImportantInfo"), model.ArticleImportantInfo)
                }));

        section.Children.Add(
            DmsUiFactory.CreateInfoCard(
                T("QO.Field.ArticleNotes"),
                string.IsNullOrWhiteSpace(model.ArticleNotes)
                    ? T("QO.Text.NoArticleData")
                    : model.ArticleNotes));

        return section;
    }

    private UIElement CreateOpenTasksSection(QualityOrderFormModel model)
    {
        var section = DmsDisplayFactory.CreateSection(T("QO.Section.OpenTasks"));

        section.Children.Add(
            DmsUiFactory.CreateInfoCard(
                T("QO.Field.OpenTasks"),
                string.IsNullOrWhiteSpace(model.OpenTasksText)
                    ? T("QO.Text.NoOpenTasks")
                    : model.OpenTasksText));

        return section;
    }

    private UIElement CreateOrderSection(QualityOrder order)
    {
        var section = DmsDisplayFactory.CreateSection(T("QO.Section.OrderData"));
        var scheduleCode = QualityOrderMaintenanceService.GetScheduleStatusCode(order);

        section.Children.Add(
            DmsDisplayFactory.CreateFieldGrid(
                4,
                new[]
                {
                    new DmsDisplayField(T("QO.Field.OrderNumber"), order.OrderNumber),
                    new DmsDisplayField(T("QO.Field.Machine"), order.Machine),
                    new DmsDisplayField(T("QO.Field.ColorType"), order.ColorType),
                    new DmsDisplayField(T("QO.Field.QualityClass"), order.QualityClass),
                    new DmsDisplayField(T("QO.Field.ProductionStart"), FormatDate(order.ProductionStart)),
                    new DmsDisplayField(T("QO.Field.ProductionEnd"), FormatDate(order.ProductionEnd)),
                    new DmsDisplayField(T("QO.Field.ScheduleStatus"), T($"QO.Status.{scheduleCode}")),
                    new DmsDisplayField(T("QO.Field.ReleaseState"), order.Released ? T("QO.Release.Released") : T("QO.Release.Blocked")),
                    new DmsDisplayField(T("QO.Field.OrderedQuantity"), order.OrderedQuantity?.ToString()),
                    new DmsDisplayField(T("QO.Field.ProducedQuantity"), order.ProducedQuantity?.ToString()),
                    new DmsDisplayField(T("QO.Field.LabOrderNumber"), order.LabOrderNumber),
                    new DmsDisplayField(T("QO.Field.LorealLabOrder"), order.LorealLabOrder),
                    new DmsDisplayField(T("QO.Flag.Loreal"), ToYesNo(order.Loreal)),
                    new DmsDisplayField(T("QO.Flag.SortingInHd"), ToYesNo(order.SortingInHd)),
                    new DmsDisplayField(T("QO.Flag.StaysInHd"), ToYesNo(order.StaysInHd))
                }));

        return section;
    }

    private UIElement CreateReleaseChecklistSection(QualityOrder order)
    {
        var section = DmsDisplayFactory.CreateSection(T("QO06.ReleaseChecklist.Title"));

        section.Children.Add(
            DmsDisplayFactory.CreateFieldGrid(
                3,
                new[]
                {
                    new DmsDisplayField(T("QO06.Check.TesaTest"), ToYesNo(order.TesaTest)),
                    new DmsDisplayField(T("QO06.Check.AcetoneTest"), ToYesNo(order.AcetoneTest)),
                    new DmsDisplayField(T("QO06.Check.GridTest"), ToYesNo(order.GridTest)),
                    new DmsDisplayField(T("QO06.Check.VisualCheck"), ToYesNo(order.VisualCheck)),
                    new DmsDisplayField(T("QO06.Check.Approved"), ToYesNo(order.Approved)),
                    new DmsDisplayField(T("QO06.Field.ReleasedBy"), order.ReleasedBy),
                    new DmsDisplayField(T("QO06.Field.ReleasedAt"), FormatDateTime(order.ReleasedAt)),
                    new DmsDisplayField(T("QO06.Field.BlockedBy"), order.BlockedBy),
                    new DmsDisplayField(T("QO06.Field.BlockedAt"), FormatDateTime(order.BlockedAt))
                }));

        section.Children.Add(
            DmsUiFactory.CreateInfoCard(
                T("QO06.Field.ReleaseNotes"),
                string.IsNullOrWhiteSpace(order.ReleaseNotes)
                    ? T("QO06.Text.NoReleaseNotes")
                    : order.ReleaseNotes));

        return section;
    }

    private UIElement CreateNotesSection(QualityOrderFormModel model, QualityOrder order)
    {
        var section = DmsDisplayFactory.CreateSection(T("QO.Section.Notes"));

        section.Children.Add(
            DmsUiFactory.CreateInfoCard(
                T("QO.Field.OrderNotesImportant"),
                string.IsNullOrWhiteSpace(order.Notes)
                    ? T("QO.Text.NoOrderNotes")
                    : order.Notes));

        section.Children.Add(
            DmsUiFactory.CreateInfoCard(
                T("QO.Field.PrintVersionNotes"),
                string.IsNullOrWhiteSpace(model.PrintVersionNotes)
                    ? T("QO.Text.NoPrintVersionNotes")
                    : model.PrintVersionNotes));

        return section;
    }

    private Button CreateActionButton(string text, Action action, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 120,
            Margin = new Thickness(0, 0, 8, 0)
        };

        button.SetResourceReference(FrameworkElement.StyleProperty, primary ? "DmsPrimaryButtonStyle" : "DmsFormButtonStyle");
        button.Click += (_, _) => action();

        return button;
    }

    private static string TargetForTec03(QualityOrderFormModel model)
    {
        return !string.IsNullOrWhiteSpace(model.SapMaterialNumber)
            ? model.SapMaterialNumber
            : model.PrintVersionNumber;
    }

    private string BuildGaugeText(QualityOrderFormModel model)
    {
        if (!model.HasGauge && string.IsNullOrWhiteSpace(model.GaugeLocation))
        {
            return T("Common.No");
        }

        if (string.IsNullOrWhiteSpace(model.GaugeLocation))
        {
            return model.HasGauge ? T("Common.Yes") : T("Common.No");
        }

        return model.HasGauge
            ? $"{T("Common.Yes")} - {model.GaugeLocation}"
            : model.GaugeLocation;
    }

    private static string FormatDate(DateTime? date)
    {
        return date?.ToString("dd.MM.yyyy") ?? string.Empty;
    }

    private static string FormatDateTime(DateTime? date)
    {
        return date?.ToString("dd.MM.yyyy HH:mm") ?? string.Empty;
    }

    private string ToYesNo(bool value)
    {
        return value ? T("Common.Yes") : T("Common.No");
    }

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;
        return IsMissing(value, key) ? key : value;
    }

    private string TF(string key, params object[] args)
    {
        if (_translateFormat is not null)
        {
            var value = _translateFormat(key, args);
            return IsMissing(value, key) ? key : value;
        }

        var pattern = T(key);

        try
        {
            return string.Format(pattern, args);
        }
        catch
        {
            return pattern;
        }
    }

    private static bool IsMissing(string value, string key)
    {
        return string.IsNullOrWhiteSpace(value) ||
               string.Equals(value, key, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }
}
