using DMS.Core.Transactions;
using DMS.Desktop.Help;
using DMS.Desktop.Views.Help;
using System.IO;
using System.Windows;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private HelpWindow? _contextHelpWindow;

    private DmsHelpCatalog CreateHelpCatalog()
    {
        var docsRoot = Path.Combine(AppContext.BaseDirectory, "Docs", "Help");

        return new DmsHelpCatalog(
            docsRoot,
            _localizationService.ActiveCulture,
            GetVisibleTransactionDefinitions(),
            key => T(key));
    }

    private void RenderHelp(string initialTopic = "")
    {
        WorkspacePanel.Children.Clear();

        var catalog = CreateHelpCatalog();
        var topic = NormalizeHelpTopic(initialTopic);

        WorkspacePanel.Children.Add(new HelpView(
            catalog,
            executeTransaction: ExecuteTransaction,
            initialTopic: topic,
            showTechnicalInfo: _userSettings.DocumentationTechnicalInfoEnabled,
            logHelpAction: (action, details) =>
            {
                _logger.AdminAction(
                    "HELP",
                    action,
                    _currentUser.DisplayName,
                    details);
            }));

        _logger.AdminAction(
            "HELP",
            "OpenDocumentationCenter",
            _currentUser.DisplayName,
            $"Topic={topic}; Culture={catalog.ActiveCulture}; Technical={_userSettings.DocumentationTechnicalInfoEnabled}");

        ResetWorkspaceScroll();
    }

    private void ShowContextHelpWindow()
    {
        if (!_userSettings.DocumentationHelpEnabled)
        {
            return;
        }

        var topic = GetCurrentHelpTopic();
        var catalog = CreateHelpCatalog();

        if (_contextHelpWindow is null || !_contextHelpWindow.IsLoaded)
        {
            _contextHelpWindow = new HelpWindow(
                catalog,
                executeTransaction: ExecuteTransaction,
                openDocumentation: OpenFullDocumentationFromHelpWindow,
                showTechnicalInfo: _userSettings.DocumentationTechnicalInfoEnabled)
            {
                Owner = this,
                ShowInTaskbar = false
            };

            _contextHelpWindow.Closed += (_, _) => _contextHelpWindow = null;
            _contextHelpWindow.Show();
        }

        var actions = GetEffectiveFunctionKeyActions();
        _contextHelpWindow.ShowTopic(
            topic,
            _currentTransactionCommand ?? string.Empty,
            actions);

        if (_contextHelpWindow.WindowState == WindowState.Minimized)
        {
            _contextHelpWindow.WindowState = WindowState.Normal;
        }

        _contextHelpWindow.Activate();

        _logger.AdminAction(
            "HELP",
            "OpenContextHelp",
            _currentUser.DisplayName,
            $"Topic={topic}; Command={_currentTransactionCommand}; Culture={catalog.ActiveCulture}; Technical={_userSettings.DocumentationTechnicalInfoEnabled}");
    }

    private void OpenFullDocumentationFromHelpWindow(string topicId)
    {
        var topic = NormalizeHelpTopic(topicId);
        ExecuteTransaction(string.IsNullOrWhiteSpace(topic) ? "HELP" : $"HELP {topic}");
    }

    private string GetCurrentHelpTopic()
    {
        if (string.IsNullOrWhiteSpace(_currentTransactionCommand))
        {
            return "GUIDE-START";
        }

        var parsed = TransactionParser.Parse(_currentTransactionCommand);
        return string.IsNullOrWhiteSpace(parsed.Code) ? "GUIDE-START" : parsed.Code;
    }

    private static string NormalizeHelpTopic(string? topic)
    {
        return string.IsNullOrWhiteSpace(topic)
            ? string.Empty
            : topic.Trim().Replace(' ', '-').ToUpperInvariant();
    }
}
