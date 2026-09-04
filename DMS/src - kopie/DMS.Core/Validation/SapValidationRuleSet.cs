namespace DMS.Core.Sap.Validation;

public sealed class SapValidationRuleSet
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "SAP validační pravidla DMS";

    public List<SapValidationRule> Rules { get; set; } = new();
}

public sealed class SapValidationRule
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// BOM_HEADER, BOM_ITEM, ROUTING_OPERATION, CROSS_PLANT...
    /// </summary>
    public string Scope { get; set; } = "BOM_ITEM";

    /// <summary>
    /// Info, Warning, Error
    /// </summary>
    public string Severity { get; set; } = "Warning";

    public string Message { get; set; } = string.Empty;

    public List<SapValidationCondition> Conditions { get; set; } = new();

    public string Note { get; set; } = string.Empty;
}

public sealed class SapValidationCondition
{
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Equals, NotEquals, Contains, StartsWith, EndsWith,
    /// IsEmpty, IsNotEmpty, IsTrue, IsFalse,
    /// GreaterThan, GreaterOrEqual, LessThan, LessOrEqual
    /// </summary>
    public string Operator { get; set; } = "Equals";

    public string Value { get; set; } = string.Empty;
}