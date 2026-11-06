using DMS.Integration.Mes.Orders;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DMS.Desktop.Views.Orders;

public partial class OrderOverviewView
{
    private void GridOrders_MesOrderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_openMesOrder == null) return;
        var node = e.OriginalSource as DependencyObject;
        while (node != null && node is not DataGridRow)
            node = VisualTreeHelper.GetParent(node);
        if (node is DataGridRow row && row.Item is MesProductionOrderRecord order &&
            !string.IsNullOrWhiteSpace(order.OrderCode))
        {
            _openMesOrder(order.OrderCode);
            e.Handled=true;
        }
    }
}
