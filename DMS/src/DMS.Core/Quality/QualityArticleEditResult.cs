namespace DMS.Core.Quality;

public sealed class QualityArticleEditResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;

    public static QualityArticleEditResult Ok(string message)
    {
        return new QualityArticleEditResult
        {
            Success = true,
            Message = message
        };
    }

    public static QualityArticleEditResult Fail(string message)
    {
        return new QualityArticleEditResult
        {
            Success = false,
            Message = message
        };
    }
}