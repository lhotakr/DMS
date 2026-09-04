using System.Text.Json;
using DMS.Core.Domain.Organization;
using DMS.Core.Domain.People;
using DMS.Core.Domain.Units;

namespace DMS.Core.Framework.MasterData;

public sealed class DmsMasterDataInspector
{
    public IReadOnlyList<DmsMasterDataHealthResult> Inspect(
        IReadOnlyList<DmsOrganizationUnit> organizationUnits,
        IReadOnlyList<DmsPerson> people,
        IReadOnlyList<UnitDimension> dimensions,
        IReadOnlyList<UnitDefinition> units,
        IReadOnlyList<DmsUserPersonLink> userLinks)
    {
        var results = new List<DmsMasterDataHealthResult>();

        CheckOrganizationUnits(results, organizationUnits);
        CheckPeople(results, people, organizationUnits);
        CheckUnits(results, dimensions, units);
        CheckUserLinks(results, userLinks, people);

        return results;
    }

    private static void CheckOrganizationUnits(
        ICollection<DmsMasterDataHealthResult> results,
        IReadOnlyList<DmsOrganizationUnit> items)
    {
        var duplicateCodes = items
            .Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.Code))
            .GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "Organization",
            "Duplicate active organization codes",
            duplicateCodes);

        var ids = items
            .Select(x => x.OrganizationUnitId)
            .ToHashSet();

        var missingParents = items
            .Where(x =>
                x.ParentOrganizationUnitId.HasValue &&
                !ids.Contains(x.ParentOrganizationUnitId.Value))
            .Select(x => x.Code)
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "Organization",
            "Missing parent organization unit",
            missingParents);

        var cycles = new List<string>();

        foreach (var item in items)
        {
            var visited = new HashSet<Guid>
            {
                item.OrganizationUnitId
            };

            var parent = item.ParentOrganizationUnitId;

            while (parent.HasValue)
            {
                if (!visited.Add(parent.Value))
                {
                    cycles.Add(item.Code);
                    break;
                }

                var parentItem = items.FirstOrDefault(x =>
                    x.OrganizationUnitId == parent.Value);

                if (parentItem is null)
                {
                    break;
                }

                parent = parentItem.ParentOrganizationUnitId;
            }
        }

        AddListCheck(
            results,
            "Organization",
            "Organization hierarchy cycles",
            cycles.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static void CheckPeople(
        ICollection<DmsMasterDataHealthResult> results,
        IReadOnlyList<DmsPerson> people,
        IReadOnlyList<DmsOrganizationUnit> organizationUnits)
    {
        var duplicateNumbers = people
            .Where(x =>
                x.IsActive &&
                !string.IsNullOrWhiteSpace(x.PersonnelNumber))
            .GroupBy(
                x => x.PersonnelNumber.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "People",
            "Duplicate active personnel numbers",
            duplicateNumbers);

        var unitMap = organizationUnits
            .ToDictionary(x => x.OrganizationUnitId);

        var missingUnits = people
            .Where(x =>
                !unitMap.ContainsKey(x.OrganizationUnitId))
            .Select(x => $"{x.PersonnelNumber}:{x.DisplayName}")
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "People",
            "People referencing missing organization units",
            missingUnits);

        var inactiveUnits = people
            .Where(x =>
                x.IsActive &&
                unitMap.TryGetValue(
                    x.OrganizationUnitId,
                    out var unit) &&
                !unit.IsActive)
            .Select(x => $"{x.PersonnelNumber}:{x.DisplayName}")
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "People",
            "Active people in inactive organization units",
            inactiveUnits,
            warning: true);

        var incomplete = people
            .Where(x =>
                x.IsActive &&
                (string.IsNullOrWhiteSpace(x.PersonnelNumber) ||
                 string.IsNullOrWhiteSpace(x.FirstName) ||
                 string.IsNullOrWhiteSpace(x.LastName)))
            .Select(x =>
                string.IsNullOrWhiteSpace(x.PersonnelNumber)
                    ? x.PersonId.ToString()
                    : x.PersonnelNumber)
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "People",
            "Incomplete active people",
            incomplete);
    }

    private static void CheckUnits(
        ICollection<DmsMasterDataHealthResult> results,
        IReadOnlyList<UnitDimension> dimensions,
        IReadOnlyList<UnitDefinition> units)
    {
        var dimensionIds = dimensions
            .Select(x => x.UnitDimensionId)
            .ToHashSet();

        var duplicateDimensionCodes = dimensions
            .Where(x => x.IsActive)
            .GroupBy(
                x => x.Code.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Where(x =>
                string.IsNullOrWhiteSpace(x.Key) ||
                x.Count() > 1)
            .Select(x => x.Key)
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "Units",
            "Duplicate unit dimension codes",
            duplicateDimensionCodes);

        var duplicateUnitCodes = units
            .GroupBy(
                x => x.Code.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Where(x =>
                string.IsNullOrWhiteSpace(x.Key) ||
                x.Count() > 1)
            .Select(x => x.Key)
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "Units",
            "Duplicate unit codes",
            duplicateUnitCodes);

        var orphanUnits = units
            .Where(x =>
                !dimensionIds.Contains(x.UnitDimensionId))
            .Select(x => x.Code)
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "Units",
            "Units referencing missing dimensions",
            orphanUnits);

        var zeroScale = units
            .Where(x => x.ScaleToBase == 0m)
            .Select(x => x.Code)
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "Units",
            "Units with zero conversion scale",
            zeroScale);

        var multipleDefaults = units
            .Where(x => x.IsDefault)
            .GroupBy(x => x.UnitDimensionId)
            .Where(x => x.Count() > 1)
            .Select(group =>
            {
                var dimension = dimensions.FirstOrDefault(x =>
                    x.UnitDimensionId == group.Key);

                return dimension?.Code ??
                       group.Key.ToString();
            })
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "Units",
            "Dimensions with multiple default units",
            multipleDefaults);

        var missingDefaults = dimensions
            .Where(x => x.IsActive)
            .Where(dimension =>
                !units.Any(unit =>
                    unit.UnitDimensionId == dimension.UnitDimensionId &&
                    unit.IsActive &&
                    unit.IsDefault))
            .Select(x => x.Code)
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "Units",
            "Active dimensions without a default unit",
            missingDefaults,
            warning: true);

        var missingBaseUnits = dimensions
            .Where(x => x.IsActive)
            .Where(dimension =>
                string.IsNullOrWhiteSpace(dimension.BaseUnitCode) ||
                !units.Any(unit =>
                    unit.UnitDimensionId == dimension.UnitDimensionId &&
                    unit.IsActive &&
                    string.Equals(
                        unit.Code,
                        dimension.BaseUnitCode,
                        StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.Code)
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "Units",
            "Dimensions with missing base unit",
            missingBaseUnits);

        var invalidBaseUnits = dimensions
            .Where(x =>
                x.IsActive &&
                !string.IsNullOrWhiteSpace(x.BaseUnitCode))
            .Select(dimension => new
            {
                Dimension = dimension,
                BaseUnit = units.FirstOrDefault(unit =>
                    unit.UnitDimensionId == dimension.UnitDimensionId &&
                    unit.IsActive &&
                    string.Equals(
                        unit.Code,
                        dimension.BaseUnitCode,
                        StringComparison.OrdinalIgnoreCase))
            })
            .Where(x =>
                x.BaseUnit is not null &&
                (x.BaseUnit.ScaleToBase != 1m ||
                 x.BaseUnit.OffsetToBase != 0m))
            .Select(x =>
                $"{x.Dimension.Code}->{x.BaseUnit!.Code}")
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "Units",
            "Base units with invalid conversion identity",
            invalidBaseUnits);
    }

    private static void CheckUserLinks(
        ICollection<DmsMasterDataHealthResult> results,
        IReadOnlyList<DmsUserPersonLink> users,
        IReadOnlyList<DmsPerson> people)
    {
        var personIds = people
            .Select(x => x.PersonId)
            .ToHashSet();

        var brokenLinks = users
            .Where(x =>
                x.PersonId.HasValue &&
                !personIds.Contains(x.PersonId.Value))
            .Select(x => x.WindowsLogin)
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "Users",
            "Users referencing missing people",
            brokenLinks);

        var duplicatePersonLinks = users
            .Where(x => x.PersonId.HasValue)
            .GroupBy(x => x.PersonId!.Value)
            .Where(x => x.Count() > 1)
            .Select(group =>
            {
                var person = people.FirstOrDefault(x =>
                    x.PersonId == group.Key);

                return person is null
                    ? group.Key.ToString()
                    : $"{person.PersonnelNumber}:{person.DisplayName}";
            })
            .OrderBy(x => x)
            .ToList();

        AddListCheck(
            results,
            "Users",
            "People linked to multiple DMS users",
            duplicatePersonLinks,
            warning: true);
    }

    private static void AddListCheck(
        ICollection<DmsMasterDataHealthResult> results,
        string area,
        string check,
        IReadOnlyCollection<string> problems,
        bool warning = false)
    {
        if (problems.Count == 0)
        {
            results.Add(new DmsMasterDataHealthResult(
                "OK",
                area,
                check,
                "No problem found."));
            return;
        }

        var sample = string.Join(
            ", ",
            problems.Take(25));

        if (problems.Count > 25)
        {
            sample += $", … (+{problems.Count - 25})";
        }

        results.Add(new DmsMasterDataHealthResult(
            warning ? "WARNING" : "ERROR",
            area,
            check,
            $"{problems.Count} problem(s): {sample}"));
    }
}

public sealed record DmsUserPersonLink(
    string WindowsLogin,
    Guid? PersonId,
    bool IsActive);
