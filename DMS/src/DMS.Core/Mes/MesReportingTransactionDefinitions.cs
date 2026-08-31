using DMS.Core.Transactions;

namespace DMS.Core.Mes;

public static class MesReportingTransactionDefinitions
{
    public static IReadOnlyList<TransactionDefinition> AddMissing(
        IEnumerable<TransactionDefinition> source)
    {
        var result =
            source?.ToList()
            ?? new List<TransactionDefinition>();

        AddIfMissing(
            result,
            new TransactionDefinition
            {
                Code = "MESSET",
                Name = "MES Database Settings",
                Module = "MES",
                Description = "Read-only FASTEC SQL database connection settings.",
                HandlerKey = "MesDatabaseSettings",
                RequiresArticleNumber = false,
                IsActive = true,
                Roles = new List<string>
                {
                    "DMS_ADMIN"
                }
            });

        AddIfMissing(
            result,
            new TransactionDefinition
            {
                Code = "MES06",
                Name = "MES Reporting",
                Module = "MES",
                Description = "Dynamic read-only production reporting from the FASTEC analytical database.",
                HandlerKey = "MesReporting",
                RequiresArticleNumber = false,
                IsActive = true,
                Roles = new List<string>
                {
                    "DMS_ADMIN",
                    "DMS_TECHNOLOGIE"
                }
            });

        return result;
    }

    private static void AddIfMissing(
        ICollection<TransactionDefinition> target,
        TransactionDefinition definition)
    {
        if (target.Any(item =>
                string.Equals(
                    item.Code,
                    definition.Code,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        target.Add(definition);
    }
}
