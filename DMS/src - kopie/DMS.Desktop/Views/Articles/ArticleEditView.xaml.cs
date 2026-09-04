using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DMS.Core.Articles;
using DMS.Desktop.Models;

namespace DMS.Desktop.Views.Articles;

public partial class ArticleEditView : UserControl
{
    private readonly Action<DmsArticle> _saveArticle;
    private readonly string _currentUserName;
    private readonly bool _isNew;
    private readonly DmsArticle? _originalArticle;

    private readonly ObservableCollection<ArticleDocumentLink> _documents = new();
    private readonly ObservableCollection<ArticleOperation> _operations = new();
    private readonly ObservableCollection<ArticleTechnologyLink> _technologyLinks = new();
    private readonly ObservableCollection<ArticleScreenData> _screens = new();
    private readonly ObservableCollection<ArticleFlowLink> _downstreamArticles = new();

    public ArticleEditView(
        DmsArticle? article,
        Action<DmsArticle> saveArticle,
        string currentUserName)
    {
        InitializeComponent();

        _saveArticle = saveArticle;
        _currentUserName = currentUserName;
        _isNew = article is null;
        _originalArticle = article;

        LoadArticle(article ?? CreateNewArticle());

        TxtTitle.Text = _isNew
            ? "Založení artiklu"
            : $"Změna artiklu {article!.SapArticleNumber}";
    }

    private static DmsArticle CreateNewArticle()
    {
        return new DmsArticle
        {
            CreatedAt = DateTime.Now
        };
    }

    private void LoadArticle(DmsArticle article)
    {
        TxtSapArticleNumber.Text = article.SapArticleNumber;
        TxtDescription.Text = article.Description;
        TxtOldMaterialNumber.Text = article.OldMaterialNumber;
        TxtMaterialStatusCode.Text = article.MaterialStatusCode;
        TxtDecorationCode.Text = article.DecorationCode;

        foreach (var item in article.Documents)
        {
            _documents.Add(item);
        }

        foreach (var item in article.Operations)
        {
            _operations.Add(item);
        }

        foreach (var item in article.TechnologyLinks)
        {
            _technologyLinks.Add(item);
        }

        foreach (var item in article.Screens)
        {
            _screens.Add(item);
        }

        foreach (var item in article.DownstreamArticles)
        {
            _downstreamArticles.Add(item);
        }

        if (article.UpstreamArticle is not null)
        {
            TxtUpstreamArticleNumber.Text = article.UpstreamArticle.RelatedArticleNumber;
            TxtUpstreamArticleDescription.Text = article.UpstreamArticle.RelatedArticleDescription;
            TxtUpstreamRelationKind.Text = article.UpstreamArticle.RelationKind;
            TxtUpstreamNote.Text = article.UpstreamArticle.Note;
        }

        DgvDocuments.ItemsSource = _documents;
        DgvOperations.ItemsSource = _operations;
        DgvTechnologyLinks.ItemsSource = _technologyLinks;
        DgvScreens.ItemsSource = _screens;
        DgvDownstreamArticles.ItemsSource = _downstreamArticles;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var sapArticleNumber = TxtSapArticleNumber.Text.Trim();

        if (!ArticleNumberValidator.IsValid(sapArticleNumber))
        {
            DmsMessage.Show(
                "SAP číslo artiklu musí být desetimístné číslo.",
                "DMS - artikl",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            TxtSapArticleNumber.Focus();
            TxtSapArticleNumber.SelectAll();
            return;
        }

        var decorationCode = TxtDecorationCode.Text.Trim().ToUpperInvariant();

        var article = new DmsArticle
        {
            SapArticleNumber = sapArticleNumber,
            Description = TxtDescription.Text.Trim(),
            OldMaterialNumber = TxtOldMaterialNumber.Text.Trim(),
            MaterialStatusCode = TxtMaterialStatusCode.Text.Trim(),
            DecorationCode = decorationCode,

            Documents = _documents
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.DocumentTypeCode) ||
                    !string.IsNullOrWhiteSpace(item.FilePath))
                .ToList(),

            Operations = _operations
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.OperationNumber) ||
                    !string.IsNullOrWhiteSpace(item.OperationTypeCode) ||
                    !string.IsNullOrWhiteSpace(item.Name))
                .Select(NormalizeOperation)
                .OrderBy(item => item.OperationNumber)
                .ToList(),

            TechnologyLinks = _technologyLinks
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.OperationNumber) ||
                    !string.IsNullOrWhiteSpace(item.LinkType) ||
                    !string.IsNullOrWhiteSpace(item.ArticleNumber))
                .Select(NormalizeTechnologyLink)
                .OrderBy(item => item.OperationNumber)
                .ThenBy(item => item.Sequence ?? 0)
                .ToList(),

            Screens = _screens
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.OperationNumber) ||
                    !string.IsNullOrWhiteSpace(item.ScreenArticleNumber))
                .Select(NormalizeScreen)
                .OrderBy(item => item.OperationNumber)
                .ThenBy(item => item.PrintPass)
                .ToList(),

            UpstreamArticle = CreateUpstreamArticle(decorationCode),

            DownstreamArticles = _downstreamArticles
                .Where(item => !string.IsNullOrWhiteSpace(item.RelatedArticleNumber))
                .Select(NormalizeFlowLink)
                .OrderBy(item => item.Sequence)
                .ToList(),

            CreatedAt = _isNew
                ? DateTime.Now
                : _originalArticle?.CreatedAt ?? DateTime.Now,

            CreatedBy = _isNew
                ? _currentUserName
                : _originalArticle?.CreatedBy ?? string.Empty,

            ModifiedAt = _isNew
                ? null
                : DateTime.Now,

            ModifiedBy = _isNew
                ? string.Empty
                : _currentUserName
        };

        _saveArticle(article);
    }

    private ArticleFlowLink? CreateUpstreamArticle(string decorationCode)
    {
        var articleNumber = TxtUpstreamArticleNumber.Text.Trim();

        if (string.IsNullOrWhiteSpace(articleNumber))
        {
            return null;
        }

        return new ArticleFlowLink
        {
            RelatedArticleNumber = articleNumber,
            RelatedArticleDescription = TxtUpstreamArticleDescription.Text.Trim(),
            RelationKind = TxtUpstreamRelationKind.Text.Trim(),
            OperationTypeCode = decorationCode,
            Sequence = 1,
            Note = TxtUpstreamNote.Text.Trim()
        };
    }

    private static ArticleOperation NormalizeOperation(ArticleOperation operation)
    {
        operation.OperationNumber = NormalizeOperationNumber(operation.OperationNumber);
        operation.OperationTypeCode = operation.OperationTypeCode.Trim().ToUpperInvariant();
        operation.WorkCenterGroupCode = operation.WorkCenterGroupCode.Trim().ToUpperInvariant();
        operation.Name = operation.Name.Trim();
        operation.ShiftStandardUnit = string.IsNullOrWhiteSpace(operation.ShiftStandardUnit)
            ? "ks/směna"
            : operation.ShiftStandardUnit.Trim();
        operation.Note = operation.Note.Trim();

        return operation;
    }

    private static ArticleTechnologyLink NormalizeTechnologyLink(ArticleTechnologyLink link)
    {
        link.OperationNumber = NormalizeOperationNumber(link.OperationNumber);
        link.LinkType = link.LinkType.Trim().ToUpperInvariant();
        link.SubTypeCode = link.SubTypeCode.Trim().ToUpperInvariant();
        link.ArticleNumber = link.ArticleNumber.Trim();
        link.Description = link.Description.Trim();
        link.Note = link.Note.Trim();

        return link;
    }

    private static ArticleScreenData NormalizeScreen(ArticleScreenData screen)
    {
        screen.OperationNumber = NormalizeOperationNumber(screen.OperationNumber);
        screen.Purpose = screen.Purpose.Trim();
        screen.MachineType = screen.MachineType.Trim().ToUpperInvariant();
        screen.Mesh = screen.Mesh.Trim();
        screen.ScreenArticleNumber = screen.ScreenArticleNumber.Trim();
        screen.Note = screen.Note.Trim();

        return screen;
    }

    private static ArticleFlowLink NormalizeFlowLink(ArticleFlowLink flowLink)
    {
        flowLink.RelatedArticleNumber = flowLink.RelatedArticleNumber.Trim();
        flowLink.RelatedArticleDescription = flowLink.RelatedArticleDescription.Trim();
        flowLink.RelationKind = flowLink.RelationKind.Trim();
        flowLink.OperationTypeCode = flowLink.OperationTypeCode.Trim().ToUpperInvariant();
        flowLink.Note = flowLink.Note.Trim();

        return flowLink;
    }

    private static string NormalizeOperationNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();

        if (int.TryParse(trimmed, out var number))
        {
            return number.ToString("0000");
        }

        return trimmed;
    }

    private void ArticleEditView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = FindParentScrollViewer(this);

        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(
            scrollViewer.VerticalOffset - e.Delta);

        e.Handled = true;
    }

    private static ScrollViewer? FindParentScrollViewer(DependencyObject child)
    {
        var parent = VisualTreeHelper.GetParent(child);

        while (parent is not null)
        {
            if (parent is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }
}