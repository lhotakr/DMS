namespace DMS.Core.Quality;

public sealed class QualityArticleCreateResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public string CreatedSapMaterialNumber { get; init; } = string.Empty;

    public string CreatedPrintVersionNumber { get; init; } = string.Empty;

    public static QualityArticleCreateResult Ok(
        string message,
        string sapMaterialNumber,
        string printVersionNumber)
    {
        return new QualityArticleCreateResult
        {
            Success = true,
            Message = message,
            CreatedSapMaterialNumber = sapMaterialNumber,
            CreatedPrintVersionNumber = printVersionNumber
        };
    }

    public static QualityArticleCreateResult Fail(
        string message)
    {
        return new QualityArticleCreateResult
        {
            Success = false,
            Message = message
        };
    }
}