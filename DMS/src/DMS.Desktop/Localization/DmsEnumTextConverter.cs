using System.Globalization;
using System.Windows.Data;

namespace DMS.Desktop.Localization;

/// <summary>
/// Shared localized display converter for enum values shown in WPF controls.
/// Technical enum values remain unchanged in persisted data.
/// </summary>
public sealed class DmsEnumTextConverter : IValueConverter
{
    public static Func<string, string>? TranslationResolver { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var prefix = parameter?.ToString()?.Trim();
        var raw = value.ToString() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            var key = $"{prefix}.{raw}";
            var translated = TranslationResolver?.Invoke(key);
            if (!IsMissing(translated, key))
            {
                return translated!;
            }
        }

        return Humanize(raw);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static bool IsMissing(string? value, string key)
        => string.IsNullOrWhiteSpace(value)
           || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = new List<char>(value.Length + 8) { value[0] };
        for (var index = 1; index < value.Length; index++)
        {
            if (char.IsUpper(value[index]) && !char.IsUpper(value[index - 1]))
            {
                chars.Add(' ');
            }
            chars.Add(value[index]);
        }
        return new string(chars.ToArray());
    }
}
