using DMS.Desktop.Views.Logs;
using System.Globalization;
using System.Windows.Controls;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderLogViewer()
    {
        WorkspacePanel.Children.Clear();

        WorkspaceScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        WorkspaceScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

        _logger.AdminAction(
            "LOG03",
            "OpenLogViewer",
            _currentUser.DisplayName,
            $"LogsRootPath={_appSettings.LogsRootPath}");

        var view = new LogViewerView(
            _appSettings.LogsRootPath,
            _logReader,
            key => T(key),
            (key, args) => T(key, args),
            (action, details) =>
            {
                _logger.AdminAction(
                    "LOG03",
                    action,
                    _currentUser.DisplayName,
                    details);
            },
            GetCurrentLogViewerCultureName());

        WorkspacePanel.Children.Add(view);

        ResetWorkspaceScroll();
    }

    private string GetCurrentLogViewerCultureName()
    {
        if (string.Equals(
                _userSettings.LanguageMode,
                "Manual",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_userSettings.CultureName))
        {
            return _userSettings.CultureName;
        }

        return CultureInfo.CurrentUICulture.Name;
    }
}
