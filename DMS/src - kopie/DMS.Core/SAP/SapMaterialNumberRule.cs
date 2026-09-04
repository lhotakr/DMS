namespace DMS.Core.Sap;

public sealed class SapMaterialNumberRule
{
    public string Name { get; set; } = string.Empty;
    public string? SapNumberPrefix { get; set; }
    public string? NumberFrom { get; set; }
    public string? NumberTo { get; set; }
    public string MaterialKind { get; set; } = string.Empty;
    public bool Import { get; set; }
    public string Description { get; set; } = string.Empty;
}