using DMS.Desktop.Configuration.Modules;
using System;

namespace DMS.Desktop.Views.SystemModules;

public static class DmsModuleText
{
    public static string Name(
        DmsModuleDefinition module,
        Func<string, string> translate)
    {
        if (module is null)
        {
            return string.Empty;
        }

        var byCodeKey = $"Module.{module.Code}.Name";
        var byCode = translate(byCodeKey);

        if (!IsMissingTranslation(byCode, byCodeKey))
        {
            return byCode;
        }

        var byFallbackNameKey = $"Module.{module.Name}";
        var byFallbackName = translate(byFallbackNameKey);

        return IsMissingTranslation(byFallbackName, byFallbackNameKey)
            ? module.Name
            : byFallbackName;
    }

    public static string Description(
        DmsModuleDefinition module,
        Func<string, string> translate)
    {
        if (module is null)
        {
            return string.Empty;
        }

        var key = $"Module.{module.Code}.Description";
        var translated = translate(key);

        return IsMissingTranslation(translated, key)
            ? module.Description
            : translated;
    }

    public static string SearchText(
        DmsModuleDefinition module,
        Func<string, string> translate)
    {
        if (module is null)
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            module.Code,
            module.Name,
            module.Description,
            Name(module, translate),
            Description(module, translate),
            module.SortOrder.ToString(),
            module.State,
            module.IsActive ? "Active" : "Inactive");
    }

    private static bool IsMissingTranslation(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }
}
