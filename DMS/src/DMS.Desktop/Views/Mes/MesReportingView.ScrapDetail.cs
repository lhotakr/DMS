using ClosedXML.Excel;
using DMS.Desktop.UI;
using DMS.Integration.Mes.Reporting;
using DMS.Integration.Mes.Reporting.Definitions;
using DMS.Integration.Mes.Reporting.Models;
using Microsoft.Win32;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.IO;

namespace DMS.Desktop.Views.Mes;

/// <summary>
/// Self-contained SCRAP_DETAIL integration.
/// No existing MES06 source file needs to be edited: this partial class hooks the
/// existing controls after initialization and keeps counter-preset data in a small
/// sidecar JSON next to the normal MES06 preset file.
/// </summary>
public partial class MesReportingView
{
    private const string ScrapDetailReportCode =
        "SCRAP_DETAIL";

    private const int ScrapDetailMaxRows =
        100000;

    private readonly HashSet<string> _scrapSelectedCounters =
        new(
            new[]
            {
                "Odpad - produkce",
                "Odpad - sklo",
                "Vyrobeno - stroj"
            },
            StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<Mes06CounterCatalogItem> _scrapCounterCatalog =
        Array.Empty<Mes06CounterCatalogItem>();

    private Expander? _scrapCounterExpander;
    private TextBlock? _scrapCounterSummary;
    private bool _scrapUiInitialized;

    protected override void OnInitialized(
        EventArgs e)
    {
        base.OnInitialized(e);

        Loaded +=
            MesReportingView_ScrapDetailLoaded;
    }

    private void MesReportingView_ScrapDetailLoaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -=
            MesReportingView_ScrapDetailLoaded;

        InitializeScrapDetailIntegration();

        Dispatcher.BeginInvoke(
            new Action(
                () =>
                {
                    ApplyScrapCounterPreset(
                        LoadScrapCounterPreset(
                            _activePresetName));

                    UpdateScrapCounterUi();
                }));
    }

    private void InitializeScrapDetailIntegration()
    {
        if (_scrapUiInitialized)
        {
            return;
        }

        _scrapUiInitialized =
            true;

        var selectedCode =
            (CmbReport.SelectedItem
                as MesReportDefinition)
            ?.Code;

        _definitions =
            EnsureScrapDetailDefinition(
                _definitions);

        CmbReport.ItemsSource =
            _definitions;

        CmbReport.SelectedItem =
            _definitions.FirstOrDefault(item =>
                string.Equals(
                    item.Code,
                    selectedCode,
                    StringComparison.OrdinalIgnoreCase))
            ?? _definitions.FirstOrDefault();

        CreateScrapCounterFilterUi();

        CmbReport.SelectionChanged +=
            (_, _) =>
                UpdateScrapCounterUi();

        // Replace only the command entry points. Normal reports are delegated
        // back to the existing MES06 methods.
        BtnLoad.Click -=
            BtnLoad_Click;

        BtnLoad.Click +=
            BtnLoad_ScrapAware_Click;

        BtnToolbarExcel.Click -=
            BtnToolbarExcel_Click;

        BtnToolbarExcel.Click +=
            BtnToolbarExcel_ScrapAware_Click;

        BtnExportExcel.Click -=
            BtnExportExcel_Click;

        BtnExportExcel.Click +=
            BtnExportExcel_ScrapAware_Click;

        // Preset selection must use our loader for SCRAP_DETAIL; otherwise the
        // original MES06 handler would route it through the normal report loader.
        LstReportPresets.SelectionChanged -=
            LstReportPresets_SelectionChanged;

        LstReportPresets.SelectionChanged +=
            LstReportPresets_ScrapAware_SelectionChanged;

        // The ordinary preset is still written by MES06. We only add a sidecar
        // entry containing the selected counter names.
        BtnSavePreset.Click +=
            (_, _) =>
                SaveScrapCounterPreset(
                    _activePresetName);

        UpdateScrapCounterUi();
    }

    private IReadOnlyList<MesReportDefinition> EnsureScrapDetailDefinition(
        IReadOnlyList<MesReportDefinition> definitions)
    {
        if (definitions.Any(IsScrapDetailReport))
        {
            return definitions;
        }

        var result =
            definitions.ToList();

        result.Add(
            new MesReportDefinition
            {
                Code = ScrapDetailReportCode,
                Name = "Roční odpady",
                NameKey = "MES06.Report.ScrapDetail.Name",
                Description =
                    "Detailní čítačový export odpadů pro dlouhé intervaly až za celý rok.",
                DescriptionKey =
                    "MES06.Report.ScrapDetail.Description",
                DataSource = "ScrapDetail",
                MaxRows = ScrapDetailMaxRows,
                Columns =
                {
                    Column("PointInTimeText", "Point in time", 135),
                    Column("WorkcenterCode", "Work center", 105),
                    Column("Item", "Item", 155),
                    Column("ItemDesignation", "Item designation", 220),
                    Column("OrderCode", "Order", 105),
                    Column("ItemGroup", "Item group", 145),
                    Column("Personnel", "Personnel", 180),
                    Column("CounterName", "Counter", 190),
                    Column("Amount", "Amount", 95, "0.###"),
                    Column("Unit", "Unit", 70),
                    Column("Percental", "Percental", 115, "0.000000000000000"),
                    Column("UserText", "User text", 200)
                }
            });

        return result;
    }

    private static MesReportColumnDefinition Column(
        string property,
        string header,
        double width,
        string format = "") =>
        new()
        {
            Property = property,
            Header = header,
            Width = width,
            Format = format
        };

    private static bool IsScrapDetailReport(
        MesReportDefinition? definition) =>
        definition is not null
        && string.Equals(
            definition.Code,
            ScrapDetailReportCode,
            StringComparison.OrdinalIgnoreCase);

    private void CreateScrapCounterFilterUi()
    {
        if (ExpReport.Parent
            is not Panel filterStack)
        {
            return;
        }

        var host =
            new StackPanel();

        _scrapCounterSummary =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        6),
                TextWrapping =
                    TextWrapping.Wrap
            };

        host.Children.Add(
            _scrapCounterSummary);

        var button =
            new Button
            {
                Content = "Vybrat čítače...",
                Height = 30,
                HorizontalContentAlignment =
                    HorizontalAlignment.Left,
                Padding =
                    new Thickness(
                        10,
                        0,
                        10,
                        0)
            };

        button.Click +=
            async (_, _) =>
                await SelectScrapCountersAsync();

        host.Children.Add(
            button);

        var border =
            new Border
            {
                Padding =
                    new Thickness(6),
                BorderThickness =
                    new Thickness(
                        0,
                        1,
                        0,
                        0),
                Child = host
            };

        border.SetResourceReference(
            Border.BorderBrushProperty,
            "DmsBorderBrush");

        _scrapCounterExpander =
            new Expander
            {
                Header = "Čítače",
                IsExpanded = true,
                Content = border,
                Visibility = Visibility.Collapsed,
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        8)
            };

        var index =
            filterStack.Children.IndexOf(
                ExpReport);

        filterStack.Children.Insert(
            Math.Max(
                0,
                index + 1),
            _scrapCounterExpander);

        UpdateScrapCounterSummary();
    }

    private void UpdateScrapCounterUi()
    {
        if (_scrapCounterExpander is null)
        {
            return;
        }

        _scrapCounterExpander.Visibility =
            IsScrapDetailReport(
                CmbReport.SelectedItem
                    as MesReportDefinition)
                ? Visibility.Visible
                : Visibility.Collapsed;

        UpdateScrapCounterSummary();
    }

    private void UpdateScrapCounterSummary()
    {
        if (_scrapCounterSummary is null)
        {
            return;
        }

        _scrapCounterSummary.Text =
            _scrapSelectedCounters.Count == 0
                ? "Všechny čítače"
                : $"{_scrapSelectedCounters.Count:N0} vybraných čítačů";
    }

    private async Task SelectScrapCountersAsync()
    {
        try
        {
            _settings =
                _settingsService.Load(
                    _settingsPath);

            if (_scrapCounterCatalog.Count == 0)
            {
                TxtStatus.Text =
                    "Načítám seznam čítačů...";

                var service =
                    new MesScrapDetailDataService(
                        _settings);

                _scrapCounterCatalog =
                    await service.GetCounterCatalogAsync();
            }

            var dialog =
                new Mes06CounterSelectionWindow(
                    Window.GetWindow(
                        this),
                    _scrapCounterCatalog,
                    _scrapSelectedCounters);

            if (dialog.ShowDialog()
                != true)
            {
                return;
            }

            _scrapSelectedCounters.Clear();

            foreach (var name
                     in dialog.SelectedCounterNames)
            {
                _scrapSelectedCounters.Add(
                    name);
            }

            UpdateScrapCounterSummary();
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 SCRAP_DETAIL counter selector failed.",
                ex);

            DmsMessage.Show(
                ex.Message,
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void BtnLoad_ScrapAware_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsScrapDetailReport(
                CmbReport.SelectedItem
                    as MesReportDefinition))
        {
            BtnLoad_Click(
                sender,
                e);
            return;
        }

        await LoadScrapDetailAsync();
    }

    private async Task LoadScrapDetailAsync()
    {
        var definition =
            CmbReport.SelectedItem
                as MesReportDefinition;

        if (!IsScrapDetailReport(
                definition))
        {
            return;
        }

        var workcenters =
            GetSelectedWorkcenterCodes();

        if (workcenters.Count == 0)
        {
            DmsMessage.Show(
                "Vyber alespoň jedno pracoviště.",
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _settings =
            _settingsService.Load(
                _settingsPath);

        var selectedFromDate =
            (
                DateFrom.SelectedDate
                ?? DateTime.Today
            ).Date;

        var selectedToDate =
            (
                DateTo.SelectedDate
                ?? selectedFromDate.AddDays(1)
            ).Date;

        if (!TryParseMes06Time(
                TimeFrom.Text,
                out var selectedFromTime)
            || !TryParseMes06Time(
                TimeTo.Text,
                out var selectedToTime))
        {
            DmsMessage.Show(
                "Čas musí být ve formátu HH:mm.",
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (selectedFromTime.HasValue
            != selectedToTime.HasValue)
        {
            DmsMessage.Show(
                "Vyplň oba časy, nebo nech oba prázdné.",
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var from =
            selectedFromDate;

        var to =
            selectedToDate <= selectedFromDate
                ? selectedFromDate.AddDays(1)
                : selectedToDate;

        if (selectedFromTime.HasValue
            && selectedToTime.HasValue)
        {
            from =
                selectedFromDate.Add(
                    selectedFromTime.Value);

            to =
                selectedToDate.Add(
                    selectedToTime.Value);
        }
        else
        {
            try
            {
                var periodService =
                    new MesReportingEnrichmentService(
                        _settings);

                var period =
                    await periodService.GetShiftRelatedPeriodAsync(
                        selectedFromDate,
                        selectedToDate);

                from = period.From;
                to = period.To;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    "SCRAP_DETAIL shift period resolution failed.",
                    ex);
            }
        }

        BtnLoad.IsEnabled = false;
        TxtStatus.Text =
            "Načítám roční report odpadů...";

        try
        {
            var service =
                new MesScrapDetailDataService(
                    _settings);

            var rows =
                await service.GetReportAsync(
                    from,
                    to,
                    workcenters,
                    TxtOrder.Text?.Trim()
                    ?? string.Empty,
                    TxtOperation.Text?.Trim()
                    ?? string.Empty,
                    TxtProduct.Text?.Trim()
                    ?? string.Empty,
                    _scrapSelectedCounters.ToList(),
                    ScrapDetailMaxRows);

            _mes06CounterReportMode = false;

            _currentRows =
                rows
                    .Cast<object>()
                    .ToList();

            GridReport.ItemsSource = null;
            GridReport.Columns.Clear();
            BuildColumns(
                definition!);
            GridReport.ItemsSource =
                _currentRows;

            ChartHost.Content = null;
            ChartBorder.Visibility =
                Visibility.Collapsed;

            UpdateKpis();

            TxtStatus.Text =
                $"Načteno {_currentRows.Count:N0} řádků.";

            _logger.AdminAction(
                "MES06",
                "LoadScrapDetailReport",
                _user,
                $"From={from:O}; To={to:O}; Workcenters={GetSelectedWorkcenterAuditText()}; Counters={(_scrapSelectedCounters.Count == 0 ? "ALL" : string.Join(",", _scrapSelectedCounters))}; Rows={_currentRows.Count}");
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 SCRAP_DETAIL load failed.",
                ex);

            TxtStatus.Text =
                ex.Message;

            DmsMessage.Show(
                ex.Message,
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            BtnLoad.IsEnabled = true;
        }
    }

    private void BtnToolbarExcel_ScrapAware_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (IsScrapDetailReport(
                CmbReport.SelectedItem
                    as MesReportDefinition))
        {
            ExportScrapDetailExcel();
            return;
        }

        BtnToolbarExcel_Click(
            sender,
            e);
    }

    private void BtnExportExcel_ScrapAware_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (IsScrapDetailReport(
                CmbReport.SelectedItem
                    as MesReportDefinition))
        {
            ExportScrapDetailExcel();
            return;
        }

        BtnExportExcel_Click(
            sender,
            e);
    }

    private void ExportScrapDetailExcel()
    {
        var rows =
            _currentRows
                .OfType<Mes06ScrapDetailRecord>()
                .ToList();

        if (rows.Count == 0)
        {
            DmsMessage.Show(
                "Není co exportovat.",
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog =
            new SaveFileDialog
            {
                Filter =
                    "Excel workbook (*.xlsx)|*.xlsx",
                FileName =
                    $"MES06_Scrap_{DateTime.Now:yyyyMMdd-HHmmss}.xlsx"
            };

        if (dialog.ShowDialog()
            != true)
        {
            return;
        }

        try
        {
            using var workbook =
                new XLWorkbook();

            var worksheet =
                workbook.Worksheets.Add(
                    "Counters report");

            var headers =
                new[]
                {
                    "Point in time",
                    "Work center",
                    "Item",
                    "Item designation",
                    "Order",
                    "Item group",
                    "Personnel",
                    "Counter",
                    "Amount",
                    "Unit",
                    "Percental",
                    "User text"
                };

            for (var column = 0;
                 column < headers.Length;
                 column++)
            {
                worksheet.Cell(
                        1,
                        column + 1)
                    .Value =
                    headers[column];
            }

            for (var index = 0;
                 index < rows.Count;
                 index++)
            {
                var item =
                    rows[index];

                var row =
                    index + 2;

                worksheet.Cell(row, 1).Value = item.PointInTimeText;
                worksheet.Cell(row, 2).Value = item.WorkcenterCode;
                worksheet.Cell(row, 3).Value = item.Item;
                worksheet.Cell(row, 4).Value = item.ItemDesignation;
                worksheet.Cell(row, 5).Value = item.OrderCode;
                worksheet.Cell(row, 6).Value = item.ItemGroup;
                worksheet.Cell(row, 7).Value = item.Personnel;
                worksheet.Cell(row, 8).Value = item.CounterName;

                if (item.Amount.HasValue)
                {
                    worksheet.Cell(row, 9).Value = item.Amount.Value;
                }

                worksheet.Cell(row, 10).Value = item.Unit;

                if (item.Percental.HasValue)
                {
                    worksheet.Cell(row, 11).Value = item.Percental.Value;
                }

                worksheet.Cell(row, 12).Value = item.UserText;
            }

            worksheet.Range(1, 1, 1, 12)
                .Style.Font.Bold = true;

            worksheet.Range(1, 1, rows.Count + 1, 12)
                .SetAutoFilter();

            worksheet.SheetView.FreezeRows(1);

            // Fixed widths are intentional: AdjustToContents across 35k+ rows
            // is needlessly expensive.
            double[] widths =
            {
                18, 14, 22, 30, 14, 20,
                28, 28, 12, 9, 20, 24
            };

            for (var i = 0;
                 i < widths.Length;
                 i++)
            {
                worksheet.Column(i + 1).Width =
                    widths[i];
            }

            worksheet.Column(9)
                .Style.NumberFormat.Format =
                "0.###";

            worksheet.Column(11)
                .Style.NumberFormat.Format =
                "0.000000000000000";

            workbook.SaveAs(
                dialog.FileName);

            _logger.AdminAction(
                "MES06",
                "ExportScrapDetailExcel",
                _user,
                $"Rows={rows.Count}; File={dialog.FileName}");

            TxtStatus.Text =
                $"Excel export vytvořen: {dialog.FileName}";

            OfferOpenExportedFile(
                dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 SCRAP_DETAIL Excel export failed.",
                ex);

            DmsMessage.Show(
                ex.Message,
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void LstReportPresets_ScrapAware_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_presetUiUpdating
            || LstReportPresets.SelectedItem
                is not Mes06ReportPreset preset)
        {
            return;
        }

        _activePresetName =
            preset.Name;

        _mes06PresetDocument.DefaultPresetName =
            preset.Name;

        SavePresetDocument();

        ApplyPresetFilters(
            preset);

        ApplyScrapCounterPreset(
            LoadScrapCounterPreset(
                preset.Name));

        RefreshPresetMenu();

        PopupPresets.IsOpen = false;
        BtnPresetMenu.IsChecked = false;

        if (IsScrapDetailReport(
                CmbReport.SelectedItem
                    as MesReportDefinition))
        {
            await LoadScrapDetailAsync();
        }
        else
        {
            await LoadCurrentReportAsync();
        }
    }

    private string ScrapCounterPresetPath =>
        string.IsNullOrWhiteSpace(
            _mes06PresetPath)
            ? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "DMS",
                "MES06",
                "scrap-counter-presets.json")
            : _mes06PresetPath
              + ".scrap-counters.json";

    private IReadOnlyList<string> LoadScrapCounterPreset(
        string presetName)
    {
        if (string.IsNullOrWhiteSpace(
                presetName)
            || !File.Exists(
                ScrapCounterPresetPath))
        {
            return Array.Empty<string>();
        }

        try
        {
            var json =
                File.ReadAllText(
                    ScrapCounterPresetPath);

            var document =
                JsonSerializer.Deserialize<Dictionary<string, List<string>>>(
                    json)
                ?? new Dictionary<string, List<string>>(
                    StringComparer.OrdinalIgnoreCase);

            return document.TryGetValue(
                       presetName,
                       out var values)
                ? values
                : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private void SaveScrapCounterPreset(
        string presetName)
    {
        if (string.IsNullOrWhiteSpace(
                presetName))
        {
            return;
        }

        try
        {
            Dictionary<string, List<string>> document;

            if (File.Exists(
                    ScrapCounterPresetPath))
            {
                document =
                    JsonSerializer.Deserialize<Dictionary<string, List<string>>>(
                        File.ReadAllText(
                            ScrapCounterPresetPath))
                    ?? new Dictionary<string, List<string>>(
                        StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                document =
                    new Dictionary<string, List<string>>(
                        StringComparer.OrdinalIgnoreCase);
            }

            document[presetName] =
                _scrapSelectedCounters
                    .OrderBy(
                        value => value,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

            var directory =
                Path.GetDirectoryName(
                    ScrapCounterPresetPath);

            if (!string.IsNullOrWhiteSpace(
                    directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            File.WriteAllText(
                ScrapCounterPresetPath,
                JsonSerializer.Serialize(
                    document,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 SCRAP_DETAIL counter preset could not be saved.",
                ex);
        }
    }

    private void ApplyScrapCounterPreset(
        IReadOnlyList<string> counterNames)
    {
        if (!IsScrapDetailReport(
                CmbReport.SelectedItem
                    as MesReportDefinition))
        {
            return;
        }

        if (counterNames.Count == 0)
        {
            UpdateScrapCounterSummary();
            return;
        }

        _scrapSelectedCounters.Clear();

        foreach (var name
                 in counterNames)
        {
            if (!string.IsNullOrWhiteSpace(
                    name))
            {
                _scrapSelectedCounters.Add(
                    name.Trim());
            }
        }

        UpdateScrapCounterSummary();
    }
}
