using DMS.Core.Sap;

namespace DMS.Core.Recipes;

public enum RecipeImportKind
{
    SprayCoating,
    ScreenPrinting
}

public sealed class RecipeImportResult
{
    public RecipeImportKind Kind { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public string ArticleNumber { get; set; } = string.Empty;
    public string HdNumber { get; set; } = string.Empty;
    public string RecipeNumber { get; set; } = string.Empty;
    public string KText { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Device { get; set; } = string.Empty;
    public string GeneralNote { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; } = DateTime.Now;
    public List<RecipeLayer> Layers { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public IEnumerable<RecipeComponent> AllComponents =>
        Layers.SelectMany(layer => layer.Components);
}

public sealed class RecipeLayer
{
    public int LayerNumber { get; set; }
    public string LayerCode => $"Lay{LayerNumber * 10:00}";
    public string KText { get; set; } = string.Empty;
    public bool ProcessOnly { get; set; }
    public string ProcessText { get; set; } = string.Empty;
    public decimal SourceTotalGrams { get; set; }
    public decimal BaseQuantityGrams { get; set; } = 1000m;
    public decimal FinalTotalGrams => Components.Sum(component => component.BomGrams);
    public List<RecipeComponent> Components { get; set; } = new();
    public List<string> TextItems { get; set; } = new();

    public int ComponentCount => Components.Count;
    public string TypeText => ProcessOnly ? "PROCESS" : "BOM";
}

public sealed class RecipeComponent
{
    public string SourceText { get; set; } = string.Empty;
    public decimal SourceGrams { get; set; }
    public decimal BomGrams { get; set; }
    public string SapMaterialNumber { get; set; } = string.Empty;
    public string SapDescription { get; set; } = string.Empty;
    public double MatchScore { get; set; }
    public string MatchMethod { get; set; } = string.Empty;
    public bool IsHardener { get; set; }
    public string HardenerStatus { get; set; } = string.Empty;
    public string GeneratedByRule { get; set; } = string.Empty;

    public string MatchScoreText => SapMaterialNumber.Length == 0
        ? "-"
        : $"{MatchScore:P0}";

    public string SourceQuantityText => IsHardener
        ? "AUTO"
        : SourceGrams.ToString("0.######");
}

public sealed class RecipeMaterialCandidate
{
    public SapMaterial Material { get; set; } = new();
    public double Score { get; set; }
    public string Reason { get; set; } = string.Empty;

    public string MaterialNumber => Material.MaterialNumber;
    public string Description => Material.Description;
    public string ScoreText => $"{Score:P0}";
}

public sealed class RecipeHardenerRule
{
    public string Family { get; set; } = string.Empty;
    public decimal RatioPercent { get; set; }
    public string HardenerText { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class RecipeAliasRule
{
    public string SourceText { get; set; } = string.Empty;
    public string MaterialNumber { get; set; } = string.Empty;
    public string SapDescription { get; set; } = string.Empty;
}

public sealed class RecipeImportSettings
{
    public string SapComponentPrefix { get; set; } = "11";
    public double AutoMatchThreshold { get; set; } = 0.82;
    public double AutoMatchMinimumGap { get; set; } = 0.05;

    // Screen-print hardener behavior:
    // - if the source document already contains the configured hardener,
    //   keep it and verify its percentage against the rule;
    // - otherwise optionally add it above the 1 kg base quantity.
    public bool AddMissingHardener { get; set; } = true;
    public decimal HardenerTolerancePercent { get; set; } = 0.25m;

    public List<RecipeHardenerRule> HardenerRules { get; set; } = new()
    {
        new() { Family = "UVGL", RatioPercent = 5m, HardenerText = "UV-HV 8", IsActive = true },
        new() { Family = "LEDGL", RatioPercent = 4m, HardenerText = "UV-HV 8", IsActive = true },
        new() { Family = "MGL", RatioPercent = 5m, HardenerText = "MGLH", IsActive = true }
    };
    public List<RecipeAliasRule> Aliases { get; set; } = new();
}
