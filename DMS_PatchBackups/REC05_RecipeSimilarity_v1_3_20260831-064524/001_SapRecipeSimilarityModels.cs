namespace DMS.Core.Sap;

public enum SapRecipeSimilarityKind
{
    Identical,
    SameComponentsDifferentRatio,
    SimilarComponents
}

public sealed class SapRecipeSimilarityAnalysis
{
    public int RecipeCount { get; init; }
    public int RecipeVariantCount { get; init; }
    public int IdenticalPairCount { get; init; }
    public int SameComponentsPairCount { get; init; }
    public int SimilarPairCount { get; init; }
    public IReadOnlyList<SapRecipeSimilarityPair> Pairs { get; init; } = Array.Empty<SapRecipeSimilarityPair>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class SapRecipeSimilarityPair
{
    public string RecipeANumber { get; init; } = string.Empty;
    public string RecipeADescription { get; init; } = string.Empty;
    public string RecipeAAlternative { get; init; } = string.Empty;
    public string RecipeBNumber { get; init; } = string.Empty;
    public string RecipeBDescription { get; init; } = string.Empty;
    public string RecipeBAlternative { get; init; } = string.Empty;
    public SapRecipeSimilarityKind Kind { get; init; }
    public double ComponentSimilarityPercent { get; init; }
    public int CommonComponentCount { get; init; }
    public int UnionComponentCount { get; init; }
    public decimal? MaxRatioDifferencePercentagePoints { get; init; }
    public IReadOnlyList<SapRecipeComponentComparison> Components { get; init; } = Array.Empty<SapRecipeComponentComparison>();
}

public sealed class SapRecipeComponentComparison
{
    public string ComponentNumber { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string DifferenceCode { get; init; } = string.Empty;
    public decimal? QuantityA { get; init; }
    public string UnitA { get; init; } = string.Empty;
    public decimal? ShareA { get; init; }
    public bool? IsFixedA { get; init; }
    public decimal? QuantityB { get; init; }
    public string UnitB { get; init; } = string.Empty;
    public decimal? ShareB { get; init; }
    public bool? IsFixedB { get; init; }
    public decimal? DifferencePercentagePoints { get; init; }
}
