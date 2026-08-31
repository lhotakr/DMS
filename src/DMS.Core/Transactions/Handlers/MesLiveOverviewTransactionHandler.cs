namespace DMS.Core.Transactions.Handlers;

/// <summary>
/// Thin dispatcher handler for the read-only MES live overview.
/// The actual data access and UI stay in DMS.Integration.Mes and DMS.Desktop.
/// </summary>
public sealed class MesLiveOverviewTransactionHandler : ITransactionHandler
{
    public string HandlerKey => "MesLiveOverview";

    public TransactionResult Execute(TransactionCommand command, TransactionDefinition definition)
    {
        return TransactionResult.Ok(
            definition.Code,
            command.Parameter,
            "MES live machine overview opened.");
    }
}
