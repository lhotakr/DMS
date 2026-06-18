namespace DMS.Core.Sap;

public sealed class SapDecorationRuleService
{
    private readonly Dictionary<string, SapDecorationRule> _decorations;

    public SapDecorationRuleService(SapDecorationRulesOptions options)
    {
        _decorations = options.Decorations
            .Where(item => item.IsActive)
            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .GroupBy(item => item.Code.Trim().ToUpperInvariant())
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
    }

    public string GetName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "Neznámý dekorační krok";
        }

        var normalizedCode = code.Trim().ToUpperInvariant();

        if (_decorations.TryGetValue(normalizedCode, out var rule))
        {
            return rule.Name;
        }

        return $"Neznámý dekorační krok ({normalizedCode})";
    }

    public SapDecorationRule? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        _decorations.TryGetValue(code.Trim().ToUpperInvariant(), out var rule);

        return rule;
    }
}