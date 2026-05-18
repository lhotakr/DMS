using System.Windows.Controls;
using DMS.Desktop.Models;

namespace DMS.Desktop.Views.Articles;

public partial class ArticleDetailView : UserControl
{
    public ArticleDetailView(DmsArticle article)
    {
        InitializeComponent();

        TxtTitle.Text = $"Artikl {article.SapArticleNumber}";

        TxtBasic.Text =
            $"SAP číslo: {article.SapArticleNumber}\n" +
            $"Označení: {article.Description}\n" +
            $"Staré číslo: {article.OldMaterialNumber}\n" +
            $"Status materiálu: {article.MaterialStatusCode}\n" +
            $"Dekorace: {article.DecorationCode}";

        DgvDocuments.ItemsSource = article.Documents;
        DgvOperations.ItemsSource = article.Operations;
        DgvTechnologyLinks.ItemsSource = article.TechnologyLinks;
        DgvScreens.ItemsSource = article.Screens;

        DgvUpstreamArticle.ItemsSource = article.UpstreamArticle is null
            ? Array.Empty<ArticleFlowLink>()
            : new[] { article.UpstreamArticle };

        DgvDownstreamArticles.ItemsSource = article.DownstreamArticles;
    }
}