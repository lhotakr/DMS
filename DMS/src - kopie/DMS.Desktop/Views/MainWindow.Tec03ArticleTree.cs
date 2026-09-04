using DMS.Core.Sap;
using DMS.Desktop.Views.Technology;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    /// <summary>
    /// Keeps the existing TEC03 renderer untouched, then moves its already-built
    /// visual content into a Summary tab and adds the read-only plant-9200 article
    /// graph as a second tab.
    /// </summary>
    private void RenderTechnicalArticleSummaryWithTree(string articleNumber)
    {
        RenderTechnicalArticleSummary(articleNumber);

        if (string.IsNullOrWhiteSpace(articleNumber))
        {
            return;
        }

        try
        {
            var summaryChildren = WorkspacePanel.Children
                .Cast<UIElement>()
                .ToList();

            WorkspacePanel.Children.Clear();

            UIElement summaryContent;
            if (summaryChildren.Count == 1)
            {
                summaryContent = summaryChildren[0];
            }
            else
            {
                var summaryPanel = new StackPanel();
                foreach (var child in summaryChildren)
                {
                    summaryPanel.Children.Add(child);
                }

                summaryContent = summaryPanel;
            }

            var configurationRoot = _appSettings.ConfigurationRootPath;
            var dataRoot = ResolveTec03ArticleTreeSapDataRoot(configurationRoot);
            var storagePaths = new SapStoragePaths(dataRoot);

            var treeView = new Tec03ArticleTreeView(
                articleNumber,
                storagePaths,
                _logger,
                _currentUser.DisplayName,
                translate: key => T(key),
                translateFormat: (key, args) => T(key, args));

            treeView.TransactionRequested += command => ExecuteTransaction(command);

            var tabs = new TabControl
            {
                Margin = new Thickness(0),
                BorderThickness = new Thickness(0)
            };

            tabs.SetResourceReference(Control.BackgroundProperty, "DmsPanelBrush");
            tabs.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");

            var summaryTab = new TabItem
            {
                Header = Tec03ArticleTreeText("TEC03.Tree.Tab.Summary", "Souhrn"),
                Content = summaryContent
            };

            var treeTab = new TabItem
            {
                Header = Tec03ArticleTreeText("TEC03.Tree.Tab.Tree", "Strom artiklu"),
                Content = treeView
            };

            tabs.Items.Add(summaryTab);
            tabs.Items.Add(treeTab);
            tabs.SelectedIndex = 0;

            WorkspacePanel.Children.Add(tabs);

            _logger.Info(
                $"TX_START TEC03_ARTICLE_TREE; Article={articleNumber}; Plant=9200; User={_currentUser.DisplayName}");

            ResetWorkspaceScroll();
        }
        catch (Exception ex)
        {
            _logger.Info(
                $"TX_FAIL TEC03_ARTICLE_TREE; Article={articleNumber}; User={_currentUser.DisplayName}; Error={ex.Message}");

            // If tab composition itself fails, preserve the original TEC03 instead
            // of breaking the transaction. Re-render the unchanged summary.
            RenderTechnicalArticleSummary(articleNumber);
        }
    }

    private string Tec03ArticleTreeText(string key, string fallback)
    {
        var value = T(key);

        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        return value;
    }

    private static string ResolveTec03ArticleTreeSapDataRoot(string? configurationRoot)
    {
        if (string.IsNullOrWhiteSpace(configurationRoot))
        {
            return AppContext.BaseDirectory;
        }

        var fullPath = Path.GetFullPath(configurationRoot);
        var trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(
                Path.GetFileName(trimmed),
                "Config",
                StringComparison.OrdinalIgnoreCase))
        {
            return Directory.GetParent(trimmed)?.FullName ?? trimmed;
        }

        return trimmed;
    }
}
