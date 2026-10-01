namespace DMS.Core.Transactions.Handlers;

public sealed class QualityOrderEditTransactionHandler : ITransactionHandler
{
    public string HandlerKey => "QualityOrderEdit";

    public TransactionResult Execute(
        TransactionCommand command,
        TransactionDefinition definition)
    {
        // QO02 without an order number intentionally opens QO05 as a picker.
        // MainWindow.RenderQualityOrderEdit handles the empty parameter.
        if (string.IsNullOrWhiteSpace(command.Parameter))
        {
            return TransactionResult.Ok(
                definition.Code,
                string.Empty,
                "Select a quality order in QO05.");
        }

        return TransactionResult.Ok(
            definition.Code,
            command.Parameter.Trim(),
            $"Quality order edit opened for {command.Parameter.Trim()}.");
    }
}
