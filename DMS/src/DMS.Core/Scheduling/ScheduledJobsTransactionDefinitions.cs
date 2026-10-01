using DMS.Core.Transactions;

namespace DMS.Core.Scheduling;

public static class ScheduledJobsTransactionDefinitions
{
    public static IReadOnlyList<TransactionDefinition> AddMissing(
        IEnumerable<TransactionDefinition> source)
    {
        var result =
            source?.ToList()
            ?? new List<TransactionDefinition>();

        if (result.Any(item =>
                string.Equals(
                    item.Code,
                    "JOB10",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return result;
        }

        result.Add(
            new TransactionDefinition
            {
                Code = "JOB10",
                Name = "Automatic jobs",
                Module = "System",
                Description = "Schedule automatic MES/SAP reports and deliveries.",
                HandlerKey = "JobScheduler",
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
}
