using DMS.Core.Articles;

namespace DMS.Core.Transactions.Handlers;

public sealed class ArticleDocumentEditTransactionHandler : ITransactionHandler
{
    public string HandlerKey => "ArticleDocumentEdit";

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
            $"Article documentation editor opened for {command.Parameter}.");
    }
}
