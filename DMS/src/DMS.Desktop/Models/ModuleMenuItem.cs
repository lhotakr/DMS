namespace DMS.Desktop.Models;

public sealed class ModuleMenuItem
{
    public string Name { get; init; } = string.Empty;

    public string DisplayText => Name;
}