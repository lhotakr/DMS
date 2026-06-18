using DMS.Core.Quality;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DMS.Desktop.Views.Quality;

public partial class QualityPrintVersionListView : UserControl
{
    private const int MaxDisplayedRows = 500;

    private readonly List<QualityPrintVersionListRow> _allRows;
    private readonly DispatcherTimer _filterTimer;

    public event Action<string>? TransactionRequested;

    public QualityPrintVersionListView()
    {
        InitializeComponent();

        _filterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };

        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            ApplyFilter();
        };

        var basePath = @"Z:\SAP\DMS-db\DEV";

        var qualityPaths = new QualityStoragePaths(basePath);
        var qualityRepository = new JsonQualityRepository(qualityPaths);

        var service = new QualityPrintVersionListService(
            qualityRepository.LoadPrintVersions());

        _allRows = service.BuildRows().ToList();

        ApplyFilter();
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
            ? $"Zobrazeno: {displayedRows.Count:N0} / max. {MaxDisplayedRows:N0} filtrovaných výsledků z {_allRows.Count:N0}"
            : $"Zobrazeno posledních: {displayedRows.Count:N0} / {_allRows.Count:N0}";
    }

    private void FilterChanged(object sender, TextChangedEventArgs e)
    {
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void GridPrintVersions_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
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

        TransactionRequested?.Invoke($"QA03 {target}");
    }

    private static string NormalizeForFilter(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();

        // Jedno písmeno ignorujeme, jinak to zbytečně filtruje skoro celý dataset.
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
}