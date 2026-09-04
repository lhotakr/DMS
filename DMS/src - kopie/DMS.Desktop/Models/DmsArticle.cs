namespace DMS.Desktop.Models;

public sealed class DmsArticle
{
    public string SapArticleNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string OldMaterialNumber { get; set; } = string.Empty;
    public string MaterialStatusCode { get; set; } = string.Empty;
    public string DecorationCode { get; set; } = string.Empty;
    public List<ArticleOperation> Operations { get; set; } = new();
    public List<ArticleTechnologyLink> TechnologyLinks { get; set; } = new();
    public List<ArticleScreenData> Screens { get; set; } = new();
    public List<ArticleDocumentLink> Documents { get; set; } = new();
    public ArticleFlowLink? UpstreamArticle { get; set; }
    public List<ArticleFlowLink> DownstreamArticles { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}