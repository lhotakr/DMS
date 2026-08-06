using DMS.Core.Sap;
using DMS.Desktop.Views.Sap;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderRecipeOverview(string recipeFilter = "")
    {
        WorkspacePanel.Children.Clear();

        var storagePaths = new SapStoragePaths(GetDmsDataRootPath());

        _logger.AdminAction(
            "REC03",
            "OpenRecipeOverview",
            _currentUser.DisplayName,
            $"RecipeFilter={recipeFilter}; Root={storagePaths.RootDirectory}; " +
            $"Boms={storagePaths.SapBomSnapshotsFilePath}");

        var view = new SapRecipeOverviewView(
            recipeFilter,
            storagePaths,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += ExecuteTransactionFromView;

        WorkspacePanel.Children.Add(view);
        ResetWorkspaceScroll();
    }

    private void ExecuteTransactionFromView(string commandText)
    {
        SetTransactionInputText(commandText);
        ExecuteTransaction(commandText);
    }
}