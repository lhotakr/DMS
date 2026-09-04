namespace DMS.Core.Transactions.Handlers;

public sealed class SimpleMessageTransactionHandler : ITransactionHandler
{
    private readonly string _handlerKey;
    private readonly string _title;

    public SimpleMessageTransactionHandler(string handlerKey, string title)
    {
        _handlerKey = handlerKey;
        _title = title;
    }

    public string HandlerKey => _handlerKey;

    public TransactionResult Execute(TransactionCommand command, TransactionDefinition definition)
    {
        return TransactionResult.Ok(
            definition.Code,
            command.Parameter,
            $"{_title}: {definition.Name}");
    }
}