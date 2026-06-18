using DMS.Desktop.Views.Sap;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderMaterialUsage(string materialNumber)
    {
        WorkspacePanel.Children.Clear();

        if (string.IsNullOrWhiteSpace(materialNumber))
        {
            RenderSimplePage(
                "MAT03 - Použití materiálu",
                "Zadej SAP číslo materiálu, například MAT03 1700001045.");
            return;
        }

        var view = new SapMaterialUsageView(materialNumber);

        view.TransactionRequested += ExecuteTransactionFromView;

        WorkspacePanel.Children.Add(view);

        ResetWorkspaceScroll();
    }
}