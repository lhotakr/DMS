namespace DMS.Core.Quality;

public sealed class QualityCustomer
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;

    public bool IsLoreal { get; init; }

    public int SourceId { get; init; }

    public override string ToString()
    {
        return Name;
    }
}