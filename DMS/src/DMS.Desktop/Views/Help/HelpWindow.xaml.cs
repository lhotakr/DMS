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
    private readonly Func<string, string> _translate;
    private string _currentTopicId = string.Empty;

    public HelpWindow(
        DmsHelpCatalog catalog,
        Action<string> executeTransaction,
        Action<string> openDocumentation,
        Func<string, string> translate,
        bool showTechnicalInfo)
    {
        InitializeComponent();

        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _executeTransaction = executeTransaction ?? throw new ArgumentNullException(nameof(executeTransaction));
        _openDocumentation = openDocumentation ?? throw new ArgumentNullException(nameof(openDocumentation));
        _translate = translate ?? throw new ArgumentNullException(nameof(translate));
        _showTechnicalInfo = showTechnicalInfo;

        ApplyLocalization();
    }

    private string TOr(
        string key,
        string fallback)
    {
        var value = _translate(key);
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.Ordinal)
               || value.StartsWith("[[", StringComparison.Ordinal)
            ? fallback
            : value;
    }

    private void ApplyLocalization()
    {
        TxtWindowTitle.Text = TOr("Help.Window.Heading", "DMS Help");
        BtnOpenDocumentation.Content = TOr("Help.Window.OpenDocumentation", "Open full documentation");
        BtnClose.Content = TOr("Help.Window.Close", "Close");
        Title = TOr("Help.Window.Title", "DMS Help");
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
                "# DMS Help\n\n" + TOr("Help.Window.NoDocumentation", "No documentation is available for this topic."));
            return;
        }

        _currentTopicId = document.TopicId;
        Title = $"DMS Help - {document.Title}";
        TxtWindowTitle.Text = TOr("Help.Window.Heading", "DMS Help");
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
            $"{TOr("Help.Technical.Topic", "Topic")}: {document.TopicId}\n" +
            $"{TOr("Help.Technical.Culture", "Culture")}: {document.Culture}\n" +
            $"{TOr("Help.Technical.CurrentCommand", "Current command")}: {currentCommand}\n" +
            $"{TOr("Help.Technical.Generated", "Generated")}: {document.IsGenerated}\n" +
            $"{TOr("Help.Technical.Source", "Source")}: {(string.IsNullOrWhiteSpace(document.SourcePath) ? TOr("Help.Technical.RuntimeGenerated", "runtime/generated") : document.SourcePath)}";
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
