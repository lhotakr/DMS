using DMS.Core.Transactions;

namespace DMS.Core.Administration;

public static class DmsThemeDesignerTransactionDefinitions
{
    public static IReadOnlyList<TransactionDefinition> AddMissing(
        IEnumerable<TransactionDefinition> source)
    {
        var result = source.ToList();

        if (result.Any(item =>
                string.Equals(
                    item.Code,
                    "SYS14",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return result;
        }

        result.Add(new TransactionDefinition
        {
            Code = "SYS14",
            Name = "Theme & UI Designer",
            Module = "ADMIN",
            Description = "Administrator editor for distributed DMS themes and UI overrides.",
            HandlerKey = "ThemeDesigner",
            RequiresArticleNumber = false,
            IsActive = true,
            Roles = new List<string> { "DMS_ADMIN" }
        });

        return result;
    }
}
