using DMS.Core.Sap;
using DMS.Desktop.Views.Sap;
using DMS.Desktop.Views.Recipes;

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

    private void RenderRecipeImport()
    {
        WorkspacePanel.Children.Clear();
        var storagePaths = new SapStoragePaths(GetDmsDataRootPath());
        var view = new RecipeImportView(
            storagePaths,
            _logger,
            _currentUser.DisplayName,
            _currentUser.HasRole("DMS_ADMIN"),
            translate: key => T(key));
        WorkspacePanel.Children.Add(view);
        ResetWorkspaceScroll();
    }
    private void ExecuteTransactionFromView(string commandText)
    {
        SetTransactionInputText(commandText);
        ExecuteTransaction(commandText);
    }
}