using System.IO;
using System.Windows;
using System.Windows.Input;
using DMS.Desktop.Models;
using DMS.Desktop.Repositories;

namespace DMS.Desktop.Views.Dialogs;

public partial class ArticleSearchDialog : Window
{
    private readonly JsonArticleRepository _articleRepository;
    private readonly IReadOnlyList<MaterialStatus> _materialStatuses;
    private readonly IReadOnlyList<DecorationType> _decorationTypes;

    public string? SelectedArticleNumber { get; private set; }

    public ArticleSearchDialog(JsonArticleRepository articleRepository, string configurationRootPath)
    {
        InitializeComponent();

        _articleRepository = articleRepository;

        var lookupRepository = new JsonLookupRepository();

        _materialStatuses = lookupRepository.LoadList<MaterialStatus>(
            Path.Combine(configurationRootPath, "material-statuses.json"));

        _decorationTypes = lookupRepository.LoadList<DecorationType>(
            Path.Combine(configurationRootPath, "decoration-types.json"));

        Search();
        TxtSapNumber.Focus();
    }

    private void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        Search();
    }

    private void BtnSelect_Click(object sender, RoutedEventArgs e)
    {
        SelectCurrentArticle();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void DgvArticles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectCurrentArticle();
    }

    private string BuildStatusText(string statusCode)
    {
        if (string.IsNullOrWhiteSpace(statusCode))
        {
            return string.Empty;
        }

        var status = _materialStatuses.FirstOrDefault(item =>
            string.Equals(item.Code, statusCode, StringComparison.OrdinalIgnoreCase));

        if (status is null)
        {
            return statusCode;
        }

        return $"{status.Code} - {status.Name}";
    }

    private string BuildDecorationText(string decorationCode)
    {
        if (string.IsNullOrWhiteSpace(decorationCode))
        {
            return string.Empty;
        }

        var decoration = _decorationTypes.FirstOrDefault(item =>
            string.Equals(
                item.Code,
                decorationCode,
                StringComparison.OrdinalIgnoreCase));

        if (decoration is null)
        {
            return decorationCode;
        }

        return $"{decoration.Code} - {decoration.Name}";
    }

    private void Search()
    {
        var articles = _articleRepository.Search(
                TxtSapNumber.Text,
                TxtOldNumber.Text,
                TxtDescription.Text,
                TxtDecoration.Text)
            .Select(article => new ArticleSearchRow
            {
                SapArticleNumber = article.SapArticleNumber,
                Description = article.Description,
                OldMaterialNumber = article.OldMaterialNumber,
                MaterialStatusName = BuildStatusText(article.MaterialStatusCode),
                DecorationsText = BuildDecorationText(article.DecorationCode)
            })
            .ToList();

        DgvArticles.ItemsSource = articles;
    }

    private void SelectCurrentArticle()
    {
        if (DgvArticles.SelectedItem is not ArticleSearchRow row)
        {
            DmsMessage.Show(
                "Nejdřív vyber artikl.",
                "DMS - výběr artiklu",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        SelectedArticleNumber = row.SapArticleNumber;
        DialogResult = true;
        Close();
    }

    private sealed class ArticleSearchRow
    {
        public string SapArticleNumber { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string OldMaterialNumber { get; init; } = string.Empty;
        public string MaterialStatusName { get; init; } = string.Empty;
        public string DecorationsText { get; init; } = string.Empty;
    }
}