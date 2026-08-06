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
                $"{definition.Code} expects a ten-digit SAP article number.");
        }

        return TransactionResult.Ok(
            definition.Code,
            command.Parameter,
            $"Article documentation opened for {command.Parameter}.");
    }
}
