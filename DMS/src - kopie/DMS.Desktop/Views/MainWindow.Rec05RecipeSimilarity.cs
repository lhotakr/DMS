using DMS.Core.Sap;
using DMS.Desktop.Views.Sap;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    /// <summary>
    /// REC05 is intentionally a thin shell wrapper. All recipe comparison logic lives
    /// in DMS.Core.Sap and the WPF rendering stays in SapRecipeSimilarityView.
    /// </summary>
    private void RenderRecipeSimilarityAnalysis()
    {
        WorkspacePanel.Children.Clear();

        var storagePaths = new SapStoragePaths(GetDmsDataRootPath());
        var view = new SapRecipeSimilarityView(
            storagePaths,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += command => ExecuteTransaction(command);

        WorkspacePanel.Children.Add(view);
        ResetWorkspaceScroll();
    }
}
