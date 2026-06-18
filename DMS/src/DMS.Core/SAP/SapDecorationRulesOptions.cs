namespace DMS.Core.Sap;

public sealed class SapDecorationRulesOptions
{
    public int Version { get; set; } = 1;

    public List<SapDecorationRule> Decorations { get; set; } = new();
}

public sealed class SapDecorationRule
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Technology { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public string Description { get; set; } = string.Empty;
}