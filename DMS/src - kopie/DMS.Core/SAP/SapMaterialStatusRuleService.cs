namespace DMS.Core.Sap;

public sealed class SapMaterialStatusRuleService
{
    private readonly Dictionary<string, SapMaterialStatusRule> _rulesByCode;

    public SapMaterialStatusRuleService(IReadOnlyList<SapMaterialStatusRule> rules)
    {
        _rulesByCode = rules
            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .GroupBy(item => item.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
    }

    public string FormatStatus(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var normalizedCode = code.Trim();

        return _rulesByCode.TryGetValue(normalizedCode, out var rule)
            ? $"{normalizedCode} ({rule.Name})"
            : normalizedCode;
    }
}