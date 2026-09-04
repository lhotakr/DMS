using DMS.Core.Transactions;

namespace DMS.Core.WorkLog;

public static class WorkLogTransactionDefinitions
{
    public static IReadOnlyList<TransactionDefinition> AddMissing(
        IReadOnlyList<TransactionDefinition> definitions)
    {
        var result = definitions.ToList();
        var knownCodes = result
            .Select(item => item.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in Create())
        {
            if (knownCodes.Add(definition.Code))
            {
                result.Add(definition);
            }
        }

        return result;
    }

    public static IReadOnlyList<TransactionDefinition> Create()
    {
        return new[]
        {
            CreateDefinition(
                "WORKLOG",
                "WorkLog",
                "Personal work-log dashboard and calendar."),
            CreateDefinition(
                "WLUSERS",
                "WorkLog users",
                "Manage WorkLog users, access level, employee type and delegation."),
            CreateDefinition(
                "WLWORK",
                "WorkLog activities",
                "Manage WorkLog projects and activity types."),
            CreateDefinition(
                "WLLOCK",
                "WorkLog locks",
                "Lock or unlock WorkLog dates for editing."),
            CreateDefinition(
                "WLCONFIG",
                "WorkLog configuration",
                "Configure the WorkLog database and future server-client tasks.")
        };
    }

    private static TransactionDefinition CreateDefinition(
        string code,
        string name,
        string description)
    {
        return new TransactionDefinition
        {
            Code = code,
            Name = name,
            Module = "WORKLOG",
            Description = description,
            HandlerKey = "WorkLog",
            RequiresArticleNumber = false,
            IsActive = true,
            Roles = new List<string>()
        };
    }
}
