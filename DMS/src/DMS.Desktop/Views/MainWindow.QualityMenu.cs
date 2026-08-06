using DMS.Desktop.Views.Quality;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualityMenu()
    {
        WorkspacePanel.Children.Clear();
        WorkspacePanel.Children.Add(new QualityMenuView(
            ExecuteTransaction,
            code =>
            {
                var allowed = UserCanExecuteTransaction(code, out var message);
                return (allowed, allowed ? string.Empty : message);
            }));
        ResetWorkspaceScroll();
    }
}
