using DMS.Core.Transactions;

namespace DMS.Core.Recipes;

public static class RecipeImportTransactionDefinitions
{
    public static IReadOnlyList<TransactionDefinition> AddMissing(
        IEnumerable<TransactionDefinition> source)
    {
        var result = source.ToList();

        if (result.Any(item =>
                string.Equals(item.Code, "REC04", StringComparison.OrdinalIgnoreCase)))
        {
            return result;
        }

        result.Add(new TransactionDefinition
        {
            Code = "REC04",
            Name = "Import receptur",
            Module = "SAP",
            Description = "Import and normalization of spray-coating and screen-printing recipes.",
            HandlerKey = "RecipeImport",
            RequiresArticleNumber = false,
            IsActive = true,
            Roles = new List<string>()
        });

        return result;
    }
}
