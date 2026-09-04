using ClosedXML.Excel;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Reporting;
using DMS.Integration.Mes.Reporting.Definitions;
using DMS.Integration.Mes.Reporting.Models;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WPF;
using Microsoft.Win32;
using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
    : UserControl
{
    private readonly string _settingsPath;
    private readonly string _definitionsPath;
    private readonly DmsLogger _logger;
    private readonly string _user;
    private readonly Func<string, string> _translate;
    private readonly MesDatabaseSettingsService _settingsService = new();
    private readonly MesReportDefinitionService _definitionService = new();

    private MesDatabaseConnectionSettings _settings = new();
    private IReadOnlyList<MesReportDefinition> _definitions =
        Array.Empty<MesReportDefinition>();

    private IReadOnlyList<object> _currentRows =
        Array.Empty<object>();

    // The former Production-data definition is intentionally rendered as
    // a direct FASTEC counter report in MES06 v1.5.
    private bool _mes06CounterReportMode;

    public MesReportingView(
        string settingsPath,
        string definitionsPath,
        DmsLogger logger,
        string user,
        Func<string, string>? translate = null)
    {
        InitializeComponent();

        _settingsPath =
            settingsPath
            ?? throw new ArgumentNullException(nameof(settingsPath));

        _definitionsPath =
            definitionsPath
            ?? throw new ArgumentNullException(nameof(definitionsPath));

        _logger =
            logger
            ?? throw new ArgumentNullException(nameof(logger));

        _user =
            user ?? string.Empty;

        _translate =
            translate ?? (key => key);

        ApplyLocalization();  
        InitializeDates();
        InitializeLeftFilterPanel();
        InitializeReportToolbar();
        
        LoadDefinitions();
        
        Loaded += MesReportingView_Loaded;
    }

    private string T(
        string key,
        string fallback)
    {
        var translated =
            _translate(key);

        return string.IsNullOrWhiteSpace(translated)
               || string.Equals(
                   translated,
                   key,
                   StringComparison.Ordinal)
            ? fallback
            : translated;
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text =
            T(
                "MES06.Title",
                "MES06 - MES Reporting");

        TxtSubtitle.Text =
            T(
                "MES06.Subtitle",
                "Dynamic read-only reporting directly from the FASTEC analytical SQL layer.");

        LblReport.Text =
            T(
                "MES06.Filter.Report",
                "Report");

        LblFrom.Text =
            T(
                "MES06.Filter.From",
                "From");

        LblTo.Text =
            T(
                "MES06.Filter.To",
                "To");

        var timeHint =
            T(
                "MES06.Filter.TimeHint",
                "Optional time HH:mm. Fill both times for an exact interval.");

        TimeFrom.ToolTip =
            timeHint;

        TimeTo.ToolTip =
            timeHint;

        LblWorkcenter.Text =
            T(
                "MES06.Filter.Workcenter",
                "Workcenter");

        LblOrder.Text =
            T(
                "MES06.Filter.Order",
                "Order");

        LblProduct.Text =
            T(
                "MES06.Filter.Product",
                "Article");

        BtnLoad.Content =
            T(
                "MES06.Button.Load",
                "Load data");

        LblRows.Text =
            T(
                "MES06.Kpi.Rows",
                "Rows");

        LblWorkcenters.Text =
            T(
                "MES06.Kpi.Workcenters",
                "Workcenters");

        LblRefresh.Text =
            T(
                "MES06.Kpi.Refresh",
                "Last refresh");

        BtnReloadDefinitions.Content =
            T(
                "MES06.Button.ReloadDefinitions",
                "Reload definitions");

        BtnExportExcel.Content =
            T(
                "MES06.Button.ExportExcel",
                "Export Excel");

        ApplyFinalReportingLocalization();
    }

    private async void MesReportingView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= MesReportingView_Loaded;

        _settings =
            _settingsService.Load(
                _settingsPath);

        if (!_settings.IsEnabled)
        {
            TxtStatus.Text =
                T(
                    "MES06.Status.Disabled",
                    "MES SQL reporting connection is disabled in MESSET.");

            BtnLoad.IsEnabled = false;
            return;
        }

        try
        {
            await LoadWorkcentersAsync();

            ApplyStartupPresetAfterWorkcenters();
        }
        catch (Exception ex)
        {
            TxtStatus.Text =
                T(
                    "MES06.Status.WorkcenterLoadFailed",
                    "Could not load MES workcenters.");

            _logger.Error(
                "MES06 workcenter load failed.",
                ex);
        }
    }

    private void InitializeDates()
    {
        var now =
            DateTime.Now;

        DateTo.SelectedDate =
            now.Date.AddDays(1);

        DateFrom.SelectedDate =
            now.Date;

        TimeFrom.Text =
            string.Empty;

        TimeTo.Text =
            string.Empty;
    }

    private static bool TryParseMes06Time(
        string? text,
        out TimeSpan? value)
    {
        value =
            null;

        if (string.IsNullOrWhiteSpace(
                text))
        {
            return true;
        }

        var parts =
            text
                .Trim()
                .Split(':');

        if (parts.Length != 2
            || !int.TryParse(
                parts[0],
                out var hours)
            || !int.TryParse(
                parts[1],
                out var minutes)
            || hours < 0
            || hours > 23
            || minutes < 0
            || minutes > 59)
        {
            return false;
        }

        value =
            new TimeSpan(
                hours,
                minutes,
                0);

        return true;
    }

    private void LoadDefinitions()
    {
        var selectedCode =
            (CmbReport.SelectedItem
                as MesReportDefinition)
            ?.Code;

        var loadedDefinitions =
            _definitionService.Load(
                _definitionsPath);

        var visibleDefinitions =
            loadedDefinitions
                .Where(definition =>
                    !string.Equals(
                        definition.DataSource,
                        "Counters",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        _definitions =
            EnsureFinalReportingDefinitions(
                EnsureProductionGraphDefinition(
                    EnsureMachineTimelineDefinition(
                        visibleDefinitions)));

        _definitions =
            MesLoggedOperatorsReportSupport.AddDefinitionIfMissing(
                _definitions);

        LocalizeDefinitions(
            _definitions);

        CmbReport.ItemsSource =
            _definitions;

        var selected =
            _definitions.FirstOrDefault(definition =>
                string.Equals(
                    definition.Code,
                    selectedCode,
                    StringComparison.OrdinalIgnoreCase))
            ?? _definitions.FirstOrDefault();

        CmbReport.SelectedItem =
            selected;

        ApplySelectedDefinition();
    }


    private void LocalizeDefinitions(
        IEnumerable<MesReportDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            if (!string.IsNullOrWhiteSpace(
                    definition.NameKey))
            {
                definition.Name =
                    T(
                        definition.NameKey,
                        definition.Name);
            }

            if (!string.IsNullOrWhiteSpace(
                    definition.DescriptionKey))
            {
                definition.Description =
                    T(
                        definition.DescriptionKey,
                        definition.Description);
            }

            if (definition.Chart is not null &&
                !string.IsNullOrWhiteSpace(
                    definition.Chart.TitleKey))
            {
                definition.Chart.Title =
                    T(
                        definition.Chart.TitleKey,
                        definition.Chart.Title);
            }

            foreach (var column in definition.Columns)
            {
                if (!string.IsNullOrWhiteSpace(
                        column.HeaderKey))
                {
                    column.Header =
                        T(
                            column.HeaderKey,
                            column.Header);
                }
            }

            if (IsProductionReport(
                    definition)
                && !IsLoggedOperatorsReport(
                    definition))
            {
                definition.Name =
                    T(
                        "MES06.Report.Counter.Name",
                        "Counter report");

                definition.Description =
                    T(
                        "MES06.Report.Counter.Description",
                        "FASTEC counter events including Miscellaneous, rejects and user text.");

                if (definition.Chart is not null)
                {
                    definition.Chart.Title =
                        T(
                            "MES06.Report.Counter.ChartTitle",
                            "Counter report");
                }
            }
        }
    }

    private async Task LoadWorkcentersAsync()
    {
        _settings =
            _settingsService.Load(
                _settingsPath);

        var reportingService =
            new MesReportingDataService(
                _settings);

        var workcenters =
            await reportingService
                .GetWorkcentersAsync();

        var enrichmentService =
            new MesReportingEnrichmentService(
                _settings);

        IReadOnlyDictionary<string, IReadOnlyList<string>> groupMap;

        try
        {
            groupMap =
                await enrichmentService
                    .GetWorkcenterGroupsAsync();
        }
        catch (Exception ex)
        {
            groupMap =
                new Dictionary<string, IReadOnlyList<string>>(
                    StringComparer.OrdinalIgnoreCase);

            _logger.Error(
                "MES06 workcenter-group metadata load failed.",
                ex);
        }

        InitializeWorkcenterSelector(
            workcenters,
            groupMap);
    }

    private async void BtnLoad_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadCurrentReportAsync();
    }

    private async Task LoadCurrentReportAsync()
    {
        var definition =
            CmbReport.SelectedItem
                as MesReportDefinition;

        if (definition is null)
        {
            return;
        }

        if (IsProductionGraphReport(
                definition))
        {
            var selectedWorkcenters =
                GetSelectedWorkcenterCodes();

            if (selectedWorkcenters.Count != 1)
            {
                DmsMessage.Show(
                    T(
                        "MES06.ProductionGraph.OneWorkcenterRequired",
                        "Production graph requires exactly one work center."),
                    "MES06",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }
        }

        _settings =
            _settingsService.Load(
                _settingsPath);

        if (!_settings.IsEnabled)
        {
            DmsMessage.Show(
                T(
                    "MES06.Status.Disabled",
                    "MES SQL reporting connection is disabled in MESSET."),
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var selectedFromDate =
            (
                DateFrom.SelectedDate
                ?? DateTime.Today
            ).Date;

        var selectedToDate =
            (
                DateTo.SelectedDate
                ?? selectedFromDate.AddDays(
                    1)
            ).Date;

        if (!TryParseMes06Time(
                TimeFrom.Text,
                out var selectedFromTime)
            || !TryParseMes06Time(
                TimeTo.Text,
                out var selectedToTime))
        {
            DmsMessage.Show(
                T(
                    "MES06.Filter.TimeInvalid",
                    "Time must use HH:mm, for example 08:30."),
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var hasFromTime =
            selectedFromTime.HasValue;

        var hasToTime =
            selectedToTime.HasValue;

        if (hasFromTime !=
            hasToTime)
        {
            DmsMessage.Show(
                T(
                    "MES06.Filter.TimePairRequired",
                    "Fill both From and To times, or leave both empty."),
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var useExactTimeFilter =
            hasFromTime
            && hasToTime;

        var from =
            selectedFromDate;

        var to =
            selectedToDate;

        if (useExactTimeFilter)
        {
            from =
                selectedFromDate
                    .Add(
                        selectedFromTime!.Value);

            to =
                selectedToDate
                    .Add(
                        selectedToTime!.Value);

            if (to <=
                from)
            {
                DmsMessage.Show(
                    T(
                        "MES06.Filter.TimeRangeInvalid",
                        "The To date/time must be later than the From date/time."),
                    "MES06",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }
        }
        else
        {
            if (selectedToDate <=
                selectedFromDate)
            {
                selectedToDate =
                    selectedFromDate.AddDays(
                        1);

                to =
                    selectedToDate;
            }

            // Default MES06 behavior follows FASTEC
            // "Related to: Start of shift" semantics.
            //
            // If optional HH:mm values are entered above, the exact
            // date/time range is used instead and this shift conversion
            // is intentionally skipped.
            try
            {
                var shiftPeriodService =
                    new MesReportingEnrichmentService(
                        _settings);

                var shiftPeriod =
                    await shiftPeriodService
                        .GetShiftRelatedPeriodAsync(
                            selectedFromDate,
                            selectedToDate);

                from =
                    shiftPeriod.From;

                to =
                    shiftPeriod.To;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    $"MES06 shift-related period resolution failed. SelectedFrom={selectedFromDate:O}; SelectedTo={selectedToDate:O}",
                    ex);
            }
        }

        _mes06EffectiveFrom =
            from;

        _mes06EffectiveTo =
            to;

        var filter =
            new MesReportFilter
            {
                From = from,
                To = to,
                // For one selected workcenter let SQL reduce the dataset.
                // For multiple selections load the normal report dataset and
                // apply the selected-code set in the presentation layer.
                WorkcenterCode =
                    GetSingleSelectedWorkcenterCodeForServer(),
                OrderCode =
                    TxtOrder.Text?.Trim()
                    ?? string.Empty,
                ProductCode =
                    TxtProduct.Text?.Trim()
                    ?? string.Empty,
                MaxRows =
                    definition.MaxRows > 0
                        ? definition.MaxRows
                        : _settings.DefaultReportMaxRows
            };

        BtnLoad.IsEnabled = false;
        TxtStatus.Text =
            T(
                "MES06.Status.Loading",
                "Loading MES data...");

        try
        {
            var service =
                new MesReportingDataService(
                    _settings);

            _mes06CounterReportMode =
                IsProductionReport(
                    definition)
                && !IsLoggedOperatorsReport(
                    definition);

            IReadOnlyList<object> loadedRows;
            if (IsLoggedOperatorsReport(
                definition))
            {
                var rows =
                    await service
                        .GetLoggedOperatorsAsync(
                            filter,
                            GetSelectedWorkcenterCodes());

                loadedRows =
                    rows
                        .Cast<object>()
                        .ToList();
            }
            else if (IsOeeReport(
                         definition))
            {
                var finalService =
                    new MesReportingEnrichmentService(
                        _settings);

                var rows =
                    await finalService
                        .GetOeeReportAsync(
                            filter.From,
                            filter.To,
                            GetSelectedWorkcenterCodes(),
                            (CmbShift.SelectedItem as Mes06FilterChoice)?.Code
                                ?? string.Empty,
                            filter.OrderCode,
                            TxtOperation.Text?.Trim()
                                ?? string.Empty,
                            filter.ProductCode);

                loadedRows =
                    rows
                        .Cast<object>()
                        .ToList();
            }
            else if (IsProcessValuesReport(
                         definition))
            {
                var finalService =
                    new MesReportingEnrichmentService(
                        _settings);

                var rows =
                    await finalService
                        .GetProcessValuesAsync(
                            filter.From,
                            filter.To,
                            GetSelectedWorkcenterCodes(),
                            (CmbShift.SelectedItem as Mes06FilterChoice)?.Code
                                ?? string.Empty,
                            filter.OrderCode,
                            TxtOperation.Text?.Trim()
                                ?? string.Empty,
                            filter.ProductCode);

                loadedRows =
                    rows
                        .Cast<object>()
                        .ToList();
            }
            else if (IsProductionGraphReport(
                         definition))
            {
                var graphService =
                    new MesReportingEnrichmentService(
                        _settings);

                var graphRows =
                    await graphService
                        .GetProductionGraphRowsAsync(
                            filter.From,
                            filter.To,
                            GetSelectedWorkcenterCodes()[0],
                            filter.OrderCode,
                            TxtOperation.Text?.Trim()
                            ?? string.Empty,
                            filter.ProductCode);

                loadedRows =
                    graphRows
                        .Cast<object>()
                        .ToList();
            }
            else if (_mes06CounterReportMode)
            {
                var counterService =
                    new MesReportingEnrichmentService(
                        _settings);

                var counterRows =
                    await counterService
                        .GetCounterReportAsync(
                            filter.From,
                            filter.To,
                            GetSelectedWorkcenterCodes(),
                            filter.OrderCode,
                            TxtOperation.Text?.Trim()
                            ?? string.Empty,
                            filter.ProductCode,
                            filter.MaxRows);

                loadedRows =
                    counterRows
                        .Cast<object>()
                        .ToList();
            }
            else
            {
                loadedRows =
                    await LoadRowsAsync(
                        service,
                        definition,
                        filter);

                loadedRows =
                    ApplySelectedWorkcenterFilter(
                        loadedRows);
            }

            if (!IsLoggedOperatorsReport(
                    definition)
                && !IsOeeReport(
                    definition)
                && !IsProcessValuesReport(
                    definition))
            {
                await LoadProductionEnrichmentAsync(
                    definition,
                    filter,
                    loadedRows);
            }

            RefreshShiftChoices(
                loadedRows);

            ApplyPendingPresetShift();

            if (IsLoggedOperatorsReport(
        definition))
            {
                var selectedShift =
                    (CmbShift.SelectedItem
                        as Mes06FilterChoice)
                    ?.Code
                    ?? string.Empty;

                _currentRows =
                    loadedRows
                        .OfType<MesLoggedOperatorRecord>()
                        .Where(row =>
                            string.IsNullOrWhiteSpace(
                                selectedShift)
                            || string.Equals(
                                row.Shift,
                                selectedShift,
                                StringComparison.OrdinalIgnoreCase))
                        .Cast<object>()
                        .ToList();
            }
            else if (IsProcessValuesReport(
                         definition))
            {
                UpdateProcessValueStateChoices(
                    loadedRows);

                _currentRows =
                    ApplyProcessValueStateFilter(
                        loadedRows);
            }
            else if (IsOeeReport(
                         definition))
            {
                _currentRows =
                    loadedRows;
            }
            else
            {
                _currentRows =
                    ApplyProductionLeftFilters(
                        definition,
                        loadedRows);
            }

            ResetOeeReportPresentation();
            ResetProcessValuesPresentation();
            ResetProductionGraphPresentation();
            ResetMachineTimelinePresentation();

            ApplyGridPresentation(
                definition);

            if (_mes06CounterReportMode)
            {
                ChartHost.Content = null;
                ChartBorder.Visibility =
                    Visibility.Collapsed;
            }
            else if (!IsMachineTimelineReport(
                         definition)
                     && !IsProductionGraphReport(
                         definition)
                     && !IsOeeReport(
                         definition)
                     && !IsProcessValuesReport(
                         definition))
            {
                BuildChart(
                    definition);
            }

            UpdateKpis();

            TxtStatus.Text =
                string.Format(
                    T(
                        "MES06.Status.Loaded",
                        "Loaded {0} rows."),
                    _currentRows.Count);

            _logger.AdminAction(
                "MES06",
                "LoadMesReport",
                _user,
                $"Report={definition.Code}; SelectedFrom={selectedFromDate:yyyy-MM-dd}; SelectedTo={selectedToDate:yyyy-MM-dd}; PeriodMode={(useExactTimeFilter ? "ExactTime" : "ShiftStart")}; SelectedTimeFrom={(selectedFromTime.HasValue ? selectedFromTime.Value.ToString() : "")}; SelectedTimeTo={(selectedToTime.HasValue ? selectedToTime.Value.ToString() : "")}; EffectiveFrom={filter.From:O}; EffectiveTo={filter.To:O}; Workcenters={GetSelectedWorkcenterAuditText()}; Shift={(CmbShift.SelectedItem as Mes06FilterChoice)?.Code}; Order={filter.OrderCode}; Product={filter.ProductCode}; Operation={TxtOperation.Text?.Trim()}; Rows={_currentRows.Count}");
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"MES06 report load failed. Report={definition.Code}",
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

    private static async Task<IReadOnlyList<object>> LoadRowsAsync(
        IMesReportingDataService service,
        MesReportDefinition definition,
        MesReportFilter filter)
    {

        if (string.Equals(
                definition.DataSource,
                "States",
                StringComparison.OrdinalIgnoreCase))
        {
            var rows =
                await service
                    .GetStatesAsync(filter);

            return rows.Cast<object>().ToList();
        }

        if (string.Equals(
                definition.DataSource,
                "Counters",
                StringComparison.OrdinalIgnoreCase))
        {
            var rows =
                await service
                    .GetCountersAsync(filter);

            return rows.Cast<object>().ToList();
        }

        var production =
            await service
                .GetProductionAsync(filter);

        return production.Cast<object>().ToList();
    }

    private void BuildColumns(
        MesReportDefinition definition)
    {
        GridReport.Columns.Clear();

        foreach (var column
                 in definition.Columns)
        {
            if (string.IsNullOrWhiteSpace(
                    column.Property))
            {
                continue;
            }

            var binding =
                new Binding(
                    column.Property);

            if (!string.IsNullOrWhiteSpace(
                    column.Format))
            {
                binding.StringFormat =
                    column.Format;
            }

            GridReport.Columns.Add(
                new DataGridTextColumn
                {
                    Header =
                        column.Header,
                    Binding =
                        binding,
                    Width =
                        new DataGridLength(
                            Math.Max(
                                60d,
                                column.Width))
                });
        }
    }

    private void BuildChart(
        MesReportDefinition definition)
    {
        ChartHost.Content = null;

        if (definition.Chart is null ||
            _currentRows.Count == 0)
        {
            ChartBorder.Visibility =
                Visibility.Collapsed;

            return;
        }

        var chartDefinition =
            definition.Chart;

        var groups =
            AggregateChart(
                    _currentRows,
                    chartDefinition)
                .Take(
                    chartDefinition.Top)
                .ToList();

        if (groups.Count == 0)
        {
            ChartBorder.Visibility =
                Visibility.Collapsed;

            return;
        }

        var labels =
            groups
                .Select(item => item.Label)
                .ToArray();

        var values =
            groups
                .Select(item => item.Value)
                .ToArray();

        ISeries series =
            string.Equals(
                chartDefinition.Kind,
                "Line",
                StringComparison.OrdinalIgnoreCase)
                ? new LineSeries<double>
                {
                    Values = values
                }
                : new ColumnSeries<double>
                {
                    Values = values
                };

        var chart =
            new CartesianChart
            {
                Series =
                    new[]
                    {
                        series
                    },
                XAxes =
                    new[]
                    {
                        new Axis
                        {
                            Labels = labels,
                            LabelsRotation = 15
                        }
                    },
                YAxes =
                    new[]
                    {
                        new Axis
                        {
                            MinLimit = 0
                        }
                    }
            };

        TxtChartTitle.Text =
            chartDefinition.Title;

        ChartHost.Content =
            chart;

        ChartBorder.Visibility =
            Visibility.Visible;
    }

    private static IEnumerable<(string Label, double Value)> AggregateChart(
        IReadOnlyList<object> rows,
        MesChartDefinition chart)
    {
        if (string.IsNullOrWhiteSpace(
                chart.GroupBy) ||
            string.IsNullOrWhiteSpace(
                chart.Measure))
        {
            yield break;
        }

        var grouped =
            rows
                .Select(row =>
                    new
                    {
                        Label =
                            Convert.ToString(
                                ReadProperty(
                                    row,
                                    chart.GroupBy))
                            ?? string.Empty,
                        Value =
                            ToDouble(
                                ReadProperty(
                                    row,
                                    chart.Measure))
                    })
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.Label))
                .GroupBy(
                    item => item.Label,
                    StringComparer.CurrentCultureIgnoreCase)
                .Select(group =>
                    new
                    {
                        Label = group.Key,
                        Value =
                            string.Equals(
                                chart.Aggregation,
                                "Average",
                                StringComparison.OrdinalIgnoreCase)
                                ? group.Average(item => item.Value)
                                : group.Sum(item => item.Value)
                    })
                .OrderByDescending(item =>
                    item.Value)
                .ToList();

        foreach (var item in grouped)
        {
            yield return (
                item.Label,
                item.Value);
        }
    }

    private static object? ReadProperty(
        object source,
        string propertyName)
    {
        return source
            .GetType()
            .GetProperty(
                propertyName,
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.IgnoreCase)
            ?.GetValue(source);
    }

    private static double ToDouble(
        object? value)
    {
        if (value is null)
        {
            return 0d;
        }

        try
        {
            return Convert.ToDouble(value);
        }
        catch
        {
            return 0d;
        }
    }

    private void UpdateKpis()
    {
        TxtRows.Text =
            _currentRows.Count.ToString("N0");

        var workcenters =
            _currentRows
                .Select(row =>
                    Convert.ToString(
                        ReadProperty(
                            row,
                            "WorkcenterCode")))
                .Where(value =>
                    !string.IsNullOrWhiteSpace(value))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count();

        TxtWorkcenters.Text =
            workcenters.ToString("N0");

        TxtRefresh.Text =
            DateTime.Now.ToString(
                "dd.MM.yyyy HH:mm:ss");
    }

    private void ApplySelectedDefinition()
    {
        var definition =
            CmbReport.SelectedItem
                as MesReportDefinition;

        TxtReportDescription.Text =
            definition?.Description
            ?? string.Empty;

        if (definition is null
            || definition.Chart is null
            || IsProductionReport(
                definition))
        {
            ChartBorder.Visibility =
                Visibility.Collapsed;
        }
    }

    private void CmbReport_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        ApplySelectedDefinition();

        var definition =
            CmbReport.SelectedItem
                as MesReportDefinition;

        UpdateProductionGraphWorkcenterMode(
            definition);

        UpdateFinalReportingFilterMode(
            definition);
    }

    private void BtnReloadDefinitions_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadDefinitions();

        TxtStatus.Text =
            T(
                "MES06.Status.DefinitionsReloaded",
                "Report definitions reloaded.");
    }


    private static void SetExcelCellValue(
        IXLCell cell,
        object? value)
    {
        switch (value)
        {
            case null:
                cell.Value = string.Empty;
                break;

            case DateTime dateTime:
                cell.Value = dateTime;
                break;

            case DateTimeOffset dateTimeOffset:
                cell.Value = dateTimeOffset.DateTime;
                break;

            case decimal decimalValue:
                cell.Value = decimalValue;
                break;

            case double doubleValue:
                cell.Value = doubleValue;
                break;

            case float floatValue:
                cell.Value = Convert.ToDouble(floatValue);
                break;

            case int intValue:
                cell.Value = intValue;
                break;

            case long longValue:
                cell.Value = longValue;
                break;

            case bool boolValue:
                cell.Value = boolValue;
                break;

            default:
                cell.Value =
                    Convert.ToString(value)
                    ?? string.Empty;
                break;
        }
    }

    private void BtnExportExcel_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_mes06CounterReportMode)
        {
            ExportCounterReportExcel();
            return;
        }

        if (_currentRows.Count == 0)
        {
            DmsMessage.Show(
                T(
                    "MES06.Status.NoData",
                    "There is no report data to export."),
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var definition =
            CmbReport.SelectedItem
                as MesReportDefinition;

        if (definition is null)
        {
            return;
        }

        if (IsOeeReport(
                definition)
            || IsProcessValuesReport(
                definition))
        {
            ExportFinalReportingVisibleGridExcel(
                definition);

            return;
        }

        var dialog =
            new SaveFileDialog
            {
                Filter =
                    "Excel workbook (*.xlsx)|*.xlsx",
                FileName =
                    $"MES06_{definition.Code}_{DateTime.Now:yyyyMMdd-HHmmss}.xlsx"
            };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            using var workbook =
                new XLWorkbook();

            var worksheet =
                workbook.Worksheets.Add(
                    "MES Report");

            for (var columnIndex = 0;
                 columnIndex < definition.Columns.Count;
                 columnIndex++)
            {
                worksheet.Cell(
                        1,
                        columnIndex + 1)
                    .Value =
                    definition.Columns[columnIndex]
                        .Header;
            }

            for (var rowIndex = 0;
                 rowIndex < _currentRows.Count;
                 rowIndex++)
            {
                var row =
                    _currentRows[rowIndex];

                for (var columnIndex = 0;
                     columnIndex < definition.Columns.Count;
                     columnIndex++)
                {
                    var value =
                        ReadProperty(
                            row,
                            definition.Columns[columnIndex]
                                .Property);

                    SetExcelCellValue(
                        worksheet.Cell(
                            rowIndex + 2,
                            columnIndex + 1),
                        value);
                }
            }

            worksheet
                .ColumnsUsed()
                .AdjustToContents();

            workbook.SaveAs(
                dialog.FileName);

            _logger.AdminAction(
                "MES06",
                "ExportMesReportExcel",
                _user,
                $"Report={definition.Code}; Rows={_currentRows.Count}; File={dialog.FileName}");

            TxtStatus.Text =
                string.Format(
                    T(
                        "MES06.Status.Exported",
                        "Excel export created: {0}"),
                    dialog.FileName);


            OfferOpenExportedFile(
                dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 Excel export failed.",
                ex);

            DmsMessage.Show(
                ex.Message,
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
    private static bool IsLoggedOperatorsReport(
    MesReportDefinition? definition)
    {
        return definition is not null
               && string.Equals(
                   definition.Code,
                   MesLoggedOperatorsReportSupport.ReportCode,
                   StringComparison.OrdinalIgnoreCase);
    }
}
