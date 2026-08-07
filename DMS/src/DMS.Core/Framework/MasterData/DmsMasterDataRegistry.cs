namespace DMS.Core.Framework.MasterData;

public sealed record DmsMasterDataEntityDescriptor(
    string Code,
    string Name,
    string FileName,
    string KeyField,
    string Description,
    IReadOnlyList<string> Dependencies);

public static class DmsMasterDataRegistry
{
    private static readonly IReadOnlyList<DmsMasterDataEntityDescriptor> Items =
        new[]
        {
            new DmsMasterDataEntityDescriptor(
                "ORGANIZATION_UNITS",
                "Organization units",
                "organization-units.json",
                "OrganizationUnitId / Code",
                "Corporate and local organizational structure.",
                Array.Empty<string>()),

            new DmsMasterDataEntityDescriptor(
                "PEOPLE",
                "People",
                "people.json",
                "PersonId / PersonnelNumber",
                "Persons used by forms, approvals and user linkage.",
                new[] { "ORGANIZATION_UNITS" }),

            new DmsMasterDataEntityDescriptor(
                "UNIT_DIMENSIONS",
                "Unit dimensions",
                "unit-dimensions.json",
                "UnitDimensionId / Code",
                "Physical dimensions used by the unit conversion framework.",
                Array.Empty<string>()),

            new DmsMasterDataEntityDescriptor(
                "UNITS",
                "Units",
                "units.json",
                "UnitDefinitionId / Code",
                "Measurement units and conversion parameters.",
                new[] { "UNIT_DIMENSIONS" }),

            new DmsMasterDataEntityDescriptor(
                "USERS",
                "DMS users",
                "users.json",
                "WindowsLogin",
                "DMS accounts and their optional person linkage.",
                new[] { "PEOPLE" })
        };

    public static IReadOnlyList<DmsMasterDataEntityDescriptor> All => Items;

    public static DmsMasterDataEntityDescriptor? Find(string code) =>
        Items.FirstOrDefault(x =>
            string.Equals(
                x.Code,
                code,
                StringComparison.OrdinalIgnoreCase));
}

public sealed record DmsMasterDataHealthResult(
    string Severity,
    string Area,
    string Check,
    string Details);
