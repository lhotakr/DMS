namespace DMS.Core.Quality;

public sealed class QualityArticle
{
    public QualityRecordMetadata Metadata { get; init; } = new();
    public string LegacyArticleNumber { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Prefix { get; init; } = string.Empty;

    public string ArticleNumberPart { get; init; } = string.Empty;

    public string ImportantInfo { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public DateTime ImportedAt { get; init; } = DateTime.Now;

    public string SourceFilePath { get; init; } = string.Empty;
}