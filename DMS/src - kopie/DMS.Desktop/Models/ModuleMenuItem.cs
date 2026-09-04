namespace DMS.Desktop.Models;

public sealed class ModuleMenuItem
{
    /// <summary>
    /// Internal module name from transactions.json. Keep it stable for filtering.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Localized UI text.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    public string DisplayText => string.IsNullOrWhiteSpace(DisplayName)
        ? Name
        : DisplayName;
}
