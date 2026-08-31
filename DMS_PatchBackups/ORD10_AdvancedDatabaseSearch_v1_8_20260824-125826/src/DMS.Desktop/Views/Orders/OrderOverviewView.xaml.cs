using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Orders;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace DMS.Desktop.Views.Orders;

public partial class OrderOverviewView : UserControl
{
    public sealed class SimpleFilterOption
    {
        public SimpleFilterOption(
            int? value,
            string code,
            string description)
        {
            Value = value;
            Code = code;
            DisplayText = value.HasValue
                ? $"{code} ({description})"
                : description;
        }

        public int? Value { get; }
        public string Code { get; }
        public string DisplayText { get; }

        public override string ToString() => DisplayText;
    }

    private sealed class StatusFilterOption
    {
        public StatusFilterOption(
            string code,
            string description)
        {
            Code = code;
            DisplayText = $"{code} ({description})";
        }

        public string Code { get; }
        public string DisplayText { get; }

        public override string ToString() => DisplayText;
    }

    private static readonly IReadOnlyList<StatusFilterOption> MesStatusOptions =
        new[]
        {
            new StatusFilterOption("CRTD", "Created"),
            new StatusFilterOption("REL", "Released"),
            new StatusFilterOption("PREL", "Partially released"),
            new StatusFilterOption("RWDN", "Release withdrawn"),
            new StatusFilterOption("CCLD", "Canceled"),
            new StatusFilterOption("UCPL", "Uncompleted"),
            new StatusFilterOption("CLSD", "Closed")
        };

    private readonly string _configurationRootPath;
    private readonly DmsLogger _logger;
    private readonly string _user;
    private readonly Action<string> _openTechnology;
    private readonly Func<string, string> _translate;
    private readonly MesDatabaseSettingsService _settingsService = new();

    private MesOrderOverviewDataService? _service;
    private List<MesProductionOrderRecord> _allOrders = new();
    private ICollectionView? _ordersView;

    private readonly DispatcherTimer _searchDebounceTimer =
        new()
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };

    private readonly HashSet<string> _selectedStatusCodes =
        new(StringComparer.OrdinalIgnoreCase);

    private string _searchText = string.Empty;

    // Detail loading is intentionally serialized. At most ONE operation SQL
    // query may be in flight; newer selections replace the pending request.
    private MesProductionOrderRecord? _pendingOperationOrder;
    private bool _operationWorkerRunning;
    private long _operationSelectionVersion;

    private ScrollViewer? _hostScrollViewer;

    private bool _loaded;
    private bool _loadingOrders;
    private bool _suppressStatusRefresh;

    public OrderOverviewView(
        string configurationRootPath,
        DmsLogger logger,
        string user,
        Action<string> openTechnology,
        Func<string, string>? translate = null)
    {
        InitializeComponent();

        _configurationRootPath =
            configurationRootPath
            ?? throw new ArgumentNullException(nameof(configurationRootPath));

        _logger =
            logger
            ?? throw new ArgumentNullException(nameof(logger));

        _user = user ?? string.Empty;
        _openTechnology =
            openTechnology
            ?? throw new ArgumentNullException(nameof(openTechnology));

        _translate =
            translate ?? (key => key);

        ApplyLocalization();
        ConfigureAdditionalFilters();

        _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

        Loaded += OrderOverviewView_Loaded;
        Unloaded += OrderOverviewView_Unloaded;
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
               || string.Equals(
                   translated,
                   $"[[{key}]]",
                   StringComparison.Ordinal)
            ? fallback
            : translated;
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text =
            T(
                "ORD10.Title",
                "ORD10 - Přehled zakázek");

        TxtSubtitle.Text =
            T(
                "ORD10.Subtitle",
                "Read-only přehled výrobních zakázek FASTEC. Vyber zakázku a dole se zobrazí její operace.");

        LblStatus.Text =
            T(
                "ORD10.Filter.Status",
                "Status");

        LblErrorStatus.Text =
            T(
                "ORD10.Filter.ErrorStatus",
                "Chyba");

        LblPlanningStatus.Text =
            T(
                "ORD10.Filter.PlanningStatus",
                "Plánování");

        LblFixationStatus.Text =
            T(
                "ORD10.Filter.FixationStatus",
                "Fixace");

        LblProductionStatus.Text =
            T(
                "ORD10.Filter.ProductionStatus",
                "Výroba");

        LblSearch.Text =
            T(
                "ORD10.Filter.Search",
                "Hledat");

        BtnRefresh.Content =
            T(
                "ORD10.Button.Refresh",
                "Obnovit");

        BtnSelectAllStatuses.Content =
            T(
                "ORD10.Button.SelectAll",
                "Vybrat vše");

        BtnClearStatuses.Content =
            T(
                "ORD10.Button.Clear",
                "Zrušit výběr");

        ColOrder.Header =
            T(
                "ORD10.Column.Order",
                "Zakázka");

        ColStatus.Header =
            T(
                "ORD10.Column.Status",
                "Status");

        ColProgress.Header =
            T(
                "ORD10.Column.Progress",
                "Průběh");

        ColProduct.Header =
            T(
                "ORD10.Column.Product",
                "Artikl");

        ColSap.Header =
            T(
                "ORD10.Column.SapArticle",
                "SAP číslo");

        ColTarget.Header =
            T(
                "ORD10.Column.Target",
                "Cíl");

        ColFinished.Header =
            T(
                "ORD10.Column.Finished",
                "Hotovo");

        ColScrap.Header =
            T(
                "ORD10.Column.Scrap",
                "NOK");

        ColPlannedStart.Header =
            T(
                "ORD10.Column.PlannedStart",
                "Plán od");

        ColPlannedEnd.Header =
            T(
                "ORD10.Column.PlannedEnd",
                "Plán do");

        ColOperations.Header =
            T(
                "ORD10.Column.OperationCount",
                "Operací");

        OpColOperation.Header =
            T(
                "ORD10.Operation.Column.Operation",
                "Operace");

        OpColStatus.Header =
            T(
                "ORD10.Operation.Column.Status",
                "Status");

        OpColWorkcenter.Header =
            T(
                "ORD10.Operation.Column.Workcenter",
                "Pracoviště");

        OpColTarget.Header =
            T(
                "ORD10.Operation.Column.Target",
                "Cíl");

        OpColFinished.Header =
            T(
                "ORD10.Operation.Column.Finished",
                "Hotovo");

        OpColScrap.Header =
            T(
                "ORD10.Operation.Column.Scrap",
                "NOK");

        OpColActualStart.Header =
            T(
                "ORD10.Operation.Column.ActualStart",
                "Skutečný start");

        OpColActualEnd.Header =
            T(
                "ORD10.Operation.Column.ActualEnd",
                "Skutečný konec");

        OpColDescription.Header =
            T(
                "ORD10.Operation.Column.Description",
                "Popis");

        TxtOperationsTitle.Text =
            T(
                "ORD10.Operations.None",
                "Operace - vyber zakázku");
    }

    private void ConfigureAdditionalFilters()
    {
        CmbErrorStatus.ItemsSource =
            new[]
            {
                new SimpleFilterOption(null, "ALL", "Vše"),
                new SimpleFilterOption(0, "NERR", "Not erroneous"),
                new SimpleFilterOption(1, "ERRO", "Erroneous")
            };

        CmbPlanningStatus.ItemsSource =
            new[]
            {
                new SimpleFilterOption(null, "ALL", "Vše"),
                new SimpleFilterOption(0, "NPLD", "Not planned"),
                new SimpleFilterOption(1, "PPLD", "Partly planned"),
                new SimpleFilterOption(2, "PLND", "Planned")
            };

        CmbFixationStatus.ItemsSource =
            new[]
            {
                new SimpleFilterOption(null, "ALL", "Vše"),
                new SimpleFilterOption(0, "NFIX", "Not fixed"),
                new SimpleFilterOption(1, "PFIX", "Partially fixed"),
                new SimpleFilterOption(2, "FIX", "Fixed")
            };

        CmbProductionStatus.ItemsSource =
            new[]
            {
                new SimpleFilterOption(null, "ALL", "Vše"),
                new SimpleFilterOption(0, "NPRO", "Not in production"),
                new SimpleFilterOption(1, "PROD", "In production"),
                new SimpleFilterOption(2, "PFIN", "Production finished")
            };

        CmbErrorStatus.SelectedIndex = 0;
        CmbPlanningStatus.SelectedIndex = 0;
        CmbFixationStatus.SelectedIndex = 0;
        CmbProductionStatus.SelectedIndex = 0;
    }

    private static int? SelectedFilterValue(
        ComboBox comboBox)
    {
        return (comboBox.SelectedItem
                as SimpleFilterOption)
            ?.Value;
    }

    private void OrderOverviewView_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        _searchDebounceTimer.Stop();
        _pendingOperationOrder = null;
        _operationSelectionVersion++;

        if (_hostScrollViewer is not null)
        {
            _hostScrollViewer.SizeChanged -= HostScrollViewer_SizeChanged;
            _hostScrollViewer = null;
        }

        ClearValue(HeightProperty);
        ClearValue(MaxHeightProperty);

        // Detach the view filter so the CollectionView cannot unnecessarily
        // retain this UserControl through the filter delegate.
        if (_ordersView is not null)
        {
            _ordersView.Filter = null;
        }
    }

    private void AttachToHostViewport()
    {
        _hostScrollViewer =
            FindVisualAncestor<ScrollViewer>(
                this);

        if (_hostScrollViewer is null)
        {
            // If MainWindow is ever changed to a finite Grid host, the normal
            // star-sized layout already works and no override is necessary.
            return;
        }

        _hostScrollViewer.SizeChanged += HostScrollViewer_SizeChanged;
        UpdateViewportHeight();
    }

    private void HostScrollViewer_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        UpdateViewportHeight();
    }

    private void UpdateViewportHeight()
    {
        if (_hostScrollViewer is null)
        {
            return;
        }

        var viewportHeight =
            _hostScrollViewer.ViewportHeight;

        if (double.IsNaN(viewportHeight)
            || double.IsInfinity(viewportHeight)
            || viewportHeight <= 0)
        {
            viewportHeight =
                _hostScrollViewer.ActualHeight;
        }

        if (viewportHeight <= 0)
        {
            return;
        }

        // The host ScrollViewer otherwise measures ORD10 with infinite height,
        // which forces GridOrders to realize hundreds of WPF rows and pushes
        // the operation detail below the entire order list.
        var targetHeight =
            Math.Max(
                520d,
                viewportHeight - 12d);

        Height =
            targetHeight;

        MaxHeight =
            targetHeight;
    }

    private static T? FindVisualAncestor<T>(
        DependencyObject child)
        where T : DependencyObject
    {
        DependencyObject? current =
            child;

        while (current is not null)
        {
            current =
                VisualTreeHelper.GetParent(
                    current);

            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private async void OrderOverviewView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        AttachToHostViewport();

        _loaded = true;

        try
        {
            var settingsPath =
                ResolveMesDatabaseSettingsPath(
                    _configurationRootPath);

            var settings =
                _settingsService.Load(
                    settingsPath);

            if (!settings.IsEnabled)
            {
                TxtStatus.Text =
                    T(
                        "ORD10.Status.Disabled",
                        "MES SQL připojení je v MESSET vypnuté.");

                BtnRefresh.IsEnabled = false;
                return;
            }

            _service =
                new MesOrderOverviewDataService(
                    settings);

            await LoadOrdersAsync(
                showDialogOnError: true);
        }
        catch (Exception ex)
        {
            _logger.Error(
                "ORD10 initialization failed.",
                ex);

            TxtStatus.Text = ex.Message;

            DmsMessage.Show(
                $"{T("ORD10.Status.LoadFailed", "Načtení zakázek FASTEC selhalo.")}\n\n{ex.Message}",
                "ORD10",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task LoadOrdersAsync(
        bool showDialogOnError)
    {
        if (_service is null
            || _loadingOrders)
        {
            return;
        }

        _operationSelectionVersion++;
        GridOperations.ItemsSource = null;

        _loadingOrders = true;
        BtnRefresh.IsEnabled = false;

        try
        {
            TxtStatus.Text =
                T(
                    "ORD10.Status.Loading",
                    "Načítám výrobní zakázky...");

            var filter =
                new MesOrderOverviewFilter
                {
                    // ORD10 v1.3 loads non-archived orders once.
                    // Search and MES status filtering are client-side.
                    SearchText = string.Empty,
                    MaxRows = 500
                };

            var rows =
                await _service.GetOrdersAsync(
                    filter);

            if (_ordersView is not null)
            {
                _ordersView.Filter = null;
            }

            _allOrders =
                rows.ToList();

            ReloadStatusFilter(
                _allOrders);

            _ordersView =
                CollectionViewSource.GetDefaultView(
                    _allOrders);

            _ordersView.Filter =
                OrderMatchesCurrentFilter;

            GridOrders.ItemsSource =
                _ordersView;

            ApplyClientFilter();

            _logger.Info(
                $"ORD10 MES non-archived orders loaded; user={_user}; rows={_allOrders.Count}");
        }
        catch (Exception ex)
        {
            _logger.Error(
                "ORD10 order load failed.",
                ex);

            TxtStatus.Text = ex.Message;

            if (showDialogOnError)
            {
                DmsMessage.Show(
                    ex.Message,
                    "ORD10",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            BtnRefresh.IsEnabled = true;
            _loadingOrders = false;
        }
    }

    private void ReloadStatusFilter(
        IReadOnlyList<MesProductionOrderRecord> rows)
    {
        var previous =
            LstStatuses.SelectedItems
                .Cast<StatusFilterOption>()
                .Select(item => item.Code)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        _suppressStatusRefresh = true;

        try
        {
            LstStatuses.ItemsSource =
                MesStatusOptions;

            foreach (var option in MesStatusOptions)
            {
                if (previous.Count == 0
                    || previous.Contains(option.Code))
                {
                    LstStatuses.SelectedItems.Add(
                        option);
                }
            }
        }
        finally
        {
            _suppressStatusRefresh = false;
        }

        UpdateStatusFilterText();
    }

    private void ApplyClientFilter()
    {
        _selectedStatusCodes.Clear();

        foreach (var option
                 in LstStatuses.SelectedItems
                     .Cast<StatusFilterOption>())
        {
            _selectedStatusCodes.Add(
                option.Code);
        }

        _searchText =
            TxtSearch.Text?.Trim()
            ?? string.Empty;

        _pendingOperationOrder = null;
        _operationSelectionVersion++;
        GridOperations.ItemsSource = null;

        // Force a real selection transition. Without this, filtering could
        // keep index 0 selected while the detail grid had just been cleared,
        // so SelectionChanged did not fire and operations stayed empty.
        GridOrders.SelectedIndex = -1;

        _ordersView?.Refresh();

        var resultCount =
            GridOrders.Items.Count;

        // Do not auto-select the first row. Filtering/status changes should
        // never start a detail SQL request by themselves. The user explicitly
        // selects the order whose operations should be loaded.
        TxtOperationsTitle.Text =
            T(
                "ORD10.Operations.None",
                "Operace - vyber zakázku");

        TxtStatus.Text =
            string.Format(
                T(
                    "ORD10.Status.Loaded",
                    "Zobrazeno {0} z posledních 500 nearchivovaných zakázek."),
                resultCount);

        UpdateStatusFilterText();
    }

    private bool OrderMatchesCurrentFilter(
        object item)
    {
        if (item is not MesProductionOrderRecord order)
        {
            return false;
        }

        if (_selectedStatusCodes.Count == 0
            || !_selectedStatusCodes.Contains(
                order.GeneralStatusCode))
        {
            return false;
        }

        var errorStatus =
            SelectedFilterValue(
                CmbErrorStatus);

        if (errorStatus.HasValue
            && order.FailureStatus != errorStatus.Value)
        {
            return false;
        }

        var planningStatus =
            SelectedFilterValue(
                CmbPlanningStatus);

        if (planningStatus.HasValue
            && order.PlanningStatus != planningStatus.Value)
        {
            return false;
        }

        var fixationStatus =
            SelectedFilterValue(
                CmbFixationStatus);

        if (fixationStatus.HasValue
            && order.PlanningFixStatus != fixationStatus.Value)
        {
            return false;
        }

        var productionStatus =
            SelectedFilterValue(
                CmbProductionStatus);

        if (productionStatus.HasValue
            && order.ProductionStatus != productionStatus.Value)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(
                _searchText))
        {
            return true;
        }

        return ContainsIgnoreCase(order.OrderCode, _searchText)
               || ContainsIgnoreCase(order.ProductCode, _searchText)
               || ContainsIgnoreCase(order.ProductDescription, _searchText)
               || ContainsIgnoreCase(order.SapArticleNumber, _searchText);
    }

    private static bool ContainsIgnoreCase(
        string? source,
        string search)
    {
        return !string.IsNullOrEmpty(source)
               && source.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateStatusFilterText()
    {
        var count =
            LstStatuses.SelectedItems.Count;

        var total =
            LstStatuses.Items.Count;

        if (count == total
            && total > 0)
        {
            BtnStatusFilter.Content =
                T(
                    "ORD10.Filter.AllStatuses",
                    $"Všechny statusy ({total})");

            return;
        }

        if (count == 0)
        {
            BtnStatusFilter.Content =
                T(
                    "ORD10.Filter.NoStatuses",
                    "Žádný status");

            return;
        }

        var selected =
            LstStatuses.SelectedItems
                .Cast<StatusFilterOption>()
                .Select(item => item.Code)
                .Take(3)
                .ToList();

        var suffix =
            count > 3
                ? $" +{count - 3}"
                : string.Empty;

        BtnStatusFilter.Content =
            string.Join(
                ", ",
                selected)
            + suffix;
    }

    private async void GridOrders_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        var selectionVersion =
            ++_operationSelectionVersion;

        GridOperations.ItemsSource = null;

        if (GridOrders.SelectedItem
            is not MesProductionOrderRecord order)
        {
            _pendingOperationOrder = null;
            TxtOperationsTitle.Text =
                T(
                    "ORD10.Operations.None",
                    "Operace - vyber zakázku");
            return;
        }

        _pendingOperationOrder =
            order;

        TxtOperationsTitle.Text =
            string.Format(
                T(
                    "ORD10.Operations.Title",
                    "Operace zakázky {0}"),
                order.OrderCode);

        if (!_operationWorkerRunning)
        {
            try
            {
                await RunOperationWorkerAsync();
            }
            catch (Exception ex)
            {
                // Final safety net: no ORD10 detail exception should escape
                // into the global DispatcherUnhandledException handler.
                _logger.Error(
                    $"ORD10 detail worker failed. Order={order.OrderCode}",
                    ex);

                TxtStatus.Text =
                    $"Detail zakázky {order.OrderCode} se nepodařilo načíst: {ex.Message}";
            }
        }
    }

    private async Task RunOperationWorkerAsync()
    {
        if (_operationWorkerRunning
            || _service is null)
        {
            return;
        }

        _operationWorkerRunning = true;

        try
        {
            while (_pendingOperationOrder
                   is MesProductionOrderRecord order)
            {
                _pendingOperationOrder = null;

                var version =
                    _operationSelectionVersion;

                try
                {
                    var rows =
                        await _service.GetOperationsAsync(
                            order.Id,
                            order.OrderCode);

                    if (version != _operationSelectionVersion)
                    {
                        continue;
                    }

                    if (GridOrders.SelectedItem
                            is not MesProductionOrderRecord selectedOrder
                        || selectedOrder.Id != order.Id)
                    {
                        continue;
                    }

                    GridOperations.ItemsSource =
                        rows;
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        $"ORD10 operation load failed. Order={order.OrderCode}; SelectionVersion={version}",
                        ex);

                    if (version == _operationSelectionVersion
                        && GridOrders.SelectedItem
                            is MesProductionOrderRecord selectedOrder
                        && selectedOrder.Id == order.Id)
                    {
                        GridOperations.ItemsSource = null;
                        TxtStatus.Text =
                            $"Detail zakázky {order.OrderCode} se nepodařilo načíst: {ex.Message}";
                    }
                }
            }
        }
        finally
        {
            _operationWorkerRunning = false;

            // A pending selection will be picked up by the next explicit
            // SelectionChanged event. Avoid unobserved fire-and-forget tasks.
        }
    }

    private void SapArticle_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button
            || button.DataContext
                is not MesProductionOrderRecord order)
        {
            return;
        }

        var sapNumber =
            order.SapArticleNumber?.Trim()
            ?? string.Empty;

        if (sapNumber.Length != 10
            || !sapNumber.All(char.IsDigit))
        {
            DmsMessage.Show(
                T(
                    "ORD10.Status.InvalidSapArticle",
                    "Zakázka nemá platné desetimístné SAP číslo pro TEC03."),
                "ORD10",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        _openTechnology(
            sapNumber);
    }

    private async void BtnRefresh_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadOrdersAsync(
            showDialogOnError: true);
    }

    private void AdditionalFilter_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_loaded
            || _allOrders.Count == 0)
        {
            return;
        }

        ApplyClientFilter();
    }

    private void TxtSearch_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (!_loaded
            || _allOrders.Count == 0)
        {
            return;
        }

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void SearchDebounceTimer_Tick(
        object? sender,
        EventArgs e)
    {
        _searchDebounceTimer.Stop();
        ApplyClientFilter();
    }

    private void BtnStatusFilter_Click(
        object sender,
        RoutedEventArgs e)
    {
        PopupStatusFilter.IsOpen =
            !PopupStatusFilter.IsOpen;
    }

    private void PopupStatusFilter_Closed(
        object? sender,
        EventArgs e)
    {
        if (_suppressStatusRefresh)
        {
            return;
        }

        ApplyClientFilter();
    }

    private void BtnSelectAllStatuses_Click(
        object sender,
        RoutedEventArgs e)
    {
        _suppressStatusRefresh = true;

        try
        {
            LstStatuses.SelectAll();
        }
        finally
        {
            _suppressStatusRefresh = false;
        }

        UpdateStatusFilterText();
    }

    private void BtnClearStatuses_Click(
        object sender,
        RoutedEventArgs e)
    {
        _suppressStatusRefresh = true;

        try
        {
            LstStatuses.UnselectAll();
        }
        finally
        {
            _suppressStatusRefresh = false;
        }

        UpdateStatusFilterText();
    }

    private static string ResolveMesDatabaseSettingsPath(
        string configurationRootPath)
    {
        var knownNames =
            new[]
            {
                "mes-database-settings.json",
                "mes-reporting-settings.json",
                "mes-sql-settings.json",
                "mes-database.json",
                "mes-reporting.json"
            };

        foreach (var name in knownNames)
        {
            var candidate =
                Path.Combine(
                    configurationRootPath,
                    name);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (Directory.Exists(
                configurationRootPath))
        {
            foreach (var file
                     in Directory.EnumerateFiles(
                         configurationRootPath,
                         "*.json",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    using var document =
                        JsonDocument.Parse(
                            File.ReadAllText(
                                file));

                    if (document.RootElement.ValueKind
                        != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var names =
                        document.RootElement
                            .EnumerateObject()
                            .Select(
                                property => property.Name)
                            .ToHashSet(
                                StringComparer.OrdinalIgnoreCase);

                    var hasServer =
                        names.Overlaps(
                            new[]
                            {
                                "Server",
                                "SqlServer",
                                "ServerName",
                                "DataSource",
                                "Host",
                                "Address",
                                "ServerAddress"
                            });

                    var hasDatabase =
                        names.Overlaps(
                            new[]
                            {
                                "Database",
                                "DatabaseName",
                                "InitialCatalog",
                                "Catalog"
                            });

                    var hasConnectionString =
                        names.Overlaps(
                            new[]
                            {
                                "ConnectionString",
                                "SqlConnectionString"
                            });

                    var looksMes =
                        Path.GetFileName(file)
                            .Contains(
                                "mes",
                                StringComparison.OrdinalIgnoreCase);

                    if (looksMes
                        && ((hasServer && hasDatabase)
                            || hasConnectionString))
                    {
                        return file;
                    }
                }
                catch
                {
                    // Ignore unrelated JSON configuration files.
                }
            }
        }

        throw new FileNotFoundException(
            "MES SQL settings file was not found. Open MESSET and save the connection first.");
    }
}
