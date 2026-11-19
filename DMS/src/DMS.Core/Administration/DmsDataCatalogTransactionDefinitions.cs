using DMS.Core.Transactions;

namespace DMS.Core.Administration;

public static class DmsDataCatalogTransactionDefinitions
{
    public static IReadOnlyList<TransactionDefinition> AddMissing(IEnumerable<TransactionDefinition> source)
    {
        var list = source.ToList();
        if (!list.Any(x => x.Code.Equals("SYS15", StringComparison.OrdinalIgnoreCase)))
            list.Add(new TransactionDefinition
            {
                Code = "SYS15",
                Name = "Data sources and SQL templates",
                Description = "Administration of shared data sources and editable MES06 SQL queries.",
                Module = "ADMIN", HandlerKey = "DataSourceCatalog", IsActive = true,
                RequiresArticleNumber = false, Roles = new List<string> { "DMS_ADMIN" }
            });
        return list;
    }
}
