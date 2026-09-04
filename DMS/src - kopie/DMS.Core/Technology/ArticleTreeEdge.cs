public sealed class ArticleTreeEdge
{
    public string FromArticleCode { get; init; } = "";
    public string ToArticleCode { get; init; } = "";

    public decimal? Quantity { get; init; }
    public string? Unit { get; init; }
}