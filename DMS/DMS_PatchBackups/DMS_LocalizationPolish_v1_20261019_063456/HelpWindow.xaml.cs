using DMS.Desktop.Help;
using DMS.Desktop.UI.FunctionKeys;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Help;

public partial class HelpWindow : Window
{
    private readonly DmsHelpCatalog _catalog;
    private readonly Action<string> _executeTransaction;
    private readonly Action<string> _openDocumentation;
    private readonly bool _showTechnicalInfo;
    private string _currentTopicId = string.Empty;

    public HelpWindow(
        DmsHelpCatalog catalog,
        Action<string> executeTransaction,
        Action<string> openDocumentation,
        bool showTechnicalInfo)
    {
        InitializeComponent();

        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _executeTransaction = executeTransaction ?? throw new ArgumentNullException(nameof(executeTransaction));
        _openDocumentation = openDocumentation ?? throw new ArgumentNullException(nameof(openDocumentation));
        _showTechnicalInfo = showTechnicalInfo;
    }

    public void ShowTopic(
        string topicId,
        string currentCommand,
        IReadOnlyList<DmsFunctionKeyAction> functionKeyActions)
    {
        var document = _catalog.GetDocument(topicId)
                       ?? _catalog.GetDocument("GUIDE-START");

        if (document is null)
        {
            TxtTopic.Text = topicId;
            DocViewer.Document = DmsMarkdownFlowDocumentRenderer.Render(
                "# DMS Help\n\nNo documentation is available for this topic.");
            return;
        }

        _currentTopicId = document.TopicId;
        Title = $"DMS Help - {document.Title}";
        TxtWindowTitle.Text = "Nápověda DMS";
        TxtTopic.Text = document.Title;
        DocViewer.Document = DmsMarkdownFlowDocumentRenderer.Render(
            document.Markdown,
            _executeTransaction);

        RenderFunctionKeys(functionKeyActions);
        RenderTechnicalInfo(document, currentCommand);
    }

    private void RenderFunctionKeys(IReadOnlyList<DmsFunctionKeyAction> actions)
    {
        FunctionKeyItems.Items.Clear();

        foreach (var action in actions.Where(item => item.IsEnabled))
        {
            FunctionKeyItems.Items.Add(new Border
            {
                Margin = new Thickness(0, 0, 8, 6),
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = (System.Windows.Media.Brush)FindResource("DmsBorderBrush"),
                Child = new TextBlock
                {
                    Text = $"{action.Key}  {action.Label}",
                    Foreground = (System.Windows.Media.Brush)FindResource("DmsForegroundBrush")
                }
            });
        }
    }

    private void RenderTechnicalInfo(DmsHelpDocument document, string currentCommand)
    {
        if (!_showTechnicalInfo)
        {
            TechnicalInfoBorder.Visibility = Visibility.Collapsed;
            return;
        }

        TechnicalInfoBorder.Visibility = Visibility.Visible;
        TxtTechnicalInfo.Text =
            $"Topic: {document.TopicId}\n" +
            $"Culture: {document.Culture}\n" +
            $"Current command: {currentCommand}\n" +
            $"Generated: {document.IsGenerated}\n" +
            $"Source: {(string.IsNullOrWhiteSpace(document.SourcePath) ? "runtime/generated" : document.SourcePath)}";
    }

    private void BtnOpenDocumentation_Click(object sender, RoutedEventArgs e)
    {
        _openDocumentation(_currentTopicId);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
