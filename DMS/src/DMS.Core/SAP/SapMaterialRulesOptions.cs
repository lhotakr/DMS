namespace DMS.Core.Sap;

public sealed class SapMaterialRulesOptions
{
    public int Version { get; set; } = 1;

    public string DefaultAction { get; set; } = "Ignore";

    public List<SapMaterialNumberRule> MaterialNumberRules { get; set; } = new();

    public List<SapMaterialTextRule> TextClassificationRules { get; set; } = new();
}

public sealed class SapMaterialTextRule
{
    public string Name { get; set; } = string.Empty;

    public string AppliesToMaterialKind { get; set; } = string.Empty;

    public string MaterialKind { get; set; } = string.Empty;

    public string Field { get; set; } = "Description";

    public string? Contains { get; set; }

    public string? StartsWith { get; set; }

    public string? Regex { get; set; }

    public bool CaseSensitive { get; set; }

    public string Description { get; set; } = string.Empty;
}