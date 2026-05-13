namespace DMS.Desktop.Models;

public sealed class TransactionMenuItem
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Module { get; init; } = string.Empty;
    public bool RequiresArticleNumber { get; init; }
    public bool IsFavorite { get; init; }

    public string FavoriteIcon => IsFavorite ? "★" : "☆";

    public string FavoriteToolTip => IsFavorite
        ? "Odebrat z oblíbených"
        : "Přidat do oblíbených";

    public string DisplayText
    {
        get
        {
            var parameterHint = RequiresArticleNumber ? "  [artikl]" : "";
            return $"{Code}  {Name}{parameterHint}";
        }
    }
}