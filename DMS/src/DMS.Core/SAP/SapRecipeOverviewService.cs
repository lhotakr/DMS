namespace DMS.Core.Sap;

public sealed class SapRecipeOverviewService
{
    private readonly IReadOnlyList<SapMaterial> _materials;
    private readonly IReadOnlyList<SapBom> _boms;

    private readonly Dictionary<string, SapMaterial> _materialsByNumber;
    private readonly Dictionary<string, int> _recipeUsageInArticlesCountByRecipe;
    private readonly Dictionary<string, int> _recipeBomItemCountByRecipe;
    
    public SapRecipeOverviewService(
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

        _recipeUsageInArticlesCountByRecipe = BuildRecipeUsageInArticlesIndex();
        _recipeBomItemCountByRecipe = BuildRecipeBomItemCountIndex();
    }

    public IReadOnlyList<SapRecipeSearchRow> SearchRecipes(
        string? textFilter,
        string? usedInArticleFilter)
    {
        var normalizedText = textFilter?.Trim() ?? string.Empty;
        var normalizedArticle = NormalizeMaterialNumber(usedInArticleFilter ?? string.Empty);

        var recipes = _materials
            .Where(IsRecipeMaterial)
            .ToList();

        if (!string.IsNullOrWhiteSpace(normalizedText))
        {
            recipes = recipes
                .Where(item =>
                    ContainsIgnoreCase(item.MaterialNumber, normalizedText) ||
                    ContainsIgnoreCase(item.Description, normalizedText) ||
                    ContainsIgnoreCase(item.OldMaterialNumber, normalizedText))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(normalizedArticle))
        {
            var recipesUsedInArticle = _boms
                .Where(bom => string.Equals(
                    NormalizeMaterialNumber(bom.MaterialNumber),
                    normalizedArticle,
                    StringComparison.OrdinalIgnoreCase))
                .SelectMany(bom => bom.Items)
                .Select(item => NormalizeMaterialNumber(item.ComponentNumber))
                .Where(IsRecipeNumber)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            recipes = recipes
                .Where(item => recipesUsedInArticle.Contains(NormalizeMaterialNumber(item.MaterialNumber)))
                .ToList();
        }

        return recipes
            .OrderBy(item => item.MaterialNumber)
            .Take(100)
            .Select(recipe =>
            {
                var recipeNumber = NormalizeMaterialNumber(recipe.MaterialNumber);

                return new SapRecipeSearchRow
                {
                    RecipeNumber = recipeNumber,
                    Description = recipe.Description,
                    UsedInArticleCount = _recipeUsageInArticlesCountByRecipe.TryGetValue(recipeNumber, out var usageCount)
                        ? usageCount
                        : 0,
                    BomItemCount = _recipeBomItemCountByRecipe.TryGetValue(recipeNumber, out var bomItemCount)
                        ? bomItemCount
                        : 0
                };
            })
            .ToList();
    }

    public SapRecipeOverview BuildOverview(string recipeNumber)
    {
        var normalizedRecipeNumber = NormalizeMaterialNumber(recipeNumber);

        _materialsByNumber.TryGetValue(normalizedRecipeNumber, out var recipeMaterial);

        var overview = new SapRecipeOverview
        {
            RecipeNumber = normalizedRecipeNumber,
            RecipeDescription = recipeMaterial?.Description ?? string.Empty,
            RecipeMaterial = recipeMaterial
        };

        if (recipeMaterial is null)
        {
            overview.Messages.Add($"Receptura {normalizedRecipeNumber} nebyla nalezena v SAP materiálové cache.");
        }

        var recipeBoms = _boms
            .Where(bom => string.Equals(
                NormalizeMaterialNumber(bom.MaterialNumber),
                normalizedRecipeNumber,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(bom => bom.Plant)
            .ThenBy(bom => NormalizeAlternativeForSort(bom.Alternative))
            .ToList();

        if (recipeBoms.Count == 0)
        {
            overview.Messages.Add($"Pro recepturu {normalizedRecipeNumber} nebyl nalezen kusovník.");
        }

        overview.BomVariants.AddRange(
            recipeBoms.Select(CreateRecipeBomVariant));

        overview.UsedInArticles.AddRange(
            FindRecipeUsageInArticles(normalizedRecipeNumber));

        overview.ComponentUsageInOtherRecipes.AddRange(
            FindComponentUsageInOtherRecipes(normalizedRecipeNumber, recipeBoms));

        return overview;
    }

    private SapRecipeBomVariant CreateRecipeBomVariant(SapBom bom)
    {
        return new SapRecipeBomVariant
        {
            Plant = bom.Plant,
            BomNumber = bom.BomNumber,
            Alternative = NormalizeAlternativeDisplay(bom.Alternative),
            BomUsage = bom.BomUsage,
            BaseQuantity = bom.BaseQuantity,
            BaseUnit = bom.BaseUnit,
            Items = bom.Items
                .OrderBy(item => item.Position)
                .Select(CreateRecipeBomItemRow)
                .ToList()
        };
    }

    private SapRecipeBomItemRow CreateRecipeBomItemRow(SapBomItem item)
    {
        var componentNumber = NormalizeMaterialNumber(item.ComponentNumber);
        var componentDescription = GetMaterialDescription(componentNumber, item.ItemText);

        return new SapRecipeBomItemRow
        {
            Position = item.Position,
            ItemCategory = item.ItemCategory,
            ComponentNumber = componentNumber,
            ComponentDescription = componentDescription,
            ComponentKind = item.ComponentKind,
            Quantity = item.Quantity,
            Unit = item.Unit,
            IsFixedQuantity = item.IsFixedQuantity,
            ScrapPercent = item.ScrapPercent
        };
    }

    private List<SapRecipeUsageRow> FindRecipeUsageInArticles(string recipeNumber)
    {
        var normalizedRecipeNumber = NormalizeMaterialNumber(recipeNumber);

        return _boms
            .Where(bom => IsArticleNumber(bom.MaterialNumber))
            .SelectMany(bom => bom.Items
                .Where(item => string.Equals(
                    NormalizeMaterialNumber(item.ComponentNumber),
                    normalizedRecipeNumber,
                    StringComparison.OrdinalIgnoreCase))
                .Select(item =>
                {
                    var articleNumber = NormalizeMaterialNumber(bom.MaterialNumber);

                    return new SapRecipeUsageRow
                    {
                        ArticleNumber = articleNumber,
                        ArticleDescription = GetMaterialDescription(articleNumber, string.Empty),

                        Plant = bom.Plant,
                        BomNumber = bom.BomNumber,
                        Alternative = NormalizeAlternativeDisplay(bom.Alternative),
                        Position = item.Position,

                        Quantity = item.Quantity,
                        Unit = item.Unit
                    };
                }))
            .OrderBy(item => item.ArticleNumber)
            .ThenBy(item => item.Plant)
            .ThenBy(item => item.Alternative)
            .ThenBy(item => item.Position)
            .ToList();
    }

    private List<SapRecipeComponentUsageRow> FindComponentUsageInOtherRecipes(
        string recipeNumber,
        IReadOnlyList<SapBom> recipeBoms)
    {
        var componentNumbers = recipeBoms
            .SelectMany(bom => bom.Items)
            .Select(item => NormalizeMaterialNumber(item.ComponentNumber))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (componentNumbers.Count == 0)
        {
            return new List<SapRecipeComponentUsageRow>();
        }

        return _boms
            .Where(bom =>
                IsRecipeNumber(bom.MaterialNumber) &&
                !string.Equals(
                    NormalizeMaterialNumber(bom.MaterialNumber),
                    recipeNumber,
                    StringComparison.OrdinalIgnoreCase))
            .SelectMany(bom => bom.Items
                .Where(item => componentNumbers.Contains(NormalizeMaterialNumber(item.ComponentNumber)))
                .Select(item =>
                {
                    var componentNumber = NormalizeMaterialNumber(item.ComponentNumber);
                    var otherRecipeNumber = NormalizeMaterialNumber(bom.MaterialNumber);

                    return new SapRecipeComponentUsageRow
                    {
                        ComponentNumber = componentNumber,
                        ComponentDescription = GetMaterialDescription(componentNumber, item.ItemText),
                        RecipeNumber = otherRecipeNumber,
                        RecipeDescription = GetMaterialDescription(otherRecipeNumber, string.Empty),
                        Plant = bom.Plant,
                        BomNumber = bom.BomNumber,
                        Alternative = NormalizeAlternativeDisplay(bom.Alternative),
                        Position = item.Position,
                        Quantity = item.Quantity,
                        Unit = item.Unit
                    };
                }))
            .OrderBy(item => item.ComponentNumber)
            .ThenBy(item => item.RecipeNumber)
            .ThenBy(item => item.Position)
            .ToList();
    }

    private string GetMaterialDescription(string materialNumber, string fallback)
    {
        var normalized = NormalizeMaterialNumber(materialNumber);

        if (_materialsByNumber.TryGetValue(normalized, out var material) &&
            !string.IsNullOrWhiteSpace(material.Description))
        {
            return material.Description;
        }

        return fallback ?? string.Empty;
    }

    private static bool IsRecipeMaterial(SapMaterial material)
    {
        return IsRecipeNumber(material.MaterialNumber)
               || string.Equals(material.MaterialKind, "Recipe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecipeNumber(string? materialNumber)
    {
        return NormalizeMaterialNumber(materialNumber ?? string.Empty).StartsWith("17", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsArticleNumber(string? materialNumber)
    {
        return NormalizeMaterialNumber(materialNumber ?? string.Empty).StartsWith("10", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsIgnoreCase(string? value, string text)
    {
        return value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string NormalizeMaterialNumber(string value)
    {
        value = value.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.All(char.IsDigit)
            ? value.PadLeft(10, '0')
            : value;
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
    private Dictionary<string, int> BuildRecipeUsageInArticlesIndex()
    {
        return _boms
            .Where(bom => IsArticleNumber(bom.MaterialNumber))
            .SelectMany(bom => bom.Items
                .Select(item => NormalizeMaterialNumber(item.ComponentNumber))
                .Where(IsRecipeNumber)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(recipeNumber => recipeNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, int> BuildRecipeBomItemCountIndex()
    {
        return _boms
            .Where(bom => IsRecipeNumber(bom.MaterialNumber))
            .GroupBy(
                bom => NormalizeMaterialNumber(bom.MaterialNumber),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(bom => bom.Items.Count),
                StringComparer.OrdinalIgnoreCase);
    }
}