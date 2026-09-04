using DMS.Core.Articles;

namespace DMS.Core.Transactions.Handlers;

public sealed class ArticleChangeTransactionHandler : ITransactionHandler
{
    public string HandlerKey => "ArticleChange";

    public TransactionResult Execute(TransactionCommand command, TransactionDefinition definition)
    {
        if (!ArticleNumberValidator.IsValid(command.Parameter))
        {
            return TransactionResult.Fail(
                definition.Code,
                $"{definition.Code} očekává desetimístné SAP číslo artiklu.");
        }

        return TransactionResult.Ok(
            definition.Code,
            command.Parameter,
            $"Otevřena změna artiklu {command.Parameter}.");
    }
}