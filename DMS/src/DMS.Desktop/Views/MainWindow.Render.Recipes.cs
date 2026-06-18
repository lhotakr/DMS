using DMS.Desktop.Views.Sap;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderRecipeOverview(string recipeFilter = "")
    {
        WorkspacePanel.Children.Clear();

        var view = new SapRecipeOverviewView(recipeFilter);

        view.TransactionRequested += ExecuteTransactionFromView;

        WorkspacePanel.Children.Add(view);

        ResetWorkspaceScroll();
    }
    private void ExecuteTransactionFromView(string commandText)
    {
        TxtTransaction.Text = commandText;
        ExecuteTransaction(commandText);
    }

}