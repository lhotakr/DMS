using DMS.Desktop.Configuration.Transactions;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DMS.Desktop.Views.SystemTransactions;

public sealed class Sys11TransactionTextConverter : IValueConverter
{
    public Func<string, string>? Translate { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not TransactionEditorItem transaction)
        {
            return string.Empty;
        }

        var mode = parameter?.ToString() ?? string.Empty;

        return mode switch
        {
            "Name" => TranslateWithFallback($"Transaction.{transaction.Code}.Name", transaction.Name),
            "Module" => TranslateWithFallback($"Module.{transaction.Module}", transaction.Module),
            "Description" => TranslateWithFallback($"Transaction.{transaction.Code}.Description", transaction.Description),
            _ => string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }

    private string TranslateWithFallback(string key, string? fallback)
    {
        var translated = Translate?.Invoke(key);

        return IsMissingTranslation(translated, key)
            ? fallback ?? string.Empty
            : translated!;
    }

    private static bool IsMissingTranslation(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }
}
