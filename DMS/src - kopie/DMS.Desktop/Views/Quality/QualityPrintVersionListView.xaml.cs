using DMS.Core.Quality;
using DMS.Desktop.Logging;
using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DMS.Desktop.Views.Quality;

public partial class QualityPrintVersionListView : UserControl
{
    private const int MaxDisplayedRows = 500;

    private readonly List<QualityPrintVersionListRow> _allRows;
    private readonly DispatcherTimer _filterTimer;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly string _dmsRootPath;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    public event Action<string>? TransactionRequested;

    // Backward-compatible constructor for designer / older calls.
    public QualityPrintVersionListView()
        : this(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")),
            null,
            null)
    {
    }

    public QualityPrintVersionListView(
        string dmsRootPath,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _dmsRootPath = string.IsNullOrWhiteSpace(dmsRootPath)
            ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."))
            : dmsRootPath;

        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName)
            ? "UNKNOWN"
            : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        ApplyLocalization();

        _filterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };

        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            ApplyFilter();
        };

        var qualityPaths = new QualityStoragePaths(_dmsRootPath);
        qualityPaths.EnsureDirectories();

        var qualityRepository = new JsonQualityRepository(qualityPaths);

        var service = new QualityPrintVersionListService(
            qualityRepository.LoadPrintVersions());

        _allRows = service.BuildRows().ToList();

        _logger?.AdminAction(
            "QA05",
            "LoadPrintVersionOverview",
            _currentUserName,
            $"Root={_dmsRootPath}; Count={_allRows.Count}");

        ApplyFilter();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("QA05.Title");
        TxtHint.Text = T("QA05.Hint");

        TxtFilterArticle.Tag = T("QA05.Filter.Article");
        TxtFilterCustomer.Tag = T("QA05.Filter.Customer");
        TxtFilterTitle.Tag = T("QA05.Filter.Title");
        TxtFilterDecoration.Tag = T("QA05.Filter.Decoration");

        ColTaskStatus.Header = T("QA05.Col.Tasks");
        ColSapId.Header = T("QA05.Col.SapId");
        ColPrintVersion.Header = T("QA05.Col.PrintVersion");
        ColTitle.Header = T("QA05.Col.Title");
        ColDecoration.Header = T("QA05.Col.Decoration");
        ColCustomer.Header = T("QA05.Col.Customer");
        ColColor.Header = T("QA05.Col.Color");
    }

    private void ApplyFilter()
    {
        var articleFilter = NormalizeForFilter(TxtFilterArticle.Text);
        var customerFilter = NormalizeForFilter(TxtFilterCustomer.Text);
        var titleFilter = NormalizeForFilter(TxtFilterTitle.Text);
        var decorationFilter = NormalizeForFilter(TxtFilterDecoration.Text);

        var anyFilter =
            !string.IsNullOrWhiteSpace(articleFilter) ||
            !string.IsNullOrWhiteSpace(customerFilter) ||
            !string.IsNullOrWhiteSpace(titleFilter) ||
            !string.IsNullOrWhiteSpace(decorationFilter);

        IEnumerable<QualityPrintVersionListRow> query = _allRows;

        if (anyFilter)
        {
            query = query.Where(row =>
                ContainsAny(articleFilter, row.SapMaterialNumber, row.FullPrintVersionNumber) &&
                ContainsAny(customerFilter, row.Customer) &&
                ContainsAny(titleFilter, row.Title) &&
                ContainsAny(decorationFilter, row.Decoration, row.ColorType));
        }

        var displayedRows = query
            .Take(MaxDisplayedRows)
            .ToList();

        GridPrintVersions.ItemsSource = null;
        GridPrintVersions.ItemsSource = displayedRows;

        TxtCount.Text = anyFilter
            ? TF("QA05.Count.Filtered", displayedRows.Count, MaxDisplayedRows, _allRows.Count)
            : TF("QA05.Count.Latest", displayedRows.Count, _allRows.Count);
    }

    private void FilterChanged(object sender, TextChangedEventArgs e)
    {
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void GridPrintVersions_MouseDoubleClick(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (GridPrintVersions.SelectedItem is not QualityPrintVersionListRow row)
        {
            return;
        }

        var target = !string.IsNullOrWhiteSpace(row.SapMaterialNumber)
            ? row.SapMaterialNumber
            : row.FullPrintVersionNumber;

        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        _logger?.AdminAction(
            "QA05",
            "OpenQualityArticleFromPrintVersionList",
            _currentUserName,
            $"Target={target}; PrintVersion={row.FullPrintVersionNumber}; SapMaterial={row.SapMaterialNumber}");

        TransactionRequested?.Invoke($"QA03 {target}");
    }

    private static string NormalizeForFilter(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();

        // Ignore one-character filters; they would match almost the whole dataset.
        return text.Length < 2
            ? string.Empty
            : text;
    }

    private static bool ContainsAny(string filter, params string[] values)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return values.Any(value =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(filter, StringComparison.OrdinalIgnoreCase));
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
        return string.IsNullOrWhiteSpace(value) ||
               string.Equals(value, key, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }
}
