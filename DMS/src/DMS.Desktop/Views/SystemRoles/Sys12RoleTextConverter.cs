using DMS.Desktop.Configuration.Roles;
using DMS.Desktop.Localization;
using System;
using System.Globalization;
using System.Windows.Data;

namespace DMS.Desktop.Views.SystemRoles;

public sealed class Sys12RoleTextConverter : IValueConverter
{
    public static Func<string, string>? Translate { get; set; }

    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not DmsRoleDefinition role)
        {
            return string.Empty;
        }

        var translate = Translate ?? (key => key);
        var mode = parameter?.ToString() ?? string.Empty;

        if (string.Equals(mode, "Description", StringComparison.OrdinalIgnoreCase))
        {
            return DmsRoleText.Description(role, translate);
        }

        return DmsRoleText.Name(role, translate);
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
