namespace DMS.Desktop.Models;

public sealed class ArticleRelation
{
    public string RelationType { get; set; } = string.Empty;
    // INPUT_ARTICLE, OUTPUT_ARTICLE, PREVIOUS_STEP, NEXT_STEP, VARIANT, REPLACEMENT

    public string RelatedArticleNumber { get; set; } = string.Empty;
    public string RelatedArticleDescription { get; set; } = string.Empty;

    public string ProcessStep { get; set; } = string.Empty;

    public int Sequence { get; set; }

    public string Note { get; set; } = string.Empty;
}