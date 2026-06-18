namespace DMS.Core.Sap;

public sealed class SapRecipeOverview
{
    public string RecipeNumber { get; init; } = string.Empty;
    public string RecipeDescription { get; init; } = string.Empty;

    public SapMaterial? RecipeMaterial { get; init; }

    public List<SapRecipeBomVariant> BomVariants { get; init; } = new();
    public List<SapRecipeUsageRow> UsedInArticles { get; init; } = new();
    public List<SapRecipeComponentUsageRow> ComponentUsageInOtherRecipes { get; init; } = new();

    public List<string> Messages { get; init; } = new();

    public bool HasBom => BomVariants.Any(item => item.Items.Count > 0);
}

public sealed class SapRecipeBomVariant
{
    public string Plant { get; init; } = string.Empty;
    public string BomNumber { get; init; } = string.Empty;
    public string Alternative { get; init; } = string.Empty;
    public string BomUsage { get; init; } = string.Empty;

    public decimal? BaseQuantity { get; init; }
    public string BaseUnit { get; init; } = string.Empty;

    public List<SapRecipeBomItemRow> Items { get; init; } = new();
}

public sealed class SapRecipeBomItemRow
{
    public string Position { get; init; } = string.Empty;
    public string ItemCategory { get; init; } = string.Empty;

    public string ComponentNumber { get; init; } = string.Empty;
    public string ComponentDescription { get; init; } = string.Empty;
    public string ComponentKind { get; init; } = string.Empty;

    public decimal? Quantity { get; init; }
    public string Unit { get; init; } = string.Empty;

    public bool IsFixedQuantity { get; init; }
    public decimal? ScrapPercent { get; init; }
}

public sealed class SapRecipeUsageRow
{
    public string ArticleNumber { get; init; } = string.Empty;
    public string ArticleDescription { get; init; } = string.Empty;

    public string Plant { get; init; } = string.Empty;
    public string BomNumber { get; init; } = string.Empty;
    public string Alternative { get; init; } = string.Empty;
    public string Position { get; init; } = string.Empty;

    public decimal? Quantity { get; init; }
    public string Unit { get; init; } = string.Empty;
}

public sealed class SapRecipeComponentUsageRow
{
    public string ComponentNumber { get; init; } = string.Empty;
    public string ComponentDescription { get; init; } = string.Empty;

    public string RecipeNumber { get; init; } = string.Empty;
    public string RecipeDescription { get; init; } = string.Empty;

    public string Plant { get; init; } = string.Empty;
    public string BomNumber { get; init; } = string.Empty;
    public string Alternative { get; init; } = string.Empty;
    public string Position { get; init; } = string.Empty;

    public decimal? Quantity { get; init; }
    public string Unit { get; init; } = string.Empty;
}

public sealed class SapRecipeSearchRow
{
    public string RecipeNumber { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int UsedInArticleCount { get; init; }
    public int BomItemCount { get; init; }
}