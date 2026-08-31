namespace DMS.Core.Sap;

/// <summary>
/// Read-only analysis of SAP recipe BOMs. Recipes are limited to DMS Recipe materials
/// (with a conservative 17* fallback) and plant 2000 BOM variants. Relations are not
/// inferred from text; component identity is always the SAP material number.
/// </summary>
public sealed class SapRecipeSimilarityService
{
    public const string RecipePlant = "2000";
    private const int MaxReturnedPairs = 5000;

    private readonly IReadOnlyDictionary<string, SapMaterial> _materials;
    private readonly IReadOnlyList<RecipeProfile> _profiles;
    private readonly List<string> _warnings = new();

    public SapRecipeSimilarityService(SapStoragePaths storagePaths)
    {
        ArgumentNullException.ThrowIfNull(storagePaths);

        var materials = new JsonSapMaterialRepository(storagePaths.SapMaterialsFilePath).LoadAll();
        var boms = new JsonSapBomRepository(storagePaths.SapBomSnapshotsFilePath).LoadAll();

        _materials = materials
            .Where(x => !string.IsNullOrWhiteSpace(x.MaterialNumber))
            .GroupBy(x => x.MaterialNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var usageService = new SapMaterialUsageOverviewService(materials, boms);
        _profiles = BuildProfiles(materials, usageService);
    }

    public SapRecipeSimilarityAnalysis Analyze(
        double minimumComponentSimilarityPercent = 80d,
        decimal ratioTolerancePercent = 0.5m)
    {
        minimumComponentSimilarityPercent = Math.Clamp(minimumComponentSimilarityPercent, 0d, 100d);
        ratioTolerancePercent = Math.Max(0m, ratioTolerancePercent);

        var bestByRecipePair = new Dictionary<string, SapRecipeSimilarityPair>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < _profiles.Count; i++)
        {
            for (var j = i + 1; j < _profiles.Count; j++)
            {
                var a = _profiles[i];
                var b = _profiles[j];

                if (string.Equals(a.MaterialNumber, b.MaterialNumber, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = Compare(a, b, minimumComponentSimilarityPercent, ratioTolerancePercent);
                if (candidate is null)
                {
                    continue;
                }

                var pairKey = RecipePairKey(a.MaterialNumber, b.MaterialNumber);
                if (!bestByRecipePair.TryGetValue(pairKey, out var existing) || IsBetter(candidate, existing))
                {
                    bestByRecipePair[pairKey] = candidate;
                }
            }
        }

        var ordered = bestByRecipePair.Values
            .OrderBy(x => KindRank(x.Kind))
            .ThenByDescending(x => x.ComponentSimilarityPercent)
            .ThenBy(x => x.MaxRatioDifferencePercentagePoints ?? decimal.MaxValue)
            .ThenBy(x => x.RecipeANumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.RecipeBNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ordered.Count > MaxReturnedPairs)
        {
            _warnings.Add($"Result limit reached: {ordered.Count} matching recipe pairs, returning first {MaxReturnedPairs}.");
            ordered = ordered.Take(MaxReturnedPairs).ToList();
        }

        var recipeCount = _profiles
            .Select(x => x.MaterialNumber)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new SapRecipeSimilarityAnalysis
        {
            RecipeCount = recipeCount,
            RecipeVariantCount = _profiles.Count,
            IdenticalPairCount = ordered.Count(x => x.Kind == SapRecipeSimilarityKind.Identical),
            SameComponentsPairCount = ordered.Count(x => x.Kind == SapRecipeSimilarityKind.SameComponentsDifferentRatio),
            SimilarPairCount = ordered.Count(x => x.Kind == SapRecipeSimilarityKind.SimilarComponents),
            Pairs = ordered,
            Warnings = _warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private IReadOnlyList<RecipeProfile> BuildProfiles(
        IReadOnlyList<SapMaterial> materials,
        SapMaterialUsageOverviewService usageService)
    {
        var profiles = new List<RecipeProfile>();

        var recipes = materials
            .Where(IsRecipeMaterial)
            .Where(x => !string.IsNullOrWhiteSpace(x.MaterialNumber))
            .GroupBy(x => x.MaterialNumber, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.MaterialNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var recipe in recipes)
        {
            try
            {
                var overview = usageService.BuildOverview(recipe.MaterialNumber);
                var variants = overview.OwnBomVariants
                    .Where(x => string.Equals(x.Plant, RecipePlant, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (variants.Count == 0)
                {
                    continue;
                }

                foreach (var variant in variants)
                {
                    var components = BuildComponents(variant.Items);
                    if (components.Count == 0)
                    {
                        continue;
                    }

                    profiles.Add(new RecipeProfile
                    {
                        MaterialNumber = recipe.MaterialNumber,
                        Description = recipe.Description ?? string.Empty,
                        Alternative = Convert.ToString(variant.Alternative) ?? string.Empty,
                        Components = components
                    });
                }
            }
            catch (Exception ex)
            {
                _warnings.Add($"Recipe {recipe.MaterialNumber} skipped: {ex.Message}");
            }
        }

        return profiles;
    }

    private IReadOnlyDictionary<string, RecipeComponent> BuildComponents(IEnumerable<SapMaterialOwnBomItemRow> items)
    {
        var raw = items
            .Where(x => !string.IsNullOrWhiteSpace(x.ComponentNumber))
            .GroupBy(x => x.ComponentNumber, StringComparer.OrdinalIgnoreCase)
            .Select(BuildComponent)
            .Where(x => x is not null)
            .Cast<RecipeComponent>()
            .ToList();

        foreach (var dimensionGroup in raw
                     .Where(x => !x.IsFixed && x.IsQuantityComparable && x.BaseQuantity > 0m)
                     .GroupBy(x => x.Dimension, StringComparer.OrdinalIgnoreCase))
        {
            var total = dimensionGroup.Sum(x => x.BaseQuantity);
            if (total <= 0m)
            {
                continue;
            }

            foreach (var component in dimensionGroup)
            {
                component.SharePercent = component.BaseQuantity / total * 100m;
            }
        }

        return raw.ToDictionary(x => x.ComponentNumber, StringComparer.OrdinalIgnoreCase);
    }

    private RecipeComponent? BuildComponent(IGrouping<string, SapMaterialOwnBomItemRow> rows)
    {
        var list = rows.ToList();
        var first = list[0];
        var fixedValues = list.Select(x => x.IsFixedQuantity).Distinct().ToList();
        var isFixed = fixedValues.Count == 1 && fixedValues[0];
        var fixedMixed = fixedValues.Count > 1;

        string? dimension = null;
        decimal baseQuantity = 0m;
        var comparable = !fixedMixed;
        var displayUnit = string.Empty;
        decimal displayQuantity = 0m;
        var hasDisplayQuantity = false;

        foreach (var row in list)
        {
            if (!row.Quantity.HasValue)
            {
                comparable = false;
                continue;
            }

            var qty = row.Quantity.Value;
            var unit = row.Unit ?? string.Empty;
            if (!TryToBaseQuantity(qty, unit, out var rowDimension, out var rowBase, out var canonicalUnit))
            {
                comparable = false;
                continue;
            }

            if (dimension is null)
            {
                dimension = rowDimension;
                displayUnit = canonicalUnit;
            }
            else if (!string.Equals(dimension, rowDimension, StringComparison.OrdinalIgnoreCase))
            {
                comparable = false;
                continue;
            }

            baseQuantity += rowBase;
            displayQuantity += rowBase;
            hasDisplayQuantity = true;
        }

        _materials.TryGetValue(rows.Key, out var material);
        var description = material?.Description
                          ?? first.ComponentDescription
                          ?? string.Empty;

        return new RecipeComponent
        {
            ComponentNumber = rows.Key,
            Description = description,
            IsFixed = isFixed,
            IsQuantityComparable = comparable && dimension is not null && hasDisplayQuantity,
            Dimension = dimension ?? string.Empty,
            BaseQuantity = baseQuantity,
            DisplayQuantity = hasDisplayQuantity ? displayQuantity : null,
            DisplayUnit = displayUnit
        };
    }

    private SapRecipeSimilarityPair? Compare(
        RecipeProfile a,
        RecipeProfile b,
        double minimumComponentSimilarityPercent,
        decimal ratioTolerancePercent)
    {
        var aKeys = a.Components.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bKeys = b.Components.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var common = aKeys.Intersect(bKeys, StringComparer.OrdinalIgnoreCase).ToList();
        var union = aKeys.Union(bKeys, StringComparer.OrdinalIgnoreCase).ToList();

        if (union.Count == 0)
        {
            return null;
        }

        var componentSimilarity = common.Count * 100d / union.Count;
        if (componentSimilarity + 0.0001d < minimumComponentSimilarityPercent)
        {
            return null;
        }

        var sameComponents = aKeys.SetEquals(bKeys);
        var comparisons = BuildComponentComparisons(a, b);

        var ratioComparable = sameComponents && comparisons
            .Where(x => x.DifferenceCode == "SAME")
            .All(x => x.DifferencePercentagePoints.HasValue);

        var maxDifference = comparisons
            .Where(x => x.DifferencePercentagePoints.HasValue)
            .Select(x => x.DifferencePercentagePoints!.Value)
            .DefaultIfEmpty()
            .Max();

        var fixedCompatible = sameComponents && a.Components.Keys.All(key =>
        {
            var ca = a.Components[key];
            var cb = b.Components[key];

            if (ca.IsFixed != cb.IsFixed)
            {
                return false;
            }

            if (!ca.IsFixed)
            {
                return true;
            }

            if (!ca.IsQuantityComparable || !cb.IsQuantityComparable ||
                !string.Equals(ca.Dimension, cb.Dimension, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var max = Math.Max(Math.Abs(ca.BaseQuantity), Math.Abs(cb.BaseQuantity));
            if (max <= 0m)
            {
                return true;
            }

            var relativeDifference = Math.Abs(ca.BaseQuantity - cb.BaseQuantity) / max * 100m;
            return relativeDifference <= ratioTolerancePercent;
        });

        var kind = sameComponents && ratioComparable && fixedCompatible && maxDifference <= ratioTolerancePercent
            ? SapRecipeSimilarityKind.Identical
            : sameComponents
                ? SapRecipeSimilarityKind.SameComponentsDifferentRatio
                : SapRecipeSimilarityKind.SimilarComponents;

        return new SapRecipeSimilarityPair
        {
            RecipeANumber = a.MaterialNumber,
            RecipeADescription = a.Description,
            RecipeAAlternative = a.Alternative,
            RecipeBNumber = b.MaterialNumber,
            RecipeBDescription = b.Description,
            RecipeBAlternative = b.Alternative,
            Kind = kind,
            ComponentSimilarityPercent = componentSimilarity,
            CommonComponentCount = common.Count,
            UnionComponentCount = union.Count,
            MaxRatioDifferencePercentagePoints = sameComponents && ratioComparable ? maxDifference : null,
            Components = comparisons
        };
    }

    private IReadOnlyList<SapRecipeComponentComparison> BuildComponentComparisons(RecipeProfile a, RecipeProfile b)
    {
        var keys = a.Components.Keys
            .Union(b.Components.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<SapRecipeComponentComparison>(keys.Count);

        foreach (var key in keys)
        {
            a.Components.TryGetValue(key, out var ca);
            b.Components.TryGetValue(key, out var cb);

            var code = ca is null ? "ONLY_B" : cb is null ? "ONLY_A" : "SAME";
            decimal? diff = null;

            if (ca is not null && cb is not null &&
                ca.IsFixed == cb.IsFixed &&
                ca.IsQuantityComparable && cb.IsQuantityComparable &&
                string.Equals(ca.Dimension, cb.Dimension, StringComparison.OrdinalIgnoreCase))
            {
                if (ca.IsFixed)
                {
                    var max = Math.Max(Math.Abs(ca.BaseQuantity), Math.Abs(cb.BaseQuantity));
                    diff = max <= 0m
                        ? 0m
                        : Math.Abs(ca.BaseQuantity - cb.BaseQuantity) / max * 100m;
                }
                else if (ca.SharePercent.HasValue && cb.SharePercent.HasValue)
                {
                    diff = Math.Abs(ca.SharePercent.Value - cb.SharePercent.Value);
                }
            }

            result.Add(new SapRecipeComponentComparison
            {
                ComponentNumber = key,
                Description = ca?.Description ?? cb?.Description ?? string.Empty,
                DifferenceCode = code,
                QuantityA = ca?.DisplayQuantity,
                UnitA = ca?.DisplayUnit ?? string.Empty,
                ShareA = ca?.SharePercent,
                IsFixedA = ca is null ? null : ca.IsFixed,
                QuantityB = cb?.DisplayQuantity,
                UnitB = cb?.DisplayUnit ?? string.Empty,
                ShareB = cb?.SharePercent,
                IsFixedB = cb is null ? null : cb.IsFixed,
                DifferencePercentagePoints = diff
            });
        }

        return result;
    }

    private static bool IsRecipeMaterial(SapMaterial material)
    {
        if (string.Equals(material.MaterialKind, nameof(SapMaterialKind.Recipe), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return material.MaterialNumber.StartsWith("17", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryToBaseQuantity(
        decimal quantity,
        string unit,
        out string dimension,
        out decimal baseQuantity,
        out string canonicalUnit)
    {
        var code = (unit ?? string.Empty).Trim().ToUpperInvariant().Replace(".", string.Empty);

        switch (code)
        {
            case "KG":
                dimension = "MASS";
                baseQuantity = quantity * 1000m;
                canonicalUnit = "g";
                return true;
            case "G":
                dimension = "MASS";
                baseQuantity = quantity;
                canonicalUnit = "g";
                return true;
            case "MG":
                dimension = "MASS";
                baseQuantity = quantity / 1000m;
                canonicalUnit = "g";
                return true;
            case "L":
            case "LTR":
                dimension = "VOLUME";
                baseQuantity = quantity * 1000m;
                canonicalUnit = "ml";
                return true;
            case "ML":
                dimension = "VOLUME";
                baseQuantity = quantity;
                canonicalUnit = "ml";
                return true;
            case "ST":
            case "STK":
            case "PC":
            case "PCS":
                dimension = "COUNT";
                baseQuantity = quantity;
                canonicalUnit = "pcs";
                return true;
            default:
                dimension = string.Empty;
                baseQuantity = 0m;
                canonicalUnit = unit ?? string.Empty;
                return false;
        }
    }

    private static bool IsBetter(SapRecipeSimilarityPair candidate, SapRecipeSimilarityPair existing)
    {
        var candidateRank = KindRank(candidate.Kind);
        var existingRank = KindRank(existing.Kind);
        if (candidateRank != existingRank)
        {
            return candidateRank < existingRank;
        }

        if (Math.Abs(candidate.ComponentSimilarityPercent - existing.ComponentSimilarityPercent) > 0.0001d)
        {
            return candidate.ComponentSimilarityPercent > existing.ComponentSimilarityPercent;
        }

        return (candidate.MaxRatioDifferencePercentagePoints ?? decimal.MaxValue)
               < (existing.MaxRatioDifferencePercentagePoints ?? decimal.MaxValue);
    }

    private static int KindRank(SapRecipeSimilarityKind kind) => kind switch
    {
        SapRecipeSimilarityKind.Identical => 0,
        SapRecipeSimilarityKind.SameComponentsDifferentRatio => 1,
        _ => 2
    };

    private static string RecipePairKey(string left, string right)
    {
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{left}\u001f{right}"
            : $"{right}\u001f{left}";
    }

    private sealed class RecipeProfile
    {
        public string MaterialNumber { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Alternative { get; init; } = string.Empty;
        public IReadOnlyDictionary<string, RecipeComponent> Components { get; init; } =
            new Dictionary<string, RecipeComponent>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RecipeComponent
    {
        public string ComponentNumber { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public bool IsFixed { get; init; }
        public bool IsQuantityComparable { get; init; }
        public string Dimension { get; init; } = string.Empty;
        public decimal BaseQuantity { get; init; }
        public decimal? DisplayQuantity { get; init; }
        public string DisplayUnit { get; init; } = string.Empty;
        public decimal? SharePercent { get; set; }
    }
}
