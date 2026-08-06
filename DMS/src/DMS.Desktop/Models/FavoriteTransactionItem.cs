namespace DMS.Desktop.Models;

public sealed class FavoriteTransactionItem
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    public string DisplayText => $"★ {Code}  {Name}";
}
