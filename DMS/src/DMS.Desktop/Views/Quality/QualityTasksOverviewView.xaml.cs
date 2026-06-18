using DMS.Core.Quality;
using DMS.Core.Sap;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Quality;

public partial class QualityTasksOverviewView : UserControl
{
    private readonly QualityTaskOverviewService _service;

    private IReadOnlyList<QualityTaskCockpitRow> _allRows =
        Array.Empty<QualityTaskCockpitRow>();

    public event Action<string>? TransactionRequested;
    public QualityTasksOverviewView()
    {
        InitializeComponent();

        CmbCompletionFilter.SelectedIndex = 0;

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

        _service = new QualityTaskOverviewService(
            sapMaterials,
            qualityRepository.LoadPrintVersions());

        LoadData();
    }

    private void LoadData()
    {
        _allRows = _service.BuildRows();

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
            TxtSapFilter.Text.Trim();

        if (!string.IsNullOrWhiteSpace(sapFilter))
        {
            rows = rows.Where(item =>
                item.SapMaterialNumber.Contains(
                    sapFilter,
                    StringComparison.OrdinalIgnoreCase) ||
                item.OldMaterialNumber.Contains(
                    sapFilter,
                    StringComparison.OrdinalIgnoreCase));
        }

        var finalRows = rows.ToList();

        GridTasks.ItemsSource = finalRows;

        TxtStatus.Text =
            $"Zobrazeno úkolů: {finalRows.Count:N0} / celkem: {_allRows.Count:N0}";

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

        ApplyFilter();
    }

    private void BtnReload_Click(
        object sender,
        RoutedEventArgs e)
    {
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

        TransactionRequested?.Invoke(
            $"QA03 {row.FullPrintVersionNumber}");
    }
}