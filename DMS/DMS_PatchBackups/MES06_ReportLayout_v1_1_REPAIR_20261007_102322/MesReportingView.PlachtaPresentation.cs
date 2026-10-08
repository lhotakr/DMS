using DMS.Integration.Mes.Reporting.Definitions;
using System.Globalization;
using System.Windows.Data;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    /// <summary>
    /// Presentation-only converter used by the Plachta "Skrýt nuly" option.
    /// The source data remain unchanged; only numeric zero values are rendered blank.
    /// </summary>
    private sealed class PlachtaHideZeroConverter
        : IValueConverter
    {
        public static readonly PlachtaHideZeroConverter Instance =
            new();

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            IsNumericZero(value)
                ? null
                : value;

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            Binding.DoNothing;
    }

    private static bool IsNumericZero(
        object? value) =>
        value switch
        {
            byte v => v == 0,
            sbyte v => v == 0,
            short v => v == 0,
            ushort v => v == 0,
            int v => v == 0,
            uint v => v == 0,
            long v => v == 0,
            ulong v => v == 0,
            float v => v == 0f,
            double v => v == 0d,
            decimal v => v == 0m,
            _ => false
        };

    private bool ShouldHidePlachtaZeroValue(
        MesReportDefinition definition,
        object? value) =>
        IsPlachtaReport(definition)
        && ChkHideZeros.IsChecked == true
        && IsNumericZero(value);

    private bool ShouldHideCurrentPlachtaZeroValue(
        object? value) =>
        CmbReport.SelectedItem
            is MesReportDefinition definition
        && ShouldHidePlachtaZeroValue(
            definition,
            value);
}
