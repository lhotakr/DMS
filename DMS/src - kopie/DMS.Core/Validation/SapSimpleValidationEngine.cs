using System.Globalization;
using System.Reflection;

namespace DMS.Core.Sap.Validation;

public sealed class SapSimpleValidationEngine
{
    public IReadOnlyList<SapValidationFinding> Validate<T>(
        IEnumerable<SapValidationRule> rules,
        string scope,
        T context)
    {
        var findings = new List<SapValidationFinding>();

        foreach (var rule in rules.Where(item =>
                     item.Enabled &&
                     string.Equals(item.Scope, scope, StringComparison.OrdinalIgnoreCase)))
        {
            var isMatch = rule.Conditions.All(condition =>
                EvaluateCondition(context, condition));

            if (!isMatch)
            {
                continue;
            }

            findings.Add(new SapValidationFinding
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                Scope = rule.Scope,
                Severity = rule.Severity,
                Message = FormatMessage(rule.Message, context)
            });
        }

        return findings;
    }

    private static bool EvaluateCondition<T>(
        T context,
        SapValidationCondition condition)
    {
        if (context is null)
        {
            return false;
        }

        var property = typeof(T).GetProperty(
            condition.Field,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null)
        {
            return false;
        }

        var actualValue = property.GetValue(context);
        var expectedValue = condition.Value;

        return condition.Operator switch
        {
            "Equals" => ValuesEqual(actualValue, expectedValue),
            "NotEquals" => !ValuesEqual(actualValue, expectedValue),

            "Contains" => Contains(actualValue, expectedValue),
            "StartsWith" => StartsWith(actualValue, expectedValue),
            "EndsWith" => EndsWith(actualValue, expectedValue),

            "IsEmpty" => IsEmpty(actualValue),
            "IsNotEmpty" => !IsEmpty(actualValue),

            "IsTrue" => IsBool(actualValue, expected: true),
            "IsFalse" => IsBool(actualValue, expected: false),

            "GreaterThan" => CompareDecimal(actualValue, expectedValue, (left, right) => left > right),
            "GreaterOrEqual" => CompareDecimal(actualValue, expectedValue, (left, right) => left >= right),
            "LessThan" => CompareDecimal(actualValue, expectedValue, (left, right) => left < right),
            "LessOrEqual" => CompareDecimal(actualValue, expectedValue, (left, right) => left <= right),

            "EqualsField" => ValuesEqual(actualValue, GetContextPropertyValue(context, expectedValue)),
            "NotEqualsField" => !ValuesEqual(actualValue, GetContextPropertyValue(context, expectedValue)),

            _ => false
        };
    }

    private static bool ValuesEqual(object? actualValue, object? expectedValue)
    {
        if (actualValue is null && expectedValue is null)
        {
            return true;
        }

        if (actualValue is null || expectedValue is null)
        {
            return false;
        }

        if (actualValue is bool actualBool)
        {
            if (expectedValue is bool expectedBool)
            {
                return actualBool == expectedBool;
            }

            return bool.TryParse(expectedValue.ToString(), out var parsedBool)
                   && actualBool == parsedBool;
        }

        if (TryConvertDecimal(actualValue, out var actualDecimal) &&
            TryConvertDecimal(expectedValue, out var expectedDecimal))
        {
            return Math.Abs(actualDecimal - expectedDecimal) < 0.0001m;
        }

        return string.Equals(
            actualValue.ToString(),
            expectedValue.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(object? actualValue, string expectedValue)
    {
        return actualValue?.ToString()?.Contains(
            expectedValue,
            StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool StartsWith(object? actualValue, string expectedValue)
    {
        return actualValue?.ToString()?.StartsWith(
            expectedValue,
            StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool EndsWith(object? actualValue, string expectedValue)
    {
        return actualValue?.ToString()?.EndsWith(
            expectedValue,
            StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsEmpty(object? actualValue)
    {
        if (actualValue is null)
        {
            return true;
        }

        if (actualValue is string text)
        {
            return string.IsNullOrWhiteSpace(text);
        }

        return false;
    }

    private static bool IsBool(object? actualValue, bool expected)
    {
        if (actualValue is bool boolValue)
        {
            return boolValue == expected;
        }

        if (bool.TryParse(actualValue?.ToString(), out var parsed))
        {
            return parsed == expected;
        }

        return false;
    }

    private static bool CompareDecimal(
        object? actualValue,
        string expectedValue,
        Func<decimal, decimal, bool> compare)
    {
        if (!TryConvertDecimal(actualValue, out var actualDecimal))
        {
            return false;
        }

        if (!TryConvertDecimal(expectedValue, out var expectedDecimal))
        {
            return false;
        }

        return compare(actualDecimal, expectedDecimal);
    }

    private static bool TryConvertDecimal(object? value, out decimal result)
    {
        result = 0m;

        if (value is null)
        {
            return false;
        }

        if (value is decimal decimalValue)
        {
            result = decimalValue;
            return true;
        }

        var text = value.ToString();

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim().Replace(',', '.');

        return decimal.TryParse(
            text,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static object? GetContextPropertyValue<T>(T context, string propertyName)
    {
        if (context is null || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        var property = typeof(T).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        return property?.GetValue(context);
    }

    private static string FormatMessage<T>(string template, T context)
    {
        if (context is null)
        {
            return template;
        }

        var result = template;

        foreach (var property in typeof(T).GetProperties())
        {
            var token = "{" + property.Name + "}";
            var value = property.GetValue(context)?.ToString() ?? string.Empty;

            result = result.Replace(token, value);
        }

        return result;
    }
}