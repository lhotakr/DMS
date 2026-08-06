namespace DMS.Desktop.Models;

public sealed class TransactionMenuItem
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Internal module name from transactions.json.
    /// </summary>
    public string Module { get; init; } = string.Empty;

    public string DisplayModule { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool RequiresArticleNumber { get; init; }
    public bool IsFavorite { get; init; }

    public string DisplayText => $"{Code}  {Name}";

    public string FavoriteIcon => IsFavorite ? "★" : "☆";

    public string FavoriteToolTip => IsFavorite
        ? "Remove from favorites"
        : "Add to favorites";
}
