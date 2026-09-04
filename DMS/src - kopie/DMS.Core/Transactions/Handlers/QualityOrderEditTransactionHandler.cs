namespace DMS.Core.Transactions.Handlers;

public sealed class QualityOrderEditTransactionHandler : ITransactionHandler
{
    public string HandlerKey => "QualityOrderEdit";

    public TransactionResult Execute(
        TransactionCommand command,
        TransactionDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(command.Parameter))
        {
            return TransactionResult.Fail(
                definition.Code,
                "QO02 expects an order number.");
        }

        return TransactionResult.Ok(
            definition.Code,
            command.Parameter.Trim(),
            $"Quality order edit opened for {command.Parameter.Trim()}.");
    }
}
