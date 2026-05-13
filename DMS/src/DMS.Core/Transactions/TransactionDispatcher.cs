namespace DMS.Core.Transactions;

/// <summary>
/// Dispatcher transakcí.
/// Neobsahuje pevný switch/case seznam transakcí.
/// Transakce hledá podle definic a jejich HandlerKey.
/// </summary>
public sealed class TransactionDispatcher
{
    private readonly Dictionary<string, TransactionDefinition> _definitions;
    private readonly Dictionary<string, ITransactionHandler> _handlers;

    public TransactionDispatcher(
        IEnumerable<TransactionDefinition> definitions,
        IEnumerable<ITransactionHandler> handlers)
    {
        _definitions = definitions
            .ToDictionary(item => item.Code.ToUpperInvariant());

        _handlers = handlers
            .ToDictionary(item => item.HandlerKey, StringComparer.OrdinalIgnoreCase);
    }

    public TransactionResult Dispatch(TransactionCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Code))
        {
            return TransactionResult.Fail("", "Nebyla zadána žádná transakce.");
        }

        var code = command.Code.ToUpperInvariant();

        if (!_definitions.TryGetValue(code, out var definition))
        {
            return TransactionResult.Fail(code, $"Neznámá transakce: {code}");
        }

        if (!_handlers.TryGetValue(definition.HandlerKey, out var handler))
        {
            return TransactionResult.Fail(
                code,
                $"Transakce {code} nemá dostupný handler: {definition.HandlerKey}");
        }

        return handler.Execute(command, definition);
    }

    public TransactionDefinition? FindDefinition(string transactionCode)
    {
        if (string.IsNullOrWhiteSpace(transactionCode))
        {
            return null;
        }

        _definitions.TryGetValue(
            transactionCode.ToUpperInvariant(),
            out var definition);

        return definition;
    }

    public IReadOnlyList<TransactionDefinition> GetDefinitions()
    {
        return _definitions.Values
            .OrderBy(item => item.Module)
            .ThenBy(item => item.Code)
            .ToList();
    }
}