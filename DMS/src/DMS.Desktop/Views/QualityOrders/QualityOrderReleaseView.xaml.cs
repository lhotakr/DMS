using DMS.Core.Quality;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DMS.Desktop.Views.QualityOrders;

public partial class QualityOrderReleaseView : UserControl
{
    private readonly QualityOrderMaintenanceService _service;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private string _orderNumber = string.Empty;
    private QualityOrder? _order;

    public event Action<string>? TransactionRequested;

    public QualityOrderReleaseView(
        string orderNumber,
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

        var rootPath = string.IsNullOrWhiteSpace(dmsRootPath)
            ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."))
            : dmsRootPath;

        var paths = new QualityStoragePaths(rootPath);
        paths.EnsureDirectories();
        _service = new QualityOrderMaintenanceService(new JsonQualityRepository(paths));

        ApplyLocalization();
        LoadOrder(orderNumber);
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = TF("QO06.Title", _orderNumber);
        TxtSubtitle.Text = T("QO06.Subtitle");
        BtnRelease.Content = T("QO06.Action.Release");
        BtnBlock.Content = T("QO06.Action.Block");
        BtnOpenQO02.Content = T("QO.Action.OpenQO02");
        BtnOpenQO03.Content = T("QO.Action.OpenQO03");

        LblOrder.Text = T("QO.Field.OrderNumber");
        LblPrintVersion.Text = T("QO.Field.PrintVersion");
        LblSap.Text = T("QO.Field.SapId");
        LblRelease.Text = T("QO.Field.ReleaseState");
        LblSchedule.Text = T("QO.Field.ScheduleStatus");
        LblStart.Text = T("QO.Field.ProductionStart");
        LblEnd.Text = T("QO.Field.ProductionEnd");
        LblLoreal.Text = T("QO.Flag.Loreal");
        LblNotes.Text = T("QO.Field.OrderNotesImportant");
        TxtReleaseChecklistTitle.Text = T("QO06.ReleaseChecklist.Title");
        ChkTesaTest.Content = T("QO06.Check.TesaTest");
        ChkAcetoneTest.Content = T("QO06.Check.AcetoneTest");
        ChkGridTest.Content = T("QO06.Check.GridTest");
        ChkVisualCheck.Content = T("QO06.Check.VisualCheck");
        ChkApproved.Content = T("QO06.Check.Approved");
        LblReleaseNotes.Text = T("QO06.Field.ReleaseNotes");
    }

    private void LoadOrder(string orderNumber)
    {
        _orderNumber = orderNumber.Trim();
        _order = _service.FindOrder(_orderNumber);

        TxtTitle.Text = TF("QO06.Title", _orderNumber);

        if (_order is null)
        {
            ShowWarning(TF("QO06.Warning.NotFound", _orderNumber));
            BtnRelease.IsEnabled = false;
            BtnBlock.IsEnabled = false;
            BtnOpenQO02.IsEnabled = false;
            BtnOpenQO03.IsEnabled = false;
            return;
        }

        ClearWarning();
        FillOrder(_order);

        _logger?.AdminAction(
            "QO06",
            "OpenQualityOrderReleaseBlock",
            _currentUserName,
            $"Order={_order.OrderNumber}; Released={_order.Released}");
    }

    private void FillOrder(QualityOrder order)
    {
        TxtOrder.Text = order.OrderNumber;
        TxtPrintVersion.Text = order.PrintVersionNumber;
        TxtSap.Text = order.SapMaterialNumber;
        TxtRelease.Text = order.Released
            ? T("QO.Release.Released")
            : T("QO.Release.Blocked");
        TxtRelease.Foreground = order.Released ? Brushes.LightGreen : Brushes.Orange;

        var scheduleCode = QualityOrderMaintenanceService.GetScheduleStatusCode(order);
        TxtSchedule.Text = T($"QO.Status.{scheduleCode}");
        TxtSchedule.Foreground = scheduleCode == "Finished"
            ? Brushes.LightGreen
            : scheduleCode == "Scheduled"
                ? Brushes.Orange
                : Brushes.IndianRed;

        TxtStart.Text = FormatDate(order.ProductionStart);
        TxtEnd.Text = FormatDate(order.ProductionEnd);
        TxtLoreal.Text = ToYesNo(order.Loreal);
        TxtNotes.Text = string.IsNullOrWhiteSpace(order.Notes)
            ? T("QO.Text.NoOrderNotes")
            : order.Notes;

        ChkTesaTest.IsChecked = order.TesaTest;
        ChkAcetoneTest.IsChecked = order.AcetoneTest;
        ChkGridTest.IsChecked = order.GridTest;
        ChkVisualCheck.IsChecked = order.VisualCheck;
        ChkApproved.IsChecked = order.Approved;
        TxtReleaseNotes.Text = order.ReleaseNotes;
    }

    private void BtnRelease_Click(object sender, RoutedEventArgs e)
    {
        ChangeReleaseState(true);
    }

    private void BtnBlock_Click(object sender, RoutedEventArgs e)
    {
        ChangeReleaseState(false);
    }

    private void ChangeReleaseState(bool released)
    {
        if (_order is null)
        {
            return;
        }

        if (released &&
            (ChkTesaTest.IsChecked != true ||
             ChkAcetoneTest.IsChecked != true ||
             ChkGridTest.IsChecked != true ||
             ChkVisualCheck.IsChecked != true ||
             ChkApproved.IsChecked != true))
        {
            ShowWarning(T("QO06.Warning.ReleaseChecksRequired"));
            return;
        }

        var title = released
            ? T("QO06.Dialog.Release.Title")
            : T("QO06.Dialog.Block.Title");
        var message = released
            ? TF("QO06.Dialog.Release.Message", _order.OrderNumber)
            : TF("QO06.Dialog.Block.Message", _order.OrderNumber);

        var answer = DmsConfirmDialog.Show(
            Window.GetWindow(this),
            title,
            message,
            DmsDialogButtons.YesNo);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var oldValue = _order.Released.ToString();
        var result = _service.SetReleased(
            _order.OrderNumber,
            released,
            _currentUserName,
            ChkTesaTest.IsChecked == true,
            ChkAcetoneTest.IsChecked == true,
            ChkGridTest.IsChecked == true,
            ChkVisualCheck.IsChecked == true,
            ChkApproved.IsChecked == true,
            TxtReleaseNotes.Text);

        if (!result.Success || result.SavedOrder is null)
        {
            ShowWarning(result.Message);
            return;
        }

        _logger?.AuditChange(
            "QUALITY",
            "QualityOrder",
            result.SavedOrder.OrderNumber,
            "Released",
            oldValue,
            result.SavedOrder.Released.ToString(),
            _currentUserName);

        if (released)
        {
            _logger?.AuditChange(
                "QUALITY",
                "QualityOrder",
                result.SavedOrder.OrderNumber,
                "ReleaseChecks",
                string.Empty,
                $"TesaTest={result.SavedOrder.TesaTest}; AcetoneTest={result.SavedOrder.AcetoneTest}; GridTest={result.SavedOrder.GridTest}; VisualCheck={result.SavedOrder.VisualCheck}; Approved={result.SavedOrder.Approved}; Notes={result.SavedOrder.ReleaseNotes}",
                _currentUserName);
        }

        _logger?.AdminAction(
            "QO06",
            released ? "ReleaseQualityOrder" : "BlockQualityOrder",
            _currentUserName,
            $"Order={result.SavedOrder.OrderNumber}; Released={result.SavedOrder.Released}");

        _order = result.SavedOrder;
        FillOrder(_order);
    }

    private void BtnOpenQO02_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_orderNumber))
        {
            TransactionRequested?.Invoke($"QO02 {_orderNumber}");
        }
    }

    private void BtnOpenQO03_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_orderNumber))
        {
            TransactionRequested?.Invoke($"QO03 {_orderNumber}");
        }
    }

    private void ShowWarning(string text)
    {
        TxtWarning.Text = text;
        WarningPanel.Visibility = Visibility.Visible;
    }

    private void ClearWarning()
    {
        TxtWarning.Text = string.Empty;
        WarningPanel.Visibility = Visibility.Collapsed;
    }

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;
        return IsMissing(value, key) ? key : value;
    }

    private string TF(string key, params object[] args)
    {
        if (_translateFormat is not null)
        {
            var value = _translateFormat(key, args);
            return IsMissing(value, key) ? key : value;
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

    private static bool IsMissing(string value, string key)
    {
        return string.IsNullOrWhiteSpace(value) ||
               string.Equals(value, key, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }

    private string ToYesNo(bool value)
    {
        return value ? T("Common.Yes") : T("Common.No");
    }

    private static string FormatDate(DateTime? date)
    {
        return date?.ToString("dd.MM.yyyy") ?? "-";
    }
}
