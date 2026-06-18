using DMS.Desktop.UI;
using DMS.Desktop.Views.Quality;
using System.Linq;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualityArticleEdit(string query)
    {
        // Kontrola musí být před odstraněním aktuální obrazovky.
        if (!CanLeaveCurrentWorkspace())
        {
            return;
        }

        WorkspacePanel.Children.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            RenderSimplePage(
                "QA02 - Změna quality dat",
                "Zadej SAP číslo nebo celé číslo tiskové verze.");

            return;
        }

        var view = new QualityArticleEditView(query);

        view.TransactionRequested += ExecuteTransactionFromView;

        WorkspacePanel.Children.Add(view);

        ResetWorkspaceScroll();
    }

    private bool CanLeaveCurrentWorkspace()
    {
        var guard = WorkspacePanel.Children
            .OfType<IUnsavedChangesGuard>()
            .FirstOrDefault();

        return guard?.ConfirmNavigationAway() ?? true;
    }
}