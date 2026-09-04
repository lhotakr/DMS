using DMS.Core.Sap;
using System.Text.RegularExpressions;

namespace DMS.Core.Sap;

public sealed class SapMaterialClassifier
{
    private readonly SapMaterialRulesOptions _rules;

    public SapMaterialClassifier(SapMaterialRulesOptions rules)
    {
        _rules = rules;
    }

    public SapMaterialClassificationResult Classify(
        string materialNumber,
        string description,
        string oldMaterialNumber)
    {
        var numberRule = _rules.MaterialNumberRules
            .FirstOrDefault(rule => IsMaterialNumberInRule(materialNumber, rule));

        if (numberRule is null)
        {
            return new SapMaterialClassificationResult
            {
                MaterialNumber = materialNumber,
                MaterialKind = "Ignored",
                Import = false,
                MatchedRuleName = null
            };
        }

        var materialKind = numberRule.MaterialKind;

        foreach (var textRule in _rules.TextClassificationRules)
        {
            if (!string.Equals(
                    textRule.AppliesToMaterialKind,
                    materialKind,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fieldValue = GetFieldValue(
                textRule.Field,
                description,
                oldMaterialNumber);

            if (IsTextRuleMatch(fieldValue, textRule))
            {
                materialKind = textRule.MaterialKind;
                break;
            }
        }

        return new SapMaterialClassificationResult
        {
            MaterialNumber = materialNumber,
            MaterialKind = materialKind,
            Import = numberRule.Import,
            SapNumberPrefix = numberRule.SapNumberPrefix,
            MatchedRuleName = numberRule.Name
        };
    }

    private static bool IsMaterialNumberInRule(
        string materialNumber,
        SapMaterialNumberRule rule)
    {
        if (string.IsNullOrWhiteSpace(materialNumber))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.SapNumberPrefix)
            && !materialNumber.StartsWith(rule.SapNumberPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!long.TryParse(materialNumber, out var materialNumberValue))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.NumberFrom)
            && long.TryParse(rule.NumberFrom, out var from)
            && materialNumberValue < from)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.NumberTo)
            && long.TryParse(rule.NumberTo, out var to)
            && materialNumberValue > to)
        {
            return false;
        }

        return true;
    }

    private static string GetFieldValue(
        string field,
        string description,
        string oldMaterialNumber)
    {
        return field switch
        {
            "OldMaterialNumber" => oldMaterialNumber ?? string.Empty,
            "Description" => description ?? string.Empty,
            _ => description ?? string.Empty
        };
    }

    private static bool IsTextRuleMatch(string value, SapMaterialTextRule rule)
    {
        var comparison = rule.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (!string.IsNullOrWhiteSpace(rule.Contains)
            && value.Contains(rule.Contains, comparison))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(rule.StartsWith)
            && value.StartsWith(rule.StartsWith, comparison))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(rule.Regex))
        {
            var options = rule.CaseSensitive
                ? RegexOptions.None
                : RegexOptions.IgnoreCase;

            return Regex.IsMatch(value, rule.Regex, options);
        }

        return false;
    }
}