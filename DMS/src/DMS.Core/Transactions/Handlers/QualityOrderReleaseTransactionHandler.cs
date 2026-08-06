namespace DMS.Core.Transactions.Handlers;

public sealed class QualityOrderReleaseTransactionHandler : ITransactionHandler
{
    public string HandlerKey => "QualityOrderRelease";

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
                "Quality order release/block selection opened.");
        }

        return TransactionResult.Ok(
            definition.Code,
            parameter,
            $"Quality order release/block screen opened for {parameter}.");
    }
}
