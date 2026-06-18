namespace DMS.Core.Sap;

public sealed class SapMaterialUsageOverviewService
{
    private readonly IReadOnlyList<SapMaterial> _materials;
    private readonly IReadOnlyList<SapBom> _boms;
    private readonly Dictionary<string, SapMaterial> _materialsByNumber;

    public SapMaterialUsageOverviewService(
        IReadOnlyList<SapMaterial> materials,
        IReadOnlyList<SapBom> boms)
    {
        _materials = materials;
        _boms = boms;

        _materialsByNumber = materials
            .GroupBy(item => NormalizeMaterialNumber(item.MaterialNumber), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
    }

    public SapMaterialUsageOverview BuildOverview(string materialNumber)
    {
        var normalizedMaterialNumber = NormalizeMaterialNumber(materialNumber);

        _materialsByNumber.TryGetValue(normalizedMaterialNumber, out var material);

        var overview = new SapMaterialUsageOverview
        {
            MaterialNumber = normalizedMaterialNumber,
            Description = material?.Description ?? string.Empty,
            OldMaterialNumber = material?.OldMaterialNumber ?? string.Empty,
            MaterialKind = material?.MaterialKind ?? string.Empty,
            MaterialStatus = material?.MaterialStatus ?? string.Empty,
            Material = material
        };

        if (material is null)
        {
            overview.Messages.Add(
                $"Materiál {normalizedMaterialNumber} nebyl nalezen v SAP materiálové cache.");
        }

        overview.UsedAsComponent.AddRange(
            FindUsedAsComponent(normalizedMaterialNumber));

        overview.OwnBomVariants.AddRange(
            FindOwnBomVariants(normalizedMaterialNumber));

        if (overview.UsedAsComponent.Count == 0)
        {
            overview.Messages.Add("Materiál nebyl nalezen jako komponenta v žádném importovaném kusovníku.");
        }

        if (overview.OwnBomVariants.Count == 0)
        {
            overview.Messages.Add("Pro materiál nebyl nalezen vlastní kusovník.");
        }

        return overview;
    }

    private List<SapMaterialUsedAsComponentRow> FindUsedAsComponent(string materialNumber)
    {
        return _boms
            .SelectMany(bom => bom.Items
                .Where(item => string.Equals(
                    NormalizeMaterialNumber(item.ComponentNumber),
                    materialNumber,
                    StringComparison.OrdinalIgnoreCase))
                .Select(item =>
                {
                    var parentNumber = NormalizeMaterialNumber(bom.MaterialNumber);
                    var parentMaterial = FindMaterial(parentNumber);

                    return new SapMaterialUsedAsComponentRow
                    {
                        ParentMaterialNumber = parentNumber,
                        ParentDescription = parentMaterial?.Description ?? string.Empty,
                        ParentMaterialKind = parentMaterial?.MaterialKind ?? string.Empty,

                        Plant = bom.Plant,
                        BomNumber = bom.BomNumber,
                        Alternative = NormalizeAlternativeDisplay(bom.Alternative),
                        Position = item.Position,
                        ItemCategory = item.ItemCategory,
                        Quantity = item.Quantity,
                        Unit = item.Unit
                    };
                }))
            .OrderBy(item => item.ParentMaterialNumber)
            .ThenBy(item => item.Plant)
            .ThenBy(item => item.Alternative)
            .ThenBy(item => item.Position)
            .ToList();
    }

    private List<SapMaterialOwnBomVariant> FindOwnBomVariants(string materialNumber)
    {
        return _boms
            .Where(bom => string.Equals(
                NormalizeMaterialNumber(bom.MaterialNumber),
                materialNumber,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(bom => bom.Plant)
            .ThenBy(bom => NormalizeAlternativeForSort(bom.Alternative))
            .Select(bom => new SapMaterialOwnBomVariant
            {
                Plant = bom.Plant,
                BomNumber = bom.BomNumber,
                Alternative = NormalizeAlternativeDisplay(bom.Alternative),
                BomUsage = bom.BomUsage,
                BaseQuantity = bom.BaseQuantity,
                BaseUnit = bom.BaseUnit,
                Items = bom.Items
                    .OrderBy(item => item.Position)
                    .Select(item =>
                    {
                        var componentNumber = NormalizeMaterialNumber(item.ComponentNumber);
                        var componentMaterial = FindMaterial(componentNumber);

                        return new SapMaterialOwnBomItemRow
                        {
                            Position = item.Position,
                            ItemCategory = item.ItemCategory,
                            ComponentNumber = componentNumber,
                            ComponentDescription = componentMaterial?.Description ?? item.ItemText,
                            ComponentKind = componentMaterial?.MaterialKind ?? item.ComponentKind,
                            Quantity = item.Quantity,
                            Unit = item.Unit,
                            IsFixedQuantity = item.IsFixedQuantity,
                            ScrapPercent = item.ScrapPercent
                        };
                    })
                    .ToList()
            })
            .ToList();
    }

    private SapMaterial? FindMaterial(string materialNumber)
    {
        var normalized = NormalizeMaterialNumber(materialNumber);

        return _materialsByNumber.TryGetValue(normalized, out var material)
            ? material
            : null;
    }

    public static string NormalizeMaterialNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        return text.All(char.IsDigit)
            ? text.PadLeft(10, '0')
            : text;
    }

    private static string NormalizeAlternativeDisplay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        return int.TryParse(text, out var number)
            ? number.ToString("00")
            : text;
    }

    private static string NormalizeAlternativeForSort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "9999";
        }

        var text = value.Trim();

        return int.TryParse(text, out var number)
            ? number.ToString("0000")
            : text;
    }
}