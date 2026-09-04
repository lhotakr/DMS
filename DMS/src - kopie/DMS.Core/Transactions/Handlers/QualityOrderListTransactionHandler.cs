namespace DMS.Core.Transactions.Handlers;

public sealed class QualityOrderListTransactionHandler : ITransactionHandler
{
    public string HandlerKey => "QualityOrderList";

    public TransactionResult Execute(
        TransactionCommand command,
        TransactionDefinition definition)
    {
        return TransactionResult.Ok(
            definition.Code,
            command.Parameter,
            "Quality order overview opened.");
    }
}
