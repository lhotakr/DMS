using DMS.Core.Domain.Organization;
using DMS.Core.Domain.People;
using DMS.Core.Domain.Units;
using System.IO;

namespace DMS.Desktop.Services.MasterData;

public sealed class DmsMasterDataService
{
    private readonly AtomicJsonStore<List<DmsOrganizationUnit>> _organizationStore;
    private readonly AtomicJsonStore<List<DmsPerson>> _peopleStore;
    private readonly AtomicJsonStore<List<UnitDimension>> _dimensionStore;
    private readonly AtomicJsonStore<List<UnitDefinition>> _unitStore;

    public DmsMasterDataService(string masterDataRootPath)
    {
        Directory.CreateDirectory(masterDataRootPath);
        _organizationStore = new(Path.Combine(masterDataRootPath, "organization-units.json"));
        _peopleStore = new(Path.Combine(masterDataRootPath, "people.json"));
        _dimensionStore = new(Path.Combine(masterDataRootPath, "unit-dimensions.json"));
        _unitStore = new(Path.Combine(masterDataRootPath, "units.json"));
        EnsureSeedData();
    }

    public string OrganizationUnitsPath => _organizationStore.FilePath;
    public string PeoplePath => _peopleStore.FilePath;
    public string UnitDimensionsPath => _dimensionStore.FilePath;
    public string UnitsPath => _unitStore.FilePath;

    public List<DmsOrganizationUnit> LoadOrganizationUnits() => _organizationStore.Load();
    public List<DmsPerson> LoadPeople() => _peopleStore.Load();
    public List<UnitDimension> LoadUnitDimensions() => _dimensionStore.Load();
    public List<UnitDefinition> LoadUnits() => _unitStore.Load();

    public void SaveOrganizationUnits(List<DmsOrganizationUnit> items)
    {
        ValidateOrganizationUnits(items);
        _organizationStore.Save(items);
    }

    public void SavePeople(List<DmsPerson> items, IReadOnlyCollection<DmsOrganizationUnit> organizationUnits)
    {
        ValidatePeople(items, organizationUnits);
        _peopleStore.Save(items);
    }

    public void SaveUnits(List<UnitDimension> dimensions, List<UnitDefinition> units)
    {
        ValidateUnits(dimensions, units);
        _dimensionStore.Save(dimensions);
        _unitStore.Save(units);
    }

    private void EnsureSeedData()
    {
        var dimensions = LoadUnitDimensions();
        var units = LoadUnits();
        if (dimensions.Count > 0 || units.Count > 0) return;

        var pressure = new UnitDimension { Code = "PRESSURE", Name = "Tlak", BaseUnitCode = "PA", SortOrder = 10 };
        var mass = new UnitDimension { Code = "MASS", Name = "Hmotnost", BaseUnitCode = "G", SortOrder = 20 };
        var volume = new UnitDimension { Code = "VOLUME", Name = "Objem", BaseUnitCode = "ML", SortOrder = 30 };
        var thickness = new UnitDimension { Code = "THICKNESS", Name = "Tloušťka", BaseUnitCode = "UM", SortOrder = 40 };
        var time = new UnitDimension { Code = "TIME", Name = "Čas", BaseUnitCode = "S", SortOrder = 50 };

        dimensions = new() { pressure, mass, volume, thickness, time };
        units = new()
        {
            new() { UnitDimensionId = pressure.UnitDimensionId, Code = "PA", Symbol = "Pa", Name = "pascal", ScaleToBase = 1m, DecimalPlaces = 0, IsDefault = false },
            new() { UnitDimensionId = pressure.UnitDimensionId, Code = "KPA", Symbol = "kPa", Name = "kilopascal", ScaleToBase = 1000m, DecimalPlaces = 1 },
            new() { UnitDimensionId = pressure.UnitDimensionId, Code = "MPA", Symbol = "MPa", Name = "megapascal", ScaleToBase = 1000000m, DecimalPlaces = 3 },
            new() { UnitDimensionId = pressure.UnitDimensionId, Code = "BAR", Symbol = "bar", Name = "bar", ScaleToBase = 100000m, DecimalPlaces = 2, IsDefault = true },
            new() { UnitDimensionId = pressure.UnitDimensionId, Code = "MBAR", Symbol = "mbar", Name = "milibar", ScaleToBase = 100m, DecimalPlaces = 1 },
            new() { UnitDimensionId = pressure.UnitDimensionId, Code = "PSI", Symbol = "psi", Name = "pound per square inch", ScaleToBase = 6894.757293168m, DecimalPlaces = 2 },
            new() { UnitDimensionId = mass.UnitDimensionId, Code = "MG", Symbol = "mg", Name = "miligram", ScaleToBase = 0.001m, DecimalPlaces = 2 },
            new() { UnitDimensionId = mass.UnitDimensionId, Code = "G", Symbol = "g", Name = "gram", ScaleToBase = 1m, DecimalPlaces = 2, IsDefault = true },
            new() { UnitDimensionId = mass.UnitDimensionId, Code = "KG", Symbol = "kg", Name = "kilogram", ScaleToBase = 1000m, DecimalPlaces = 3 },
            new() { UnitDimensionId = volume.UnitDimensionId, Code = "ML", Symbol = "ml", Name = "mililitr", ScaleToBase = 1m, DecimalPlaces = 0, IsDefault = true },
            new() { UnitDimensionId = volume.UnitDimensionId, Code = "L", Symbol = "l", Name = "litr", ScaleToBase = 1000m, DecimalPlaces = 3 },
            new() { UnitDimensionId = thickness.UnitDimensionId, Code = "UM", Symbol = "µm", Name = "mikrometr", ScaleToBase = 1m, DecimalPlaces = 1, IsDefault = true },
            new() { UnitDimensionId = thickness.UnitDimensionId, Code = "MM", Symbol = "mm", Name = "milimetr", ScaleToBase = 1000m, DecimalPlaces = 4 },
            new() { UnitDimensionId = time.UnitDimensionId, Code = "MS", Symbol = "ms", Name = "milisekunda", ScaleToBase = 0.001m, DecimalPlaces = 0 },
            new() { UnitDimensionId = time.UnitDimensionId, Code = "S", Symbol = "s", Name = "sekunda", ScaleToBase = 1m, DecimalPlaces = 2, IsDefault = true },
            new() { UnitDimensionId = time.UnitDimensionId, Code = "MIN", Symbol = "min", Name = "minuta", ScaleToBase = 60m, DecimalPlaces = 2 },
            new() { UnitDimensionId = time.UnitDimensionId, Code = "H", Symbol = "h", Name = "hodina", ScaleToBase = 3600m, DecimalPlaces = 3 }
        };
        SaveUnits(dimensions, units);
    }

    private static void ValidateOrganizationUnits(IReadOnlyCollection<DmsOrganizationUnit> items)
    {
        if (items.GroupBy(x => x.OrganizationUnitId).Any(g => g.Count() > 1)) throw new InvalidOperationException("Duplicate organization unit ID.");
        if (items.Where(x => x.IsActive).GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1)) throw new InvalidOperationException("Active organization unit codes must be filled and unique.");
        var ids = items.Select(x => x.OrganizationUnitId).ToHashSet();
        if (items.Any(x => x.ParentOrganizationUnitId.HasValue && !ids.Contains(x.ParentOrganizationUnitId.Value))) throw new InvalidOperationException("Organization unit references a missing parent.");
        foreach (var item in items)
        {
            var visited = new HashSet<Guid> { item.OrganizationUnitId };
            var parent = item.ParentOrganizationUnitId;
            while (parent.HasValue)
            {
                if (!visited.Add(parent.Value)) throw new InvalidOperationException("Organization tree contains a cycle.");
                parent = items.First(x => x.OrganizationUnitId == parent.Value).ParentOrganizationUnitId;
            }
        }
    }

    private static void ValidatePeople(IReadOnlyCollection<DmsPerson> people, IReadOnlyCollection<DmsOrganizationUnit> units)
    {
        if (people.Where(x => x.IsActive).GroupBy(x => x.PersonnelNumber.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1)) throw new InvalidOperationException("Personnel number must be filled and globally unique.");
        var unitIds = units.Select(x => x.OrganizationUnitId).ToHashSet();
        if (people.Any(x => !unitIds.Contains(x.OrganizationUnitId))) throw new InvalidOperationException("Person references a missing organization unit.");
    }

    private static void ValidateUnits(IReadOnlyCollection<UnitDimension> dimensions, IReadOnlyCollection<UnitDefinition> units)
    {
        if (dimensions.Where(x => x.IsActive).GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1)) throw new InvalidOperationException("Unit dimension codes must be unique.");
        var dimensionIds = dimensions.Select(x => x.UnitDimensionId).ToHashSet();
        if (units.Any(x => !dimensionIds.Contains(x.UnitDimensionId))) throw new InvalidOperationException("Unit references a missing dimension.");
        if (units.Any(x => x.ScaleToBase == 0m)) throw new InvalidOperationException("Unit scale cannot be zero.");
        if (units.GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1)) throw new InvalidOperationException("Unit codes must be globally unique.");
        if (units.Where(x => x.IsDefault).GroupBy(x => x.UnitDimensionId).Any(g => g.Count() > 1)) throw new InvalidOperationException("A dimension can have only one default unit.");
    }
}
