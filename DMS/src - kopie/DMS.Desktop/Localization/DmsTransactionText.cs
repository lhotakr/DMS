using DMS.Core.Transactions;

namespace DMS.Desktop.Localization;

public static class DmsTransactionText
{
    public static string AllModules(Func<string, string> translate)
    {
        const string key = "HELP.AllModules";
        var translated = translate(key);

        return IsMissingTranslation(translated, key)
            ? "All modules"
            : translated;
    }

    public static string Module(
        TransactionDefinition definition,
        Func<string, string> translate)
    {
        return Module(definition.Module, translate);
    }

    public static string Module(
        string? module,
        Func<string, string> translate)
    {
        if (string.IsNullOrWhiteSpace(module))
        {
            return string.Empty;
        }

        var key = $"Module.{module}";
        var translated = translate(key);

        return IsMissingTranslation(translated, key)
            ? module
            : translated;
    }

    public static string Name(
        TransactionDefinition definition,
        Func<string, string> translate)
    {
        var key = $"Transaction.{definition.Code}.Name";
        var translated = translate(key);

        return IsMissingTranslation(translated, key)
            ? definition.Name
            : translated;
    }

    public static string Description(
        TransactionDefinition definition,
        Func<string, string> translate)
    {
        var key = $"Transaction.{definition.Code}.Description";
        var translated = translate(key);

        return IsMissingTranslation(translated, key)
            ? definition.Description
            : translated;
    }

    public static string Parameter(
        TransactionDefinition definition,
        Func<string, string> translate)
    {
        if (!definition.RequiresArticleNumber)
        {
            const string noParameterKey = "HELP.NoParameter";
            var noParameter = translate(noParameterKey);

            return IsMissingTranslation(noParameter, noParameterKey)
                ? "No parameter"
                : noParameter;
        }

        const string articleKey = "HELP.RequiresArticleNumber";
        var articleText = translate(articleKey);

        return IsMissingTranslation(articleText, articleKey)
            ? "SAP article number"
            : articleText;
    }

    public static string Roles(
        TransactionDefinition definition,
        Func<string, string> translate)
    {
        if (definition.Roles.Count == 0)
        {
            const string allUsersKey = "HELP.AllUsers";
            var allUsers = translate(allUsersKey);

            return IsMissingTranslation(allUsers, allUsersKey)
                ? "All users"
                : allUsers;
        }

        return string.Join(", ", definition.Roles);
    }

    public static string SearchText(
        TransactionDefinition definition,
        Func<string, string> translate)
    {
        return string.Join(
            " ",
            definition.Code,
            Name(definition, translate),
            Module(definition, translate),
            Description(definition, translate),
            Parameter(definition, translate),
            Roles(definition, translate));
    }

    private static bool IsMissingTranslation(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }
}