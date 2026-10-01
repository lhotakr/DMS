using System.Globalization;

namespace DMS.Integration.Mes.Reporting.Models;

public sealed class Mes06ScrapDetailRecord
{
    public Guid MesId { get; init; }
    public DateTime PointInTime { get; init; }

    public string PointInTimeText =>
        PointInTime.ToString(
            "ddd dd.MM.yyyy",
            CultureInfo.InvariantCulture);

    public string WorkcenterCode { get; init; } = string.Empty;
    public string Item { get; init; } = string.Empty;
    public string ItemDesignation { get; init; } = string.Empty;
    public string OrderCode { get; init; } = string.Empty;
    public string ItemGroup { get; init; } = string.Empty;
    public string Personnel { get; init; } = string.Empty;
    public string CounterName { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public string Unit { get; init; } = "ks";
    public decimal? Percental { get; init; }
    public string UserText { get; init; } = string.Empty;
}

public sealed class Mes06CounterCatalogItem
{
    public string GroupName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public string DisplayText =>
        string.IsNullOrWhiteSpace(Description)
        || string.Equals(
            Name,
            Description,
            StringComparison.CurrentCultureIgnoreCase)
            ? Name
            : $"{Name} — {Description}";
}
