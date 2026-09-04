using DMS.Core.Quality;
using DMS.Core.Quality.Import;
using DMS.Core.Sap;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using Microsoft.Win32;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Quality;

public partial class QualityCockpitView : UserControl
{
    private readonly QualityStoragePaths _paths;
    private readonly JsonQualityRepository _repository;
    private readonly QualityExcelImportService _importService;
    private readonly QualityCustomerExcelImportService _customerImportService;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    public QualityCockpitView()
        : this(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")),
            null,
            null,
            null,
            null)
    {
    }

    public QualityCockpitView(
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

        _paths = new QualityStoragePaths(dmsRootPath);
        _paths.EnsureDirectories();

        _repository = new JsonQualityRepository(_paths);

        var sapStoragePaths = new SapStoragePaths(dmsRootPath);
        sapStoragePaths.EnsureDirectories();

        var sapMaterials = new JsonSapMaterialRepository(
                sapStoragePaths.SapMaterialsFilePath)
            .LoadAll();

        var decorationRulesPath = Path.Combine(
            dmsRootPath,
            "Config",
            "sap-decoration-rules.json");

        var decorationRules = new SapDecorationRulesLoader()
            .LoadFromJson(decorationRulesPath);

        var decorationRuleService =
            new SapDecorationRuleService(decorationRules);

        _importService = new QualityExcelImportService(
            _repository,
            sapMaterials,
            decorationRuleService);

        _customerImportService =
            new QualityCustomerExcelImportService(_repository);

        ApplyLocalization();
        RefreshStatus();

        _logger?.AdminAction(
            "QA00",
            "OpenQualityCockpitView",
            _currentUserName,
            $"QualityPath={_paths.QualityPath}; SapMaterials={sapStoragePaths.SapMaterialsFilePath}; DecorationRules={decorationRulesPath}");
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("QA00.Title", "QA00 - Quality cockpit");

        TabCacheStatus.Header = T("QA00.Tab.CacheStatus", "Cache status");
        TabImportArticles.Header = T("QA00.Tab.ImportArticles", "Import articles");
        TabImportPrintVersions.Header = T("QA00.Tab.ImportPrintVersions", "Import print versions");
        TabImportOrders.Header = T("QA00.Tab.ImportOrders", "Import orders");
        TabImportCustomers.Header = T("QA00.Tab.ImportCustomers", "Import customers");

        ColArea.Header = T("QA00.Column.Area", "Area");
        ColCount.Header = T("QA00.Column.Count", "Count");
        ColFilePath.Header = T("QA00.Column.File", "File");

        TxtImportArticlesDescription.Text = T(
            "QA00.ImportArticles.Description",
            "Import quality articles from a PowerApps / SharePoint export.");

        TxtImportPrintVersionsDescription.Text = T(
            "QA00.ImportPrintVersions.Description",
            "Import print versions, notes, tasks and SAP decoration details.");

        TxtImportOrdersDescription.Text = T(
            "QA00.ImportOrders.Description",
            "Import historical quality orders.");

        TxtImportCustomersDescription.Text = T(
            "QA00.ImportCustomers.Description",
            "Import customer master data from an MS Lists Excel export.");

        BtnImportArticles.Content = T("QA00.Button.ImportArticles", "Import article list");
        BtnImportPrintVersions.Content = T("QA00.Button.ImportPrintVersions", "Import print version list");
        BtnImportOrders.Content = T("QA00.Button.ImportOrders", "Import orders");
        BtnImportCustomers.Content = T("QA00.Button.ImportCustomers", "Import customers");

        TxtCustomersStatus.Text = T(
            "QA00.Customers.NotImported",
            "Customers have not been imported yet.");
    }

    private void RefreshStatus()
    {
        var articles = _repository.LoadArticles();
        var printVersions = _repository.LoadPrintVersions();
        var orders = _repository.LoadOrders();
        var customers = _repository.LoadCustomers();

        TxtSummary.Text = TF(
            "QA00.Summary",
            "Quality cache: {0}\nArticles: {1:N0}, print versions: {2:N0}, orders: {3:N0}, customers: {4:N0}",
            _paths.QualityPath,
            articles.Count,
            printVersions.Count,
            orders.Count,
            customers.Count);

        GridStatus.ItemsSource = new[]
        {
            new QualityCacheStatusRow
            {
                Area = T("QA00.Area.Articles", "Quality articles"),
                Count = articles.Count,
                FilePath = _paths.QualityArticlesFilePath
            },
            new QualityCacheStatusRow
            {
                Area = T("QA00.Area.PrintVersions", "Print versions"),
                Count = printVersions.Count,
                FilePath = _paths.QualityPrintVersionsFilePath
            },
            new QualityCacheStatusRow
            {
                Area = T("QA00.Area.Orders", "Orders"),
                Count = orders.Count,
                FilePath = _paths.QualityOrdersFilePath
            },
            new QualityCacheStatusRow
            {
                Area = T("QA00.Area.Customers", "Customers"),
                Count = customers.Count,
                FilePath = _paths.QualityCustomersFilePath
            }
        };

        TxtImportMessage.Text = TF(
            "QA00.CacheStatus.Message",
            "Quality cache refreshed. Last refresh: {0}",
            DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture));

        TxtCustomersStatus.Text = customers.Count == 0
            ? T("QA00.Customers.NotImported", "Customers have not been imported yet.")
            : BuildCustomerStatus(customers);

        _logger?.AdminAction(
            "QA00",
            "RefreshQualityCacheStatus",
            _currentUserName,
            $"Articles={articles.Count}; PrintVersions={printVersions.Count}; Orders={orders.Count}; Customers={customers.Count}");
    }

    private string BuildCustomerStatus(
        IReadOnlyList<QualityCustomer> customers)
    {
        var activeCount = customers.Count(customer => customer.IsActive);
        var lorealCount = customers.Count(customer => customer.IsLoreal);

        return TF(
            "QA00.Customers.Status",
            "Stored customers: {0:N0}\nActive: {1:N0}\nL'Oréal: {2:N0}",
            customers.Count,
            activeCount,
            lorealCount);
    }

    private void BtnImportArticles_Click(
        object sender,
        RoutedEventArgs e)
    {
        var filePath = PickExcelFile(
            T("QA00.ImportArticles.FileDialogTitle", "Select article list"));

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            BtnImportArticles.IsEnabled = false;

            _logger?.AdminAction(
                "QA00",
                "ImportQualityArticlesStarted",
                _currentUserName,
                $"File={filePath}");

            var before = SnapshotById(_repository.LoadArticles());

            var result = _importService.ImportArticles(filePath);

            var after = SnapshotById(_repository.LoadArticles());
            LogEntityAuditChanges("QualityArticle", before, after, filePath);

            ShowImportResult(
                result,
                "ImportQualityArticlesFinished",
                "QA00.Dialog.ImportArticles.Completed",
                "QA00.Dialog.ImportArticles.Failed");

            RefreshStatus();
        }
        catch (Exception ex)
        {
            ShowImportException(
                "ImportQualityArticlesFailed",
                T("QA00.Error.ImportArticlesFailed", "Quality article import failed."),
                ex);
        }
        finally
        {
            BtnImportArticles.IsEnabled = true;
        }
    }

    private void BtnImportPrintVersions_Click(
        object sender,
        RoutedEventArgs e)
    {
        var filePath = PickExcelFile(
            T("QA00.ImportPrintVersions.FileDialogTitle", "Select print version list"));

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            BtnImportPrintVersions.IsEnabled = false;

            _logger?.AdminAction(
                "QA00",
                "ImportPrintVersionsStarted",
                _currentUserName,
                $"File={filePath}");

            var before = SnapshotById(_repository.LoadPrintVersions());

            var result =
                _importService.ImportPrintVersions(filePath);

            var after = SnapshotById(_repository.LoadPrintVersions());
            LogEntityAuditChanges("QualityPrintVersion", before, after, filePath);

            ShowImportResult(
                result,
                "ImportPrintVersionsFinished",
                "QA00.Dialog.ImportPrintVersions.Completed",
                "QA00.Dialog.ImportPrintVersions.Failed");

            RefreshStatus();
        }
        catch (Exception ex)
        {
            ShowImportException(
                "ImportPrintVersionsFailed",
                T("QA00.Error.ImportPrintVersionsFailed", "Print version import failed."),
                ex);
        }
        finally
        {
            BtnImportPrintVersions.IsEnabled = true;
        }
    }

    private void BtnImportOrders_Click(
        object sender,
        RoutedEventArgs e)
    {
        var filePath = PickExcelFile(
            T("QA00.ImportOrders.FileDialogTitle", "Select quality order export"));

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            BtnImportOrders.IsEnabled = false;

            _logger?.AdminAction(
                "QA00",
                "ImportQualityOrdersStarted",
                _currentUserName,
                $"File={filePath}");

            var before = SnapshotById(_repository.LoadOrders());

            var result = _importService.ImportOrders(filePath);

            var after = SnapshotById(_repository.LoadOrders());
            LogEntityAuditChanges("QualityOrder", before, after, filePath);

            ShowImportResult(
                result,
                "ImportQualityOrdersFinished",
                "QA00.Dialog.ImportOrders.Completed",
                "QA00.Dialog.ImportOrders.Failed");

            RefreshStatus();
        }
        catch (Exception ex)
        {
            ShowImportException(
                "ImportQualityOrdersFailed",
                T("QA00.Error.ImportOrdersFailed", "Quality order import failed."),
                ex);
        }
        finally
        {
            BtnImportOrders.IsEnabled = true;
        }
    }

    private void BtnImportCustomers_Click(
        object sender,
        RoutedEventArgs e)
    {
        var filePath = PickExcelFile(
            T("QA00.ImportCustomers.FileDialogTitle", "Select customer export from MS Lists"));

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            BtnImportCustomers.IsEnabled = false;

            TxtCustomersStatus.Text = TF(
                "QA00.Customers.Importing",
                "Importing customers from file:\n{0}",
                filePath);

            _logger?.AdminAction(
                "QA00",
                "ImportCustomersStarted",
                _currentUserName,
                $"File={filePath}");

            var before = SnapshotById(_repository.LoadCustomers());

            var result =
                _customerImportService.Import(filePath);

            var after = SnapshotById(_repository.LoadCustomers());
            LogEntityAuditChanges("QualityCustomer", before, after, filePath);

            var message = result.Messages.Count > 0
                ? string.Join(
                    Environment.NewLine,
                    result.Messages)
                : BuildCustomerImportResultText(result);

            TxtCustomersStatus.Text = message;

            RefreshStatus();

            _logger?.AdminAction(
                "QA00",
                "ImportCustomersFinished",
                _currentUserName,
                $"Success={result.Success}; SourceRows={result.SourceRows}; Imported={result.ImportedCount}; Errors={result.ErrorCount}");

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                result.Success
                    ? T("QA00.Dialog.ImportCustomers.Completed", "QA00 - customer import completed")
                    : T("QA00.Dialog.ImportCustomers.Failed", "QA00 - customer import completed with errors"),
                message);
        }
        catch (Exception ex)
        {
            TxtCustomersStatus.Text = TF(
                "QA00.Customers.ImportFailed",
                "Customer import failed: {0}",
                ex.Message);

            ShowImportException(
                "ImportCustomersFailed",
                T("QA00.Error.ImportCustomersFailed", "Customer import failed."),
                ex);
        }
        finally
        {
            BtnImportCustomers.IsEnabled = true;
        }
    }

    private string BuildCustomerImportResultText(
        QualityCustomerImportResult result)
    {
        return TF(
            "QA00.Customers.ImportResult",
            "Source rows: {0:N0}\nImported: {1:N0}\nAdded: {2:N0}\nUpdated: {3:N0}\nSkipped: {4:N0}\nErrors: {5:N0}",
            result.SourceRows,
            result.ImportedCount,
            result.AddedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.ErrorCount);
    }


    private static Dictionary<string, T> SnapshotById<T>(IEnumerable<T> items)
    {
        return items
            .Where(item => item is not null)
            .GroupBy(GetEntityId, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.OrdinalIgnoreCase);
    }

    private void LogEntityAuditChanges<T>(
        string entityName,
        IReadOnlyDictionary<string, T> before,
        IReadOnlyDictionary<string, T> after,
        string sourceFile)
    {
        foreach (var item in after.OrderBy(item => item.Key))
        {
            if (!before.TryGetValue(item.Key, out var original))
            {
                _logger?.AuditCreated(
                    "QA00",
                    entityName,
                    item.Key,
                    _currentUserName,
                    $"SourceFile={sourceFile}; {BuildEntityDetail(item.Value)}");

                continue;
            }

            LogEntityFieldChanges(entityName, item.Key, original, item.Value);
        }

        foreach (var item in before.OrderBy(item => item.Key))
        {
            if (after.ContainsKey(item.Key))
            {
                continue;
            }

            _logger?.AuditDeleted(
                "QA00",
                entityName,
                item.Key,
                _currentUserName,
                $"SourceFile={sourceFile}; {BuildEntityDetail(item.Value)}");
        }
    }

    private void LogEntityFieldChanges<T>(
        string entityName,
        string entityId,
        T original,
        T current)
    {
        foreach (var property in GetAuditableProperties(typeof(T)))
        {
            var oldValue = FormatAuditValue(property.GetValue(original));
            var newValue = FormatAuditValue(property.GetValue(current));

            if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                continue;
            }

            _logger?.AuditChange(
                "QA00",
                entityName,
                entityId,
                property.Name,
                oldValue,
                newValue,
                _currentUserName);
        }
    }

    private static string BuildEntityDetail<T>(T item)
    {
        return string.Join(
            "; ",
            GetAuditableProperties(typeof(T))
                .Select(property => $"{property.Name}={FormatAuditValue(property.GetValue(item))}"));
    }

    private static IEnumerable<PropertyInfo> GetAuditableProperties(Type type)
    {
        return type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => property.CanRead)
            .Where(property => property.Name is not "RawLine");
    }

    private static string GetEntityId<T>(T item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        var type = item.GetType();

        foreach (var propertyName in new[]
                 {
                     "Code",
                     "FullPrintVersionNumber",
                     "PrintVersionNumber",
                     "SapMaterialNumber",
                     "MaterialNumber",
                     "OrderNumber",
                     "Number",
                     "SourceId",
                     "Id",
                     "Name"
                 })
        {
            var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            var value = FormatAuditValue(property?.GetValue(item));

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return item.GetHashCode().ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatAuditValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is DateTime dateTime)
        {
            return dateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        if (value is DateOnly dateOnly)
        {
            return dateOnly.ToString("O", CultureInfo.InvariantCulture);
        }

        if (value is TimeOnly timeOnly)
        {
            return timeOnly.ToString("O", CultureInfo.InvariantCulture);
        }

        if (value is decimal decimalValue)
        {
            return decimalValue.ToString(CultureInfo.InvariantCulture);
        }

        if (value is double doubleValue)
        {
            return doubleValue.ToString(CultureInfo.InvariantCulture);
        }

        if (value is float floatValue)
        {
            return floatValue.ToString(CultureInfo.InvariantCulture);
        }

        if (value is IFormattable formattable && value.GetType().IsValueType)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        if (value is IEnumerable enumerable)
        {
            var values = new List<string>();

            foreach (var item in enumerable)
            {
                values.Add(FormatAuditValue(item));
            }

            return string.Join(",", values);
        }

        return value.ToString() ?? string.Empty;
    }

    private string? PickExcelFile(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter =
                T("QA00.FileDialog.ExcelFilter", "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*"),
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    private void ShowImportResult(
        QualityExcelImportResult result,
        string logAction,
        string successTitleKey,
        string failureTitleKey)
    {
        var message = result.Message;

        if (result.Warnings.Count > 0)
        {
            message +=
                Environment.NewLine +
                Environment.NewLine +
                T("QA00.Warnings", "Warnings:") +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    result.Warnings.Take(20));
        }

        _logger?.AdminAction(
            "QA00",
            logAction,
            _currentUserName,
            $"Success={result.Success}; Warnings={result.Warnings.Count}; Message={result.Message}");

        DmsConfirmDialog.ShowInfo(
            Window.GetWindow(this),
            result.Success
                ? T(successTitleKey, "QA00 - import completed")
                : T(failureTitleKey, "QA00 - import failed"),
            message);
    }

    private void ShowImportException(
        string logAction,
        string title,
        Exception exception)
    {
        _logger?.Error($"QA00: {logAction}", exception);

        DmsConfirmDialog.ShowInfo(
            Window.GetWindow(this),
            T("QA00.Dialog.ImportError", "QA00 - import error"),
            $"{title}\n\n{exception.Message}");
    }

    private string T(string key, string fallback)
    {
        var value = _translate?.Invoke(key) ?? fallback;
        return IsMissing(value, key) ? fallback : value;
    }

    private string TF(string key, string fallback, params object[] args)
    {
        if (_translateFormat is not null)
        {
            var translated = _translateFormat.Invoke(key, args);
            if (!IsMissing(translated, key))
            {
                return translated;
            }
        }

        var pattern = T(key, fallback);

        try
        {
            return string.Format(CultureInfo.CurrentCulture, pattern, args);
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

    private sealed class QualityCacheStatusRow
    {
        public string Area { get; init; } = string.Empty;

        public int Count { get; init; }

        public string FilePath { get; init; } = string.Empty;
    }
}
