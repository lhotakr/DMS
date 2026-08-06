namespace DMS.Core.Quality;

public sealed class QualityOrderSaveResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public QualityOrder? SavedOrder { get; init; }

    public static QualityOrderSaveResult Ok(
        QualityOrder order,
        string message)
    {
        return new QualityOrderSaveResult
        {
            Success = true,
            SavedOrder = order,
            Message = message
        };
    }

    public static QualityOrderSaveResult Fail(string message)
    {
        return new QualityOrderSaveResult
        {
            Success = false,
            Message = message
        };
    }
}
