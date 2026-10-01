using DMS.Core.Quality;
using DMS.Core.Sap;
using DMS.Desktop.Logging;
using DMS.Desktop.UI.FunctionKeys;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DMS.Desktop.Views.Quality;

public partial class QualityTasksOverviewView : UserControl, IDmsFunctionKeyHost
{
    private readonly string _rootPath;
    private readonly JsonQualityRepository _repository;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private IReadOnlyList<QualityTaskCockpitRow> _allPrintVersionRows =
        Array.Empty<QualityTaskCockpitRow>();
    private IReadOnlyList<QualityOrderTaskCockpitRow> _allOrderRows =
        Array.Empty<QualityOrderTaskCockpitRow>();

    public event Action<string>? TransactionRequested;

    public QualityTasksOverviewView()
        : this(System.IO.Path.GetFullPath(
            System.IO.Path.Combine(AppContext.BaseDirectory, "..")))
    {
    }

    public QualityTasksOverviewView(
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

        _rootPath = string.IsNullOrWhiteSpace(dmsRootPath)
            ? System.IO.Path.GetFullPath(
                System.IO.Path.Combine(AppContext.BaseDirectory, ".."))
            : dmsRootPath;

        var qualityPaths = new QualityStoragePaths(_rootPath);
        qualityPaths.EnsureDirectories();
        _repository = new JsonQualityRepository(qualityPaths);

        ApplyLocalization();
        CmbCompletionFilter.SelectedIndex = 0;

        _logger?.AdminAction(
            "QATASK",
            "OpenQualityTaskOverview",
            _currentUserName,
            $"Root={_rootPath}");

        LoadData();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = TOr("QATASK.Title", "QATASK - Quality úkoly");
        TxtSubtitle.Text = TOr(
            "QATASK.Subtitle",
            "Přehled úkolů tiskových verzí a úkolů konkrétních zakázek.");
        TxtCompletionFilterLabel.Text = TOr("QATASK.Filter.Completion", "Stav");
        TxtSapFilterLabel.Text = TOr("QATASK.Filter.SapId", "Hledat");
        CbiOpen.Content = TOr("QATASK.Filter.Open", "Otevřené");
        CbiDone.Content = TOr("QATASK.Filter.Done", "Dokončené");
        CbiAll.Content = TOr("QATASK.Filter.All", "Vše");
        BtnClearFilter.Content = TOr("QATASK.Button.Clear", "Vymazat");
        BtnReload.Content = TOr("QATASK.Button.Refresh", "Obnovit");
        TxtHint.Text = TOr(
            "QATASK.Hint.DoubleClick",
            "Dvojklik vlevo otevře QA03, dvojklik vpravo otevře QO02.");

        TxtPrintVersionTasksTitle.Text = TOr(
            "QATASK.Panel.PrintVersionTasks",
            "Úkoly tiskových verzí");
        TxtOrderTasksTitle.Text = TOr(
            "QATASK.Panel.OrderTasks",
            "Úkoly zakázek");

        // Keep the original print-version task columns; this panel is the existing QATASK moved left.
        ColSapId.Header = TOr("QATASK.Column.SapId", "SAP ID");
        ColMaterialStatus.Header = TOr("QATASK.Column.MaterialStatus", "Status");
        ColOldNumber.Header = TOr("QATASK.Column.OldNumber", "Staré číslo");
        ColTaskNumber.Header = TOr("QATASK.Column.TaskNumber", "Číslo úkolu");
        ColTaskText.Header = TOr("QATASK.Column.TaskText", "Úkol");
        ColCreatedAt.Header = TOr("QATASK.Column.CreatedAt", "Vytvořeno");
        ColCreatedBy.Header = TOr("QATASK.Column.CreatedBy", "Vytvořil");
        ColDueDate.Header = TOr("QATASK.Column.DueDate", "Termín");
        ColDelay.Header = TOr("QATASK.Column.Delay", "Prodlení");
        ColCompletedAt.Header = TOr("QATASK.Column.CompletedAt", "Hotovo");
        ColCompletedBy.Header = TOr("QATASK.Column.CompletedBy", "Dokončil");

        ColOrderNumber.Header = TOr("QATASK.Column.OrderNumber", "Zakázka");
        ColOrderSapId.Header = TOr("QATASK.Column.SapId", "SAP ID");
        ColOrderPrintVersion.Header = TOr("QATASK.Column.PrintVersion", "Tisková verze");
        ColOrderTaskNumber.Header = TOr("QATASK.Column.TaskNumber", "#");
        ColOrderTaskText.Header = TOr("QATASK.Column.TaskText", "Úkol");
        ColOrderCreatedAt.Header = TOr("QATASK.Column.CreatedAt", "Vytvořeno");
        ColOrderCreatedBy.Header = TOr("QATASK.Column.CreatedBy", "Vytvořil");
        ColOrderDueDate.Header = TOr("QATASK.Column.DueDate", "Termín");
        ColOrderDelay.Header = TOr("QATASK.Column.Delay", "Prodlení");
        ColOrderCompletedAt.Header = TOr("QATASK.Column.CompletedAt", "Hotovo");
        ColOrderCompletedBy.Header = TOr("QATASK.Column.CompletedBy", "Dokončil");
    }

    private void LoadData()
    {
        var sapStoragePaths = new SapStoragePaths(_rootPath);
        sapStoragePaths.EnsureDirectories();
        var sapMaterials = new JsonSapMaterialRepository(
                sapStoragePaths.SapMaterialsFilePath)
            .LoadAll();

        _allPrintVersionRows = new QualityTaskOverviewService(
                sapMaterials,
                _repository.LoadPrintVersions())
            .BuildRows();
        _allOrderRows = new QualityOrderTaskOverviewService(
                _repository.LoadOrders())
            .BuildRows();

        _logger?.AdminAction(
            "QATASK",
            "LoadQualityTasks",
            _currentUserName,
            $"PrintVersionTasks={_allPrintVersionRows.Count}; OrderTasks={_allOrderRows.Count}");

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var selectedTag =
            (CmbCompletionFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString()
            ?? "open";
        var filter = TxtSapFilter.Text?.Trim() ?? string.Empty;

        IEnumerable<QualityTaskCockpitRow> printRows = _allPrintVersionRows;
        IEnumerable<QualityOrderTaskCockpitRow> orderRows = _allOrderRows;

        printRows = selectedTag switch
        {
            "done" => printRows.Where(item => item.IsCompleted),
            "all" => printRows,
            _ => printRows.Where(item => !item.IsCompleted)
        };

        orderRows = selectedTag switch
        {
            "done" => orderRows.Where(item => item.IsCompleted),
            "all" => orderRows,
            _ => orderRows.Where(item => !item.IsCompleted)
        };

        if (!string.IsNullOrWhiteSpace(filter))
        {
            printRows = printRows.Where(item =>
                Contains(item.SapMaterialNumber, filter) ||
                Contains(item.OldMaterialNumber, filter) ||
                Contains(item.FullPrintVersionNumber, filter) ||
                Contains(item.TaskText, filter));

            orderRows = orderRows.Where(item =>
                Contains(item.OrderNumber, filter) ||
                Contains(item.SapMaterialNumber, filter) ||
                Contains(item.PrintVersionNumber, filter) ||
                Contains(item.TaskText, filter));
        }

        var finalPrintRows = printRows.ToList();
        var finalOrderRows = orderRows.ToList();

        GridTasks.ItemsSource = finalPrintRows;
        GridOrderTasks.ItemsSource = finalOrderRows;
        TxtStatus.Text = $"Tiskové verze: {finalPrintRows.Count}/{_allPrintVersionRows.Count}   |   Zakázky: {finalOrderRows.Count}/{_allOrderRows.Count}";
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyFilter();
        }
    }

    private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
    {
        TxtSapFilter.Clear();
        CmbCompletionFilter.SelectedIndex = 0;

        _logger?.AdminAction(
            "QATASK",
            "ClearQualityTaskFilter",
            _currentUserName,
            string.Empty);

        ApplyFilter();
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        _logger?.AdminAction(
            "QATASK",
            "RefreshQualityTasks",
            _currentUserName,
            string.Empty);
        LoadData();
    }

    private void GridTasks_MouseDoubleClick(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (GridTasks.SelectedItem is not QualityTaskCockpitRow row ||
            string.IsNullOrWhiteSpace(row.FullPrintVersionNumber))
        {
            return;
        }

        TransactionRequested?.Invoke($"QA03 {row.FullPrintVersionNumber}");
    }

    private void GridOrderTasks_MouseDoubleClick(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (GridOrderTasks.SelectedItem is not QualityOrderTaskCockpitRow row ||
            string.IsNullOrWhiteSpace(row.OrderNumber))
        {
            return;
        }

        _logger?.AdminAction(
            "QATASK",
            "OpenQualityOrderFromTaskOverview",
            _currentUserName,
            $"Order={row.OrderNumber}; Task={row.TaskNumber}");

        TransactionRequested?.Invoke($"QO02 {row.OrderNumber}");
    }

    public IReadOnlyList<DmsFunctionKeyAction> GetFunctionKeyActions()
    {
        return new[]
        {
            new DmsFunctionKeyAction(
                Key.F5,
                TOr("FunctionKey.Refresh", "Obnovit"),
                LoadData)
        };
    }

    private static bool Contains(string? value, string filter)
    {
        return value?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;
        return IsMissing(value, key) ? key : value;
    }

    private string TOr(string key, string fallback)
    {
        var value = T(key);
        return IsMissing(value, key) ? fallback : value;
    }

    private static bool IsMissing(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }
}
