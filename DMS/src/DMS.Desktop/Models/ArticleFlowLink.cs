namespace DMS.Desktop.Models;

public sealed class ArticleFlowLink
{
    public string RelatedArticleNumber { get; set; } = string.Empty;

    public string RelatedArticleDescription { get; set; } = string.Empty;

    public string RelationKind { get; set; } = string.Empty;
    // RawGlass, SprayedInput, PrintedVariant, ProtectiveCoat, NextStep, PreviousStep

    public string OperationTypeCode { get; set; } = string.Empty;

    public int Sequence { get; set; }

    public string Note { get; set; } = string.Empty;
}