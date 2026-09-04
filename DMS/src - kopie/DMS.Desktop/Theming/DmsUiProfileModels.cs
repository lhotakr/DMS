using System.Collections.ObjectModel;

namespace DMS.Desktop.Theming;

public sealed class DmsUiProfile
{
    public int SchemaVersion { get; set; } = 1;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string ModifiedBy { get; set; } = string.Empty;
    public DateTime ModifiedAt { get; set; } = DateTime.Now;

    public DmsUiLayer Global { get; set; } = new();

    public Dictionary<string, DmsUiLayer> Modules { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, DmsUiLayer> Transactions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public DmsUiProfile Clone()
    {
        return new DmsUiProfile
        {
            SchemaVersion = SchemaVersion,
            Code = Code,
            Name = Name,
            Description = Description,
            Version = Version,
            ModifiedBy = ModifiedBy,
            ModifiedAt = ModifiedAt,
            Global = Global.Clone(),
            Modules = Modules.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone(),
                StringComparer.OrdinalIgnoreCase),
            Transactions = Transactions.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone(),
                StringComparer.OrdinalIgnoreCase)
        };
    }
}

public sealed class DmsUiLayer
{
    public Dictionary<string, string> Resources { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<DmsUiPropertyOverride> Properties { get; set; } = new();

    /// <summary>
    /// Optional self-contained WPF ResourceDictionary.
    /// clr-namespace, x:Class, Source and event handlers are rejected.
    /// </summary>
    public string AdvancedXaml { get; set; } = string.Empty;

    public DmsUiLayer Clone()
    {
        return new DmsUiLayer
        {
            Resources = new Dictionary<string, string>(
                Resources,
                StringComparer.OrdinalIgnoreCase),
            Properties = Properties
                .Select(item => item.Clone())
                .ToList(),
            AdvancedXaml = AdvancedXaml
        };
    }
}

public sealed class DmsUiPropertyOverride
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsActive { get; set; } = true;

    /// <summary>TYPE or NAME.</summary>
    public string SelectorKind { get; set; } = "TYPE";

    /// <summary>Example: DataGrid, Button, ResultsGrid.</summary>
    public string Selector { get; set; } = string.Empty;

    /// <summary>
    /// CLR/WPF property such as FontSize, RowHeight, Height, Padding,
    /// Background, Visibility, Margin, CornerRadius...
    /// </summary>
    public string Property { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DmsUiPropertyOverride Clone() => new()
    {
        Id = Id,
        IsActive = IsActive,
        SelectorKind = SelectorKind,
        Selector = Selector,
        Property = Property,
        Value = Value
    };
}

public sealed class DmsUiActiveProfile
{
    public string ProfileCode { get; set; } = string.Empty;
}

public sealed record DmsUiProfileSummary(
    string Code,
    string Name,
    int Version,
    DateTime ModifiedAt)
{
    public string DisplayName => $"{Name} [{Code}] v{Version}";
}

public sealed record DmsUiScopeOption(
    string Kind,
    string Code,
    string DisplayName,
    string ModuleCode = "")
{
    public string ScopeKey =>
        string.Equals(Kind, "GLOBAL", StringComparison.OrdinalIgnoreCase)
            ? "GLOBAL"
            : $"{Kind}:{Code}";
}

public sealed class DmsUiResourceRow
{
    public string Key { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string InheritedValue { get; set; } = string.Empty;
    public string OverrideValue { get; set; } = string.Empty;
    public string EffectiveValue { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public sealed record DmsUiResourceDescriptor(
    string Key,
    string ResourceType,
    string CurrentValue);

public sealed record DmsUiValidationIssue(
    string Severity,
    string Scope,
    string Code,
    string Details);

public sealed record DmsUiApplyIssue(
    string Scope,
    string Selector,
    string Property,
    string Details);

public sealed class DmsUiPreviewRow
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
