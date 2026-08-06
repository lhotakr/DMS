using DMS.Desktop.Configuration.Roles;
using System;

namespace DMS.Desktop.Localization;

public static class DmsRoleText
{
    public static string Name(
        DmsRoleDefinition role,
        Func<string, string> translate)
    {
        var key = $"Role.{role.Code}.Name";
        var translated = translate(key);

        return IsMissingTranslation(translated, key)
            ? role.Name
            : translated;
    }

    public static string Description(
        DmsRoleDefinition role,
        Func<string, string> translate)
    {
        var key = $"Role.{role.Code}.Description";
        var translated = translate(key);

        return IsMissingTranslation(translated, key)
            ? role.Description
            : translated;
    }

    private static bool IsMissingTranslation(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }
}
