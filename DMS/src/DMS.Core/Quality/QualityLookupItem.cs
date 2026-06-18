namespace DMS.Core.Quality;

public sealed class QualityLookupItem
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;

    public int SortOrder { get; init; }

    public string Notes { get; init; } = string.Empty;

    public override string ToString()
    {
        return Name;
    }
}