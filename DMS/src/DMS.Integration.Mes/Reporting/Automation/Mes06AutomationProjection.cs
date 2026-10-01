using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DMS.Integration.Mes.Reporting.Automation;

public static class Mes06AutomationProjection
{
    public static Mes06AutomationResult FromObjects<T>(
        string reportCode,
        IReadOnlyList<T> rows,
        params string[] excludedProperties)
    {
        var excluded = excludedProperties.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var properties = typeof(T)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p =>
                p.CanRead &&
                p.GetIndexParameters().Length == 0 &&
                !excluded.Contains(p.Name))
            .ToArray();

        var columns = properties
            .Select(p => new Mes06AutomationColumn
            {
                Key = p.Name,
                Header = SplitPascalCase(p.Name)
            })
            .ToArray();

        var data = rows
            .Select(row =>
            {
                var dictionary = new Dictionary<string, object?>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var property in properties)
                    dictionary[property.Name] = property.GetValue(row);

                return (IReadOnlyDictionary<string, object?>)dictionary;
            })
            .ToArray();

        return new Mes06AutomationResult
        {
            ReportCode = reportCode,
            Columns = columns,
            Rows = data
        };
    }

    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var chars = new List<char>(value.Length + 8);

        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0 &&
                char.IsUpper(value[i]) &&
                !char.IsUpper(value[i - 1]))
            {
                chars.Add(' ');
            }

            chars.Add(value[i]);
        }

        return new string(chars.ToArray());
    }
}
