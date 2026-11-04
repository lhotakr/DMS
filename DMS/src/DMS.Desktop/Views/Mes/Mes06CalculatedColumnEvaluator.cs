using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace DMS.Desktop.Views.Mes;

/// <summary>Definition of a user-calculated MES06 report column.</summary>
internal sealed class Mes06CalculatedColumn
{
    public string Key { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public string Formula { get; set; } = string.Empty;
    public int DecimalPlaces { get; set; } = 1;
    public bool Visible { get; set; } = true;
}

/// <summary>
/// Evaluates calculated columns against an existing report row without modifying the
/// underlying MES or Plachta figures. Results can refer to earlier calculated columns.
/// </summary>
internal static class Mes06CalculatedColumnEvaluator
{
    public static bool TryEvaluateRow(
        object row,
        IReadOnlyList<Mes06CalculatedColumn> definitions,
        out Dictionary<string, double> values,
        out string error)
    {
        values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        error = string.Empty;
        if (row == null) { error = "Report row is null."; return false; }
        if (definitions == null) { error = "Column definitions are null."; return false; }

        var variables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in row.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
            object? raw;
            try { raw = property.GetValue(row); }
            catch (Exception ex) { error = $"Cannot read '{property.Name}': {ex.Message}"; return false; }
            if (raw == null || raw is bool || raw is string) continue;
            if (raw is not IConvertible) continue;
            try
            {
                var number = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                if (!double.IsNaN(number) && !double.IsInfinity(number))
                    variables[property.Name] = number;
            }
            catch (Exception) { /* Non-numeric property. */ }
        }

        var knownKeys = new HashSet<string>(variables.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var column in definitions)
        {
            if (column == null || string.IsNullOrWhiteSpace(column.Key) ||
                !IsIdentifier(column.Key))
            { error = "A calculated column has an invalid key."; return false; }
            if (!knownKeys.Add(column.Key))
            { error = $"Duplicate or conflicting column key: {column.Key}"; return false; }
            if (!Mes06FormulaEngine.TryEvaluate(column.Formula, variables, out var value, out var formulaError))
            { error = $"Column '{column.Key}': {formulaError}"; return false; }
            variables[column.Key] = value;
            values[column.Key] = value;
        }
        return true;
    }

    private static bool IsIdentifier(string key)
    {
        if (!(char.IsLetter(key[0]) || key[0] == '_')) return false;
        return key.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
    }
}
