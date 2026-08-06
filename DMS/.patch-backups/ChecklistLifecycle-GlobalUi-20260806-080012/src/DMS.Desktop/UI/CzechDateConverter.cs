using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DMS.Desktop.UI;

public sealed class CzechDateConverter : IValueConverter
{
    private static readonly CultureInfo CzechCulture =
        CultureInfo.GetCultureInfo("cs-CZ");

    private static readonly string[] AcceptedFormats =
    {
        "dd.MM.yyyy",
        "d.M.yyyy",
        "dd.MM.yy",
        "d.M.yy"
    };

    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is DateTime date)
        {
            return date.ToString(
                "dd.MM.yyyy",
                CzechCulture);
        }

        return string.Empty;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var text = value?.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return null!;
        }

        if (DateTime.TryParseExact(
                text,
                AcceptedFormats,
                CzechCulture,
                DateTimeStyles.None,
                out var parsedDate))
        {
            return parsedDate.Date;
        }

        MessageBox.Show(
            "Datum zadej ve formátu DD.MM.RRRR.\n\n" +
            "Například: 06.01.2026",
            "Neplatný formát data",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return Binding.DoNothing;
    }
}