namespace DMS.Desktop.Models;

public sealed class ArticleTechnologyLink
{
    public string OperationNumber { get; set; } = string.Empty;
    // Například 0010

    public string LinkType { get; set; } = string.Empty;
    // RECIPE, TOOL, FOIL, GLUE, ASSEMBLY_PART, PURCHASED_PART

    public string SubTypeCode { get; set; } = string.Empty;
    // RECIPE: UV, UV_LED, ORGANIC, CERAMIC
    // TOOL: K14, K15, LEP

    public string ArticleNumber { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int? Sequence { get; set; }

    public string Note { get; set; } = string.Empty;
}