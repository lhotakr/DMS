using DMS.Core.Transactions;

namespace DMS.Core.Framework;

public static class FrameworkTransactionDefinitions
{
    public static IReadOnlyList<TransactionDefinition> AddMissing(IEnumerable<TransactionDefinition> source)
    {
        var result = source.ToList();
        var existing = result.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in Create())
        {
            if (!existing.Contains(definition.Code))
            {
                result.Add(definition);
            }
        }

        return result;
    }

    private static IEnumerable<TransactionDefinition> Create()
    {
        yield return Create("FW01", "Localization framework", "Localization dictionaries, missing keys and text consistency.");
        yield return Create("FW02", "UI framework", "Shared dialogs, DataGrid styles and UI consistency.");
        yield return Create("FW03", "Runtime configuration", "Runtime modules, transactions and client/system configuration.");
        yield return Create("FW04", "System diagnostics", "Pre-production validation of DMS configuration and data paths.", "FrameworkDiagnostics");
        yield return Create("FW05", "Audit and logging", "Application logs, audit actions and emergency diagnostics.");
        yield return Create("FW06", "Security framework", "Users, roles, permissions and approval policies.");
        yield return Create("FW07", "Workflow framework", "Workflow states, approvals and lifecycle administration.");
        yield return Create("FW08", "Performance monitor", "Runtime performance, transaction timing, memory and JSON probes.");
        yield return Create("FW09", "Core master data", "People, organization units, units and shared entities.");
        yield return Create("FW11", "Release health", "Final production readiness, release quality index and build gate.");
    }

    private static TransactionDefinition Create(
        string code,
        string name,
        string description,
        string handlerKey = "FrameworkHub") => new()
    {
        Code = code,
        Name = name,
        Module = "ADMIN",
        Description = description,
        HandlerKey = handlerKey,
        RequiresArticleNumber = false,
        IsActive = true,
        Roles = new List<string> { "DMS_ADMIN" }
    };
}
