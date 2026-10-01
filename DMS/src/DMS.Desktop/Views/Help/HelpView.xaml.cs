using DMS.Desktop.Help;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Help;

public partial class HelpView : UserControl
{
    private readonly DmsHelpCatalog _catalog;
    private readonly Action<string> _executeTransaction;
    private readonly Action<string, string>? _logHelpAction;
    private readonly bool _showTechnicalInfo;
    private DmsHelpDocument? _currentDocument;
    private string _initialTopic;

    public HelpView(
        DmsHelpCatalog catalog,
        Action<string> executeTransaction,
        string initialTopic = "",
        bool showTechnicalInfo = false,
        Action<string, string>? logHelpAction = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _executeTransaction = executeTransaction ?? throw new ArgumentNullException(nameof(executeTransaction));
        _initialTopic = initialTopic ?? string.Empty;
        _showTechnicalInfo = showTechnicalInfo;
        _logHelpAction = logHelpAction;

        InitializeComponent();

        ApplyStaticText();
        RebuildTree();
    }

    private void ApplyStaticText()
    {
        var czech = _catalog.ActiveCulture.StartsWith("cs", StringComparison.OrdinalIgnoreCase);
        TxtTitle.Text = czech ? "HELP - Dokumentace DMS" : "HELP - DMS Documentation";
        TxtSubtitle.Text = czech
            ? "Uživatelská příručka a dokumentace transakcí přímo v systému DMS."
            : "User guide and transaction documentation directly inside DMS.";
        TxtSearch.ToolTip = czech ? "Hledat v dokumentaci" : "Search documentation";
        BtnRunTransaction.Content = czech ? "Spustit transakci" : "Run transaction";
        TxtHint.Text = czech
            ? "Tip: F1 otevře kontextovou nápovědu k právě používané transakci v samostatném okně. Odkazy v textu mohou rovnou spouštět související transakce."
            : "Tip: F1 opens contextual help for the current transaction in a separate window. Links in the text can start related transactions directly.";
    }

    private void RebuildTree()
    {
        var query = TxtSearch.Text?.Trim();
        var documents = _catalog.Search(query);

        TreeTopics.Items.Clear();

        foreach (var categoryGroup in documents
                     .GroupBy(item => item.Category, StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(group => group.First().IsTransaction ? 1 : 0)
                     .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var categoryItem = new TreeViewItem
            {
                Header = categoryGroup.Key,
                IsExpanded = !string.IsNullOrWhiteSpace(query)
            };

            foreach (var document in categoryGroup
                         .OrderBy(item => item.SortOrder)
                         .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase))
            {
                categoryItem.Items.Add(new TreeViewItem
                {
                    Header = document.Title,
                    Tag = document.TopicId
                });
            }

            TreeTopics.Items.Add(categoryItem);
        }

        TxtCount.Text = _catalog.ActiveCulture.StartsWith("cs", StringComparison.OrdinalIgnoreCase)
            ? $"Dokumentů: {documents.Count}"
            : $"Documents: {documents.Count}";

        var topicToSelect = !string.IsNullOrWhiteSpace(_initialTopic)
            ? _initialTopic
            : _currentDocument?.TopicId ?? "GUIDE-START";

        if (!TrySelectTopic(topicToSelect) && documents.Count > 0)
        {
            ShowDocument(documents[0].TopicId);
        }

        _initialTopic = string.Empty;
    }

    private bool TrySelectTopic(string? topicId)
    {
        if (string.IsNullOrWhiteSpace(topicId))
        {
            return false;
        }

        foreach (var root in TreeTopics.Items.OfType<TreeViewItem>())
        {
            foreach (var child in root.Items.OfType<TreeViewItem>())
            {
                if (!string.Equals(child.Tag?.ToString(), topicId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                root.IsExpanded = true;
                child.IsSelected = true;
                child.BringIntoView();
                return true;
            }
        }

        return false;
    }

    private void ShowDocument(string? topicId)
    {
        var document = _catalog.GetDocument(topicId);
        if (document is null)
        {
            return;
        }

        _currentDocument = document;
        TxtDocumentTitle.Text = document.Title;
        DocViewer.Document = DmsMarkdownFlowDocumentRenderer.Render(document.Markdown, ExecuteFromDocumentation);

        BtnRunTransaction.Visibility = document.IsTransaction && !string.IsNullOrWhiteSpace(document.TransactionCode)
            ? Visibility.Visible
            : Visibility.Collapsed;
        BtnRunTransaction.Tag = document.TransactionCode;

        if (_showTechnicalInfo)
        {
            TechnicalInfoBorder.Visibility = Visibility.Visible;
            TxtTechnicalInfo.Text =
                $"Topic: {document.TopicId}\n" +
                $"Culture: {document.Culture}\n" +
                $"Generated: {document.IsGenerated}\n" +
                $"Transaction: {document.TransactionCode}\n" +
                $"Source: {(string.IsNullOrWhiteSpace(document.SourcePath) ? "runtime/generated" : document.SourcePath)}";
        }
        else
        {
            TechnicalInfoBorder.Visibility = Visibility.Collapsed;
        }

        _logHelpAction?.Invoke(
            "OpenDocumentationTopic",
            $"Topic={document.TopicId}; Generated={document.IsGenerated}; Culture={document.Culture}");
    }

    private void ExecuteFromDocumentation(string command)
    {
        _logHelpAction?.Invoke("RunTransactionFromDocumentation", $"Command={command}");
        _executeTransaction(command);
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        RebuildTree();
    }

    private void TreeTopics_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag is string topic)
        {
            ShowDocument(topic);
        }
    }

    private void BtnRunTransaction_Click(object sender, RoutedEventArgs e)
    {
        var command = BtnRunTransaction.Tag?.ToString();
        if (!string.IsNullOrWhiteSpace(command))
        {
            ExecuteFromDocumentation(command);
        }
    }
}
