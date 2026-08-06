namespace DMS.Core.Transactions.Handlers;

public sealed class QualityOrderCreateTransactionHandler : ITransactionHandler
{
    public string HandlerKey => "QualityOrderCreate";

    public TransactionResult Execute(
        TransactionCommand command,
        TransactionDefinition definition)
    {
        return TransactionResult.Ok(
            definition.Code,
            command.Parameter,
            string.IsNullOrWhiteSpace(command.Parameter)
                ? "Quality order creation opened."
                : $"Quality order creation opened for {command.Parameter}.");
    }
}
