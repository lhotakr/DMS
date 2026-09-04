using DMS.Core.Quality;
using DMS.Desktop.UI;
using System.IO;
using System.Windows;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualityArticleEditWithCreatePrompt(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            RenderQualityArticleEdit(query);
            return;
        }

        var paths = new QualityStoragePaths(GetDmsDataRootPath());
        paths.EnsureDirectories();

        var service = new QualityArticleEditService(
            new JsonQualityRepository(paths));

        if (service.Load(query) is not null)
        {
            RenderQualityArticleEdit(query);
            return;
        }

        var result = DmsConfirmDialog.Show(
            this,
            T("QA02.Smart.NotFound.Title"),
            T("QA02.Smart.NotFound.Message", query),
            DmsDialogButtons.YesNo);

        _logger.AdminAction(
            "QA02",
            "QualityDataMissingCreatePrompt",
            _currentUser.DisplayName,
            $"Query={query}; Result={result}");

        if (result == MessageBoxResult.Yes)
        {
            ExecuteTransaction($"QA01 {query}");
            return;
        }

        RenderSimplePage(
            T("QA02.Smart.NotFound.Title"),
            T("QA02.Smart.NotFound.Cancelled", query));
    }
}
