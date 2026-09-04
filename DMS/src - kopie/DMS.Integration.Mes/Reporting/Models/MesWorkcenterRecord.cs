namespace DMS.Integration.Mes.Reporting.Models;

public sealed class MesWorkcenterRecord
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string ErpCode { get; init; } = string.Empty;

    public string PlantName { get; init; } = string.Empty;

    public string DisplayText =>
        string.IsNullOrWhiteSpace(Description)
            ? Code
            : $"{Code} — {Description}";

    public override string ToString()
    {
        if (string.IsNullOrWhiteSpace(Code))
        {
            return Description;
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            return Code;
        }

        return $"{Code} - {Description}";
    }
}
