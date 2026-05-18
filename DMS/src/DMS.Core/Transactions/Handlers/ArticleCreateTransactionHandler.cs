namespace DMS.Core.Transactions.Handlers;

public sealed class ArticleCreateTransactionHandler : ITransactionHandler
{
    public string HandlerKey => "ArticleCreate";

    public TransactionResult Execute(TransactionCommand command, TransactionDefinition definition)
    {
        return TransactionResult.Ok(
            definition.Code,
            command.Parameter,
            "Otevřeno založení artiklu.");
    }
}