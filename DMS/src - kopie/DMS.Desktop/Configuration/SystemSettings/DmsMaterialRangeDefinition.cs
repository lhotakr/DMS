namespace DMS.Desktop.Configuration.SystemSettings;

public sealed class DmsMaterialRangeDefinition
{
    public string Name { get; set; } = string.Empty;

    public long From { get; set; }

    public long To { get; set; }

    public bool IsActive { get; set; }
}