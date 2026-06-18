using DMS.Desktop.Views.Quality;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualityArticle(string query)
    {
        WorkspacePanel.Children.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            RenderSimplePage(
                "QA03 - Quality karta",
                "Zadej SAP číslo, tiskovou verzi nebo historické číslo artiklu.\n\n" +
                "Příklady:\n" +
                "QA03 1000013206\n" +
                "QA03 0114025101.99001\n" +
                "QA03 0114025");
            return;
        }

        var view = new QualityArticleView(query);

        var canEditQuality = _currentUser.Roles.Any(role =>
            role.Equals("DMS_ADMIN", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("DMS_QUALITY_EDIT", StringComparison.OrdinalIgnoreCase));

        view.TransactionRequested += ExecuteTransactionFromView;

        WorkspacePanel.Children.Add(view);

        ResetWorkspaceScroll();
    }
}