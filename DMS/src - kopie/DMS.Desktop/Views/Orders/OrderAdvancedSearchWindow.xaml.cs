using DMS.Integration.Mes.Orders;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace DMS.Desktop.Views.Orders;

public partial class OrderAdvancedSearchWindow : Window
{
    public sealed class Choice
    {
        public Choice(
            int? value,
            string text)
        {
            Value = value;
            Text = text;
        }

        public int? Value { get; }
        public string Text { get; }

        public override string ToString() => Text;
    }

    private readonly MesOrderOverviewDataService _service;
    private bool _loading;

    public OrderAdvancedSearchWindow(
        MesOrderOverviewDataService service)
    {
        InitializeComponent();

        _service =
            service
            ?? throw new ArgumentNullException(nameof(service));

        ConfigureChoices();
    }

    public IReadOnlyList<MesProductionOrderRecord>? AppliedResults { get; private set; }

    private void ConfigureChoices()
    {
        CmbArchive.ItemsSource =
            new[]
            {
                new Choice(null, "Vše"),
                new Choice(0, "NARC (Not archived)"),
                new Choice(1, "ARCH (Archived)")
            };

        CmbFailure.ItemsSource =
            new[]
            {
                new Choice(null, "Vše"),
                new Choice(0, "NERR (Not erroneous)"),
                new Choice(1, "ERRO (Erroneous)")
            };

        CmbGeneral.ItemsSource =
            new[]
            {
                new Choice(null, "Vše"),
                new Choice(0, "CRTD (Created)"),
                new Choice(1, "REL (Released)"),
                new Choice(2, "PREL (Partially released)"),
                new Choice(3, "RWDN (Release withdrawn)"),
                new Choice(4, "CCLD (Canceled)"),
                new Choice(5, "UCPL (Uncompleted)"),
                new Choice(6, "CLSD (Closed)")
            };

        CmbPlanning.ItemsSource =
            new[]
            {
                new Choice(null, "Vše"),
                new Choice(0, "NPLD (Not planned)"),
                new Choice(1, "PPLD (Partly planned)"),
                new Choice(2, "PLND (Planned)")
            };

        CmbFixation.ItemsSource =
            new[]
            {
                new Choice(null, "Vše"),
                new Choice(0, "NFIX (Not fixed)"),
                new Choice(1, "PFIX (Partially fixed)"),
                new Choice(2, "FIX (Fixed)")
            };

        CmbProduction.ItemsSource =
            new[]
            {
                new Choice(null, "Vše"),
                new Choice(0, "NPRO (Not in production)"),
                new Choice(1, "PROD (In production)"),
                new Choice(2, "PFIN (Production finished)")
            };

        foreach (var combo
                 in new[]
                    {
                        CmbArchive,
                        CmbFailure,
                        CmbGeneral,
                        CmbPlanning,
                        CmbFixation,
                        CmbProduction
                    })
        {
            combo.SelectedIndex = 0;
        }
    }

    private static int? ChoiceValue(
        System.Windows.Controls.ComboBox combo)
    {
        return (combo.SelectedItem as Choice)?.Value;
    }

    private MesOrderAdvancedSearchCriteria BuildCriteria()
    {
        var sap =
            !string.IsNullOrWhiteSpace(TxtReferenceSap.Text)
                ? TxtReferenceSap.Text
                : TxtSapArticle.Text;

        return new MesOrderAdvancedSearchCriteria
        {
            OrderCode = TxtOrderCode.Text?.Trim() ?? string.Empty,
            ProductCode = TxtProductCode.Text?.Trim() ?? string.Empty,
            ProductDesignation = TxtProductDesignation.Text?.Trim() ?? string.Empty,
            ProductDescription = TxtProductDescription.Text?.Trim() ?? string.Empty,
            OrderDescription = TxtOrderDescription.Text?.Trim() ?? string.Empty,
            CostCenter = TxtCostCenter.Text?.Trim() ?? string.Empty,

            SapArticleNumber = sap?.Trim() ?? string.Empty,
            CustomerOrderCode = TxtCustomerOrder.Text?.Trim() ?? string.Empty,
            CustomerCode = TxtCustomerCode.Text?.Trim() ?? string.Empty,

            CreatedFrom = DpCreatedFrom.SelectedDate,
            CreatedToExclusive = EndExclusive(DpCreatedTo.SelectedDate),
            PlannedStartFrom = DpPlannedStartFrom.SelectedDate,
            PlannedStartToExclusive = EndExclusive(DpPlannedStartTo.SelectedDate),
            PlannedEndFrom = DpPlannedEndFrom.SelectedDate,
            PlannedEndToExclusive = EndExclusive(DpPlannedEndTo.SelectedDate),
            ActualStartFrom = DpActualStartFrom.SelectedDate,
            ActualStartToExclusive = EndExclusive(DpActualStartTo.SelectedDate),
            ActualEndFrom = DpActualEndFrom.SelectedDate,
            ActualEndToExclusive = EndExclusive(DpActualEndTo.SelectedDate),

            TargetQuantityMin = ParseDecimal(TxtTargetMin.Text),
            TargetQuantityMax = ParseDecimal(TxtTargetMax.Text),
            FinishedQuantityMin = ParseDecimal(TxtFinishedMin.Text),
            FinishedQuantityMax = ParseDecimal(TxtFinishedMax.Text),
            ScrapQuantityMin = ParseDecimal(TxtScrapMin.Text),
            ScrapQuantityMax = ParseDecimal(TxtScrapMax.Text),
            ProgressPercentMin = ParseDecimal(TxtProgressMin.Text),
            ProgressPercentMax = ParseDecimal(TxtProgressMax.Text),

            ArchiveStatus = ChoiceValue(CmbArchive),
            FailureStatus = ChoiceValue(CmbFailure),
            GeneralStatus = ChoiceValue(CmbGeneral),
            PlanningStatus = ChoiceValue(CmbPlanning),
            PlanningFixStatus = ChoiceValue(CmbFixation),
            ProductionStatus = ChoiceValue(CmbProduction),

            MaxRows = ParseMaxRows()
        };
    }

    private static DateTime? EndExclusive(
        DateTime? value)
    {
        return value?.Date.AddDays(1);
    }

    private int ParseMaxRows()
    {
        if (!int.TryParse(
                TxtMaxRows.Text,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out var value))
        {
            value = 1000;
        }

        return Math.Clamp(
            value,
            1,
            10000);
    }

    private static decimal? ParseDecimal(
        string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (decimal.TryParse(
                text,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out var current))
        {
            return current;
        }

        if (decimal.TryParse(
                text,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var invariant))
        {
            return invariant;
        }

        return null;
    }

    private async void BtnPreview_Click(
        object sender,
        RoutedEventArgs e)
    {
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        BtnPreview.IsEnabled = false;
        BtnApply.IsEnabled = false;

        try
        {
            TxtStatus.Text =
                "Hledám v databázi...";

            var rows =
                await _service.SearchOrdersAsync(
                    BuildCriteria());

            GridResults.ItemsSource =
                rows;

            TxtStatus.Text =
                $"Nalezeno {rows.Count:N0} zakázek.";

            BtnApply.IsEnabled =
                rows.Count > 0;
        }
        catch (Exception ex)
        {
            GridResults.ItemsSource = null;
            TxtStatus.Text =
                $"Hledání selhalo: {ex.Message}";

            MessageBox.Show(
                this,
                ex.Message,
                "ORD10 - Rozšířené hledání",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            BtnPreview.IsEnabled = true;
            _loading = false;
        }
    }

    private void BtnApply_Click(
        object sender,
        RoutedEventArgs e)
    {
        AppliedResults =
            GridResults.Items
                .Cast<MesProductionOrderRecord>()
                .ToList();

        DialogResult = true;
    }

    private void BtnCancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void BtnReset_Click(
        object sender,
        RoutedEventArgs e)
    {
        foreach (var textBox
                 in new[]
                    {
                        TxtOrderCode,
                        TxtProductCode,
                        TxtProductDesignation,
                        TxtProductDescription,
                        TxtOrderDescription,
                        TxtSapArticle,
                        TxtCostCenter,
                        TxtReferenceSap,
                        TxtCustomerOrder,
                        TxtCustomerCode,
                        TxtTargetMin,
                        TxtTargetMax,
                        TxtFinishedMin,
                        TxtFinishedMax,
                        TxtScrapMin,
                        TxtScrapMax,
                        TxtProgressMin,
                        TxtProgressMax
                    })
        {
            textBox.Clear();
        }

        foreach (var picker
                 in new[]
                    {
                        DpCreatedFrom,
                        DpCreatedTo,
                        DpPlannedStartFrom,
                        DpPlannedStartTo,
                        DpPlannedEndFrom,
                        DpPlannedEndTo,
                        DpActualStartFrom,
                        DpActualStartTo,
                        DpActualEndFrom,
                        DpActualEndTo
                    })
        {
            picker.SelectedDate = null;
        }

        foreach (var combo
                 in new[]
                    {
                        CmbArchive,
                        CmbFailure,
                        CmbGeneral,
                        CmbPlanning,
                        CmbFixation,
                        CmbProduction
                    })
        {
            combo.SelectedIndex = 0;
        }

        TxtMaxRows.Text = "1000";
        GridResults.ItemsSource = null;
        TxtStatus.Text = string.Empty;
        BtnApply.IsEnabled = false;
    }
}
