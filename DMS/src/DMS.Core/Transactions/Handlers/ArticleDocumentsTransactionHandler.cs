using DMS.Core.Articles;

namespace DMS.Core.Transactions.Handlers;

public sealed class ArticleDocumentsTransactionHandler : ITransactionHandler
{
    public string HandlerKey => "ArticleDocuments";

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
            $"Otevřena dokumentace artiklu {command.Parameter}.");
    }
}