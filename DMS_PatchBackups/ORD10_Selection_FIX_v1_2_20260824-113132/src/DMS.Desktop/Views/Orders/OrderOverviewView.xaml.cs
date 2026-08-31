using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Orders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace DMS.Desktop.Views.Orders;

public partial class OrderOverviewView : UserControl
{
    private readonly string _configurationRootPath;
    private readonly DmsLogger _logger;
    private readonly string _user;
    private readonly Action<string> _openTechnology;
    private readonly Func<string, string> _translate;
    private readonly MesDatabaseSettingsService _settingsService = new();

    private MesOrderOverviewDataService? _service;
    private List<MesProductionOrderRecord> _allOrders = new();
    private bool _loaded;
    private bool _loadingOrders;
    private CancellationTokenSource? _operationLoadCts;
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

    private void OrderOverviewView_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        _operationLoadCts?.Cancel();
        _operationLoadCts?.Dispose();
        _operationLoadCts = null;
    }

    private async void OrderOverviewView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

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

        _operationLoadCts?.Cancel();
        _operationLoadCts?.Dispose();
        _operationLoadCts = null;
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
                    SearchText =
                        TxtSearch.Text?.Trim()
                        ?? string.Empty,
                    MaxRows = 500
                };

            var rows =
                await _service.GetOrdersAsync(
                    filter);

            _allOrders =
                rows.ToList();

            ReloadStatusFilter(
                _allOrders);

            ApplyClientFilter();

            _logger.Info(
                $"ORD10 MES orders loaded; user={_user}; rows={_allOrders.Count}; search={filter.SearchText}");
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
                .Cast<string>()
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var statuses =
            rows.Select(
                    item => item.StatusCode)
                .Where(
                    value => !string.IsNullOrWhiteSpace(value))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    value => StatusSort(value))
                .ThenBy(
                    value => value,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        _suppressStatusRefresh = true;

        try
        {
            LstStatuses.ItemsSource =
                statuses;

            foreach (var status in statuses)
            {
                if (previous.Count == 0
                    || previous.Contains(status))
                {
                    LstStatuses.SelectedItems.Add(
                        status);
                }
            }
        }
        finally
        {
            _suppressStatusRefresh = false;
        }

        UpdateStatusFilterText();
    }

    private static int StatusSort(
        string status)
    {
        return status switch
        {
            "REL PROD" => 0,
            "REL" => 1,
            "CLSD" => 2,
            "ERROR" => 3,
            _ => 9
        };
    }

    private void ApplyClientFilter()
    {
        var selected =
            LstStatuses.SelectedItems
                .Cast<string>()
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        IEnumerable<MesProductionOrderRecord> query =
            _allOrders;

        if (selected.Count > 0)
        {
            query =
                query.Where(
                    item => selected.Contains(
                        item.StatusCode));
        }
        else
        {
            query =
                Array.Empty<MesProductionOrderRecord>();
        }

        var result =
            query.ToList();

        GridOrders.ItemsSource =
            result;

        if (result.Count > 0)
        {
            GridOrders.SelectedIndex = 0;
        }
        else
        {
            GridOperations.ItemsSource = null;
            TxtOperationsTitle.Text =
                T(
                    "ORD10.Operations.None",
                    "Operace - vyber zakázku");
        }

        TxtStatus.Text =
            string.Format(
                T(
                    "ORD10.Status.Loaded",
                    "Načteno {0} zakázek."),
                result.Count);

        UpdateStatusFilterText();
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
                .Cast<string>()
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
        _operationLoadCts?.Cancel();
        _operationLoadCts?.Dispose();
        _operationLoadCts = null;

        GridOperations.ItemsSource = null;

        if (GridOrders.SelectedItem
            is not MesProductionOrderRecord order)
        {
            TxtOperationsTitle.Text =
                T(
                    "ORD10.Operations.None",
                    "Operace - vyber zakázku");
            return;
        }

        var cts =
            new CancellationTokenSource();

        _operationLoadCts =
            cts;

        await LoadOperationsAsync(
            order,
            cts.Token);
    }

    private async Task LoadOperationsAsync(
        MesProductionOrderRecord order,
        CancellationToken cancellationToken)
    {
        if (_service is null)
        {
            return;
        }

        try
        {
            TxtOperationsTitle.Text =
                string.Format(
                    T(
                        "ORD10.Operations.Title",
                        "Operace zakázky {0}"),
                    order.OrderCode);

            var rows =
                await _service.GetOperationsAsync(
                    order.Id,
                    order.OrderCode,
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (GridOrders.SelectedItem
                    is not MesProductionOrderRecord selectedOrder
                || selectedOrder.Id != order.Id)
            {
                return;
            }

            GridOperations.ItemsSource =
                rows;
        }
        catch (OperationCanceledException)
        {
            // Expected when the user changes selection while the previous
            // operation query is still running.
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"ORD10 operation load failed. Order={order.OrderCode}",
                ex);

            if (GridOrders.SelectedItem
                    is MesProductionOrderRecord selectedOrder
                && selectedOrder.Id == order.Id)
            {
                GridOperations.ItemsSource = null;
                TxtStatus.Text = ex.Message;
            }
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

    private async void TxtSearch_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (!_loaded
            || _service is null)
        {
            return;
        }

        // Keep it simple and deterministic; the server-side search is cheap and parameterized.
        await LoadOrdersAsync(
            showDialogOnError: false);
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
