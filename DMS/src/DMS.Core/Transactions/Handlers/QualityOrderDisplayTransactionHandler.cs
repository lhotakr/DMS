namespace DMS.Core.Transactions.Handlers;

public sealed class QualityOrderDisplayTransactionHandler : ITransactionHandler
{
    public string HandlerKey => "QualityOrderDisplay";

    public TransactionResult Execute(
        TransactionCommand command,
        TransactionDefinition definition)
    {
        var parameter = command.Parameter?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(parameter))
        {
            return TransactionResult.Ok(
                definition.Code,
                string.Empty,
                "Quality order selection opened.");
        }

        return TransactionResult.Ok(
            definition.Code,
            parameter,
            $"Quality order display opened for {parameter}.");
    }
}
