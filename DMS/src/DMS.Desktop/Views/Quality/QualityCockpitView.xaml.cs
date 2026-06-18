using DMS.Core.Quality;
using DMS.Core.Quality.Import;
using DMS.Core.Sap;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Quality;

public partial class QualityCockpitView : UserControl
{
    private readonly QualityStoragePaths _paths;
    private readonly JsonQualityRepository _repository;
    private readonly QualityExcelImportService _importService;
    private readonly QualityCustomerExcelImportService _customerImportService;

    public QualityCockpitView()
    {
        InitializeComponent();

        const string basePath = @"Z:\SAP\DMS-db\DEV";

        _paths = new QualityStoragePaths(basePath);
        _paths.EnsureDirectories();

        _repository = new JsonQualityRepository(_paths);

        var sapStoragePaths = new SapStoragePaths(basePath);
        sapStoragePaths.EnsureDirectories();

        var sapMaterials = new JsonSapMaterialRepository(
                sapStoragePaths.SapMaterialsFilePath)
            .LoadAll();

        var decorationRulesPath = Path.Combine(
            basePath,
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

        RefreshStatus();
    }

    // ============================================================
    // STAV CACHE
    // ============================================================

    private void RefreshStatus()
    {
        var articles = _repository.LoadArticles();
        var printVersions = _repository.LoadPrintVersions();
        var orders = _repository.LoadOrders();
        var customers = _repository.LoadCustomers();

        TxtSummary.Text =
            $"Quality cache: {_paths.QualityPath}\n" +
            $"Artikly: {articles.Count:N0}, " +
            $"tiskové verze: {printVersions.Count:N0}, " +
            $"zakázky: {orders.Count:N0}, " +
            $"zákazníci: {customers.Count:N0}";

        GridStatus.ItemsSource = new[]
        {
            new QualityCacheStatusRow
            {
                Area = "Quality artikly",
                Count = articles.Count,
                FilePath = _paths.QualityArticlesFilePath
            },
            new QualityCacheStatusRow
            {
                Area = "Tiskové verze",
                Count = printVersions.Count,
                FilePath = _paths.QualityPrintVersionsFilePath
            },
            new QualityCacheStatusRow
            {
                Area = "Zakázky",
                Count = orders.Count,
                FilePath = _paths.QualityOrdersFilePath
            },
            new QualityCacheStatusRow
            {
                Area = "Zákazníci",
                Count = customers.Count,
                FilePath = _paths.QualityCustomersFilePath
            }
        };

        TxtCustomersStatus.Text = customers.Count == 0
            ? "Zákazníci zatím nejsou importováni."
            : BuildCustomerStatus(customers);
    }

    private static string BuildCustomerStatus(
        IReadOnlyList<QualityCustomer> customers)
    {
        var activeCount = customers.Count(customer => customer.IsActive);
        var lorealCount = customers.Count(customer => customer.IsLoreal);

        return
            $"Uloženo zákazníků: {customers.Count:N0}\n" +
            $"Aktivních: {activeCount:N0}\n" +
            $"L'Oréal: {lorealCount:N0}";
    }

    // ============================================================
    // IMPORT ARTIKLŮ
    // ============================================================

    private void BtnImportArticles_Click(
        object sender,
        RoutedEventArgs e)
    {
        var filePath = PickExcelFile(
            "Vyber Soupis artiklů");

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            BtnImportArticles.IsEnabled = false;

            var result = _importService.ImportArticles(filePath);

            ShowImportResult(result);
            RefreshStatus();
        }
        catch (Exception ex)
        {
            ShowImportException(
                "Import quality artiklů selhal.",
                ex);
        }
        finally
        {
            BtnImportArticles.IsEnabled = true;
        }
    }

    // ============================================================
    // IMPORT TISKOVÝCH VERZÍ
    // ============================================================

    private void BtnImportPrintVersions_Click(
        object sender,
        RoutedEventArgs e)
    {
        var filePath = PickExcelFile(
            "Vyber Soupis tiskových verzí");

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            BtnImportPrintVersions.IsEnabled = false;

            var result =
                _importService.ImportPrintVersions(filePath);

            ShowImportResult(result);
            RefreshStatus();
        }
        catch (Exception ex)
        {
            ShowImportException(
                "Import tiskových verzí selhal.",
                ex);
        }
        finally
        {
            BtnImportPrintVersions.IsEnabled = true;
        }
    }

    // ============================================================
    // IMPORT ZAKÁZEK
    // ============================================================

    private void BtnImportOrders_Click(
        object sender,
        RoutedEventArgs e)
    {
        var filePath = PickExcelFile(
            "Vyber export quality zakázek");

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            BtnImportOrders.IsEnabled = false;

            var result = _importService.ImportOrders(filePath);

            ShowImportResult(result);
            RefreshStatus();
        }
        catch (Exception ex)
        {
            ShowImportException(
                "Import quality zakázek selhal.",
                ex);
        }
        finally
        {
            BtnImportOrders.IsEnabled = true;
        }
    }

    // ============================================================
    // IMPORT ZÁKAZNÍKŮ
    // ============================================================

    private void BtnImportCustomers_Click(
        object sender,
        RoutedEventArgs e)
    {
        var filePath = PickExcelFile(
            "Vyber export zákazníků z MS Lists");

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            BtnImportCustomers.IsEnabled = false;

            TxtCustomersStatus.Text =
                $"Importuji zákazníky ze souboru:\n{filePath}";

            var result =
                _customerImportService.Import(filePath);

            var message = result.Messages.Count > 0
                ? string.Join(
                    Environment.NewLine,
                    result.Messages)
                : BuildCustomerImportResultText(result);

            TxtCustomersStatus.Text = message;

            RefreshStatus();

            MessageBox.Show(
                message,
                result.Success
                    ? "QA00 - import zákazníků dokončen"
                    : "QA00 - import zákazníků s chybami",
                MessageBoxButton.OK,
                result.Success
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            TxtCustomersStatus.Text =
                $"Import zákazníků selhal: {ex.Message}";

            ShowImportException(
                "Import zákazníků selhal.",
                ex);
        }
        finally
        {
            BtnImportCustomers.IsEnabled = true;
        }
    }

    private static string BuildCustomerImportResultText(
        QualityCustomerImportResult result)
    {
        return
            $"Načteno řádků: {result.SourceRows:N0}\n" +
            $"Importováno: {result.ImportedCount:N0}\n" +
            $"Přidáno: {result.AddedCount:N0}\n" +
            $"Aktualizováno: {result.UpdatedCount:N0}\n" +
            $"Přeskočeno: {result.SkippedCount:N0}\n" +
            $"Chyby: {result.ErrorCount:N0}";
    }

    // ============================================================
    // SPOLEČNÉ METODY
    // ============================================================

    private static string? PickExcelFile(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter =
                "Excel soubory (*.xlsx)|*.xlsx|" +
                "Všechny soubory (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    private static void ShowImportResult(
        QualityExcelImportResult result)
    {
        var message = result.Message;

        if (result.Warnings.Count > 0)
        {
            message +=
                Environment.NewLine +
                Environment.NewLine +
                "Upozornění:" +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    result.Warnings.Take(20));
        }

        MessageBox.Show(
            message,
            result.Success
                ? "QA00 - import dokončen"
                : "QA00 - import selhal",
            MessageBoxButton.OK,
            result.Success
                ? MessageBoxImage.Information
                : MessageBoxImage.Error);
    }

    private static void ShowImportException(
        string title,
        Exception exception)
    {
        MessageBox.Show(
            $"{title}\n\n{exception.Message}",
            "QA00 - chyba importu",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    // ============================================================
    // DISPLAY MODEL
    // ============================================================

    private sealed class QualityCacheStatusRow
    {
        public string Area { get; init; } =
            string.Empty;

        public int Count { get; init; }

        public string FilePath { get; init; } =
            string.Empty;
    }
}