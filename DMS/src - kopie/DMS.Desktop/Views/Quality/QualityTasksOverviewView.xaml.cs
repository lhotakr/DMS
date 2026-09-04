using DMS.Core.Quality;
using DMS.Core.Sap;
using DMS.Desktop.Logging;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Quality;

public partial class QualityTasksOverviewView : UserControl
{
    private readonly QualityTaskOverviewService _service;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private IReadOnlyList<QualityTaskCockpitRow> _allRows =
        Array.Empty<QualityTaskCockpitRow>();

    public event Action<string>? TransactionRequested;

    // Designer / backward compatibility constructor.
    public QualityTasksOverviewView()
        : this(
            System.IO.Path.GetFullPath(
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

        ApplyLocalization();

        CmbCompletionFilter.SelectedIndex = 0;

        var rootPath = string.IsNullOrWhiteSpace(dmsRootPath)
            ? System.IO.Path.GetFullPath(
                System.IO.Path.Combine(AppContext.BaseDirectory, ".."))
            : dmsRootPath;

        var sapStoragePaths = new SapStoragePaths(rootPath);
        sapStoragePaths.EnsureDirectories();

        var sapMaterials =
            new JsonSapMaterialRepository(
                    sapStoragePaths.SapMaterialsFilePath)
                .LoadAll();

        var qualityPaths = new QualityStoragePaths(rootPath);
        qualityPaths.EnsureDirectories();

        var qualityRepository =
            new JsonQualityRepository(qualityPaths);

        _service = new QualityTaskOverviewService(
            sapMaterials,
            qualityRepository.LoadPrintVersions());

        _logger?.AdminAction(
            "QATASK",
            "OpenQualityTaskOverview",
            _currentUserName,
            $"Root={rootPath}; SapMaterials={sapMaterials.Count}");

        LoadData();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("QATASK.Title");
        TxtSubtitle.Text = T("QATASK.Subtitle");
        TxtCompletionFilterLabel.Text = T("QATASK.Filter.Completion");
        TxtSapFilterLabel.Text = T("QATASK.Filter.SapId");
        CbiOpen.Content = T("QATASK.Filter.Open");
        CbiDone.Content = T("QATASK.Filter.Done");
        CbiAll.Content = T("QATASK.Filter.All");
        BtnClearFilter.Content = T("QATASK.Button.Clear");
        BtnReload.Content = T("QATASK.Button.Refresh");
        TxtHint.Text = T("QATASK.Hint.DoubleClick");

        ColSapId.Header = T("QATASK.Column.SapId");
        ColMaterialStatus.Header = T("QATASK.Column.MaterialStatus");
        ColOldNumber.Header = T("QATASK.Column.OldNumber");
        ColTaskNumber.Header = T("QATASK.Column.TaskNumber");
        ColTaskText.Header = T("QATASK.Column.TaskText");
        ColCreatedAt.Header = T("QATASK.Column.CreatedAt");
        ColCreatedBy.Header = T("QATASK.Column.CreatedBy");
        ColDueDate.Header = T("QATASK.Column.DueDate");
        ColDelay.Header = T("QATASK.Column.Delay");
        ColCompletedAt.Header = T("QATASK.Column.CompletedAt");
        ColCompletedBy.Header = T("QATASK.Column.CompletedBy");
    }

    private void LoadData()
    {
        _allRows = _service.BuildRows();

        _logger?.AdminAction(
            "QATASK",
            "LoadQualityTasks",
            _currentUserName,
            $"Total={_allRows.Count}");

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var rows = _allRows.AsEnumerable();

        var selectedTag =
            (CmbCompletionFilter.SelectedItem as ComboBoxItem)
            ?.Tag
            ?.ToString()
            ?? "open";

        rows = selectedTag switch
        {
            "done" => rows.Where(item => item.IsCompleted),
            "all" => rows,
            _ => rows.Where(item => !item.IsCompleted)
        };

        var sapFilter =
            TxtSapFilter.Text?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(sapFilter))
        {
            rows = rows.Where(item =>
                Contains(item.SapMaterialNumber, sapFilter) ||
                Contains(item.OldMaterialNumber, sapFilter) ||
                Contains(item.FullPrintVersionNumber, sapFilter) ||
                Contains(item.TaskText, sapFilter));
        }

        var finalRows = rows.ToList();

        GridTasks.ItemsSource = finalRows;

        TxtStatus.Text = TF(
            "QATASK.Status.Filtered",
            finalRows.Count,
            _allRows.Count);

        ResizeTaskColumn();
    }

    private void GridTasks_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        ResizeTaskColumn();
    }

    private void GridTasks_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        ResizeTaskColumn();
    }

    private void ResizeTaskColumn()
    {
        if (GridTasks.ActualWidth <= 0)
        {
            return;
        }

        const double safetyMargin = 36;

        var fixedWidth = GridTasks.Columns
            .Where(column => column != ColTaskText)
            .Sum(column => column.ActualWidth > 0
                ? column.ActualWidth
                : column.MinWidth);

        var availableWidth =
            GridTasks.ActualWidth - fixedWidth - safetyMargin;

        ColTaskText.Width =
            new DataGridLength(
                Math.Max(420, availableWidth),
                DataGridLengthUnitType.Pixel);
    }

    private void Filter_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyFilter();
    }

    private void BtnClearFilter_Click(
        object sender,
        RoutedEventArgs e)
    {
        TxtSapFilter.Clear();

        foreach (var item in CmbCompletionFilter.Items
                     .OfType<ComboBoxItem>())
        {
            if (string.Equals(
                    item.Tag?.ToString(),
                    "open",
                    StringComparison.OrdinalIgnoreCase))
            {
                CmbCompletionFilter.SelectedItem = item;
                break;
            }
        }

        _logger?.AdminAction(
            "QATASK",
            "ClearQualityTaskFilter",
            _currentUserName,
            string.Empty);

        ApplyFilter();
    }

    private void BtnReload_Click(
        object sender,
        RoutedEventArgs e)
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
        if (GridTasks.SelectedItem is not QualityTaskCockpitRow row)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(row.FullPrintVersionNumber))
        {
            return;
        }

        _logger?.AdminAction(
            "QATASK",
            "OpenQualityArticleFromTaskOverview",
            _currentUserName,
            $"PrintVersion={row.FullPrintVersionNumber}; SapMaterial={row.SapMaterialNumber}; Task={row.TaskNumber}");

        TransactionRequested?.Invoke(
            $"QA03 {row.FullPrintVersionNumber}");
    }

    private static bool Contains(string? value, string filter)
    {
        return value?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;

        return IsMissing(value, key)
            ? key
            : value;
    }

    private string TF(string key, params object[] args)
    {
        if (_translateFormat is not null)
        {
            return _translateFormat(key, args);
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

    private static bool IsMissing(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }
}
