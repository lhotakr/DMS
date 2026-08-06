using DMS.Desktop.Views.QualityOrders;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualityOrderCreate(string query)
    {
        WorkspacePanel.Children.Clear();

        var view = new QualityOrderFormView(
            query,
            GetDmsDataRootPath(),
            createMode: true,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += ExecuteTransactionFromView;
        WorkspacePanel.Children.Add(view);

        _logger.AdminAction(
            "QO01",
            "OpenQualityOrderCreate",
            _currentUser.DisplayName,
            $"Query={query}; Root={GetDmsDataRootPath()}");

        ResetWorkspaceScroll();
    }

    private void RenderQualityOrderEdit(string query)
    {
        WorkspacePanel.Children.Clear();

        var view = new QualityOrderFormView(
            query,
            GetDmsDataRootPath(),
            createMode: false,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += ExecuteTransactionFromView;
        WorkspacePanel.Children.Add(view);

        _logger.AdminAction(
            "QO02",
            "OpenQualityOrderEdit",
            _currentUser.DisplayName,
            $"Query={query}; Root={GetDmsDataRootPath()}");

        ResetWorkspaceScroll();
    }

    private void RenderQualityOrderDisplay(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            RenderQualityOrderPicker(
                targetTransaction: "QO03",
                blockedOnly: false);
            return;
        }

        WorkspacePanel.Children.Clear();

        var view = new QualityOrderView(
            query,
            GetDmsDataRootPath(),
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += ExecuteTransactionFromView;
        WorkspacePanel.Children.Add(view);

        _logger.AdminAction(
            "QO03",
            "OpenQualityOrderDisplay",
            _currentUser.DisplayName,
            $"Query={query}; Root={GetDmsDataRootPath()}");

        ResetWorkspaceScroll();
    }

    private void RenderQualityOrderList()
    {
        WorkspacePanel.Children.Clear();

        var view = new QualityOrderListView(
            GetDmsDataRootPath(),
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += ExecuteTransactionFromView;
        WorkspacePanel.Children.Add(view);

        _logger.AdminAction(
            "QO05",
            "OpenQualityOrderOverview",
            _currentUser.DisplayName,
            $"Root={GetDmsDataRootPath()}");

        ResetWorkspaceScroll();
    }

    private void RenderQualityOrderRelease(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            RenderQualityOrderPicker(
                targetTransaction: "QO06",
                blockedOnly: true);
            return;
        }

        WorkspacePanel.Children.Clear();

        var view = new QualityOrderReleaseView(
            query,
            GetDmsDataRootPath(),
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += ExecuteTransactionFromView;
        WorkspacePanel.Children.Add(view);

        _logger.AdminAction(
            "QO06",
            "OpenQualityOrderReleaseBlock",
            _currentUser.DisplayName,
            $"Query={query}; Root={GetDmsDataRootPath()}");

        ResetWorkspaceScroll();
    }

    private void RenderQualityOrderPicker(
        string targetTransaction,
        bool blockedOnly)
    {
        WorkspacePanel.Children.Clear();

        var view = new QualityOrderPickerView(
            targetTransaction,
            blockedOnly,
            GetDmsDataRootPath(),
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += ExecuteTransactionFromView;
        WorkspacePanel.Children.Add(view);

        _logger.AdminAction(
            targetTransaction,
            "OpenQualityOrderPicker",
            _currentUser.DisplayName,
            $"Target={targetTransaction}; BlockedOnly={blockedOnly}; Root={GetDmsDataRootPath()}");

        ResetWorkspaceScroll();
    }
}
