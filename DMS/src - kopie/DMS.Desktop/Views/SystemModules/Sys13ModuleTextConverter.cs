using DMS.Desktop.Configuration.Modules;
using System;
using System.Globalization;
using System.Windows.Data;

namespace DMS.Desktop.Views.SystemModules;

public sealed class Sys13ModuleTextConverter : IValueConverter
{
    public static Func<string, string>? Translate { get; set; }

    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not DmsModuleDefinition module)
        {
            return string.Empty;
        }

        var translate = Translate ?? (key => key);
        var mode = parameter?.ToString() ?? string.Empty;

        if (string.Equals(mode, "Description", StringComparison.OrdinalIgnoreCase))
        {
            return DmsModuleText.Description(module, translate);
        }

        return DmsModuleText.Name(module, translate);
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
