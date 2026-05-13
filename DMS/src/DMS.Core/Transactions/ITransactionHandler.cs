namespace DMS.Core.Transactions;

/// <summary>
/// Výkonná logika jedné transakce.
/// Každý handler ví, jak zpracovat konkrétní typ transakce.
/// </summary>
public interface ITransactionHandler
{
    string HandlerKey { get; }

    TransactionResult Execute(TransactionCommand command, TransactionDefinition definition);
}