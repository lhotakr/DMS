using DMS.Core.Quality;

namespace DMS.Desktop.Views.Orders;

public sealed partial class MesOrderHistoryView
{
    private string _mesordDataRoot = string.Empty;

    private string Qa9200ForMes()
    {
        if (string.IsNullOrWhiteSpace(_mesordDataRoot)) return MesordText("Zatím nedoplněno (doplní kvalita)");
        try
        {
            var repo = new JsonQualityRepository(new QualityStoragePaths(_mesordDataRoot));
            var order = repo.LoadOrders().FirstOrDefault(o =>
                string.Equals(o.MesOrderNumber2000, _orderNumber, StringComparison.OrdinalIgnoreCase));
            return !string.IsNullOrWhiteSpace(order?.OrderNumber)
                ? order.OrderNumber : MesordText("Zatím nedoplněno (doplní kvalita)");
        }
        catch (Exception e)
        {
            _logger.Error($"MESORD QA link lookup failed. Order2000={_orderNumber}", e);
            return MesordText("Zatím nedoplněno (doplní kvalita)");
        }
    }
}
