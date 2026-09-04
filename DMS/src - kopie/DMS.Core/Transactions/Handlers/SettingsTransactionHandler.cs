namespace DMS.Core.Transactions.Handlers;

public sealed class SettingsTransactionHandler : ITransactionHandler
{
    private readonly Func<int> _maxHistoryProvider;

    public SettingsTransactionHandler(Func<int> maxHistoryProvider)
    {
        _maxHistoryProvider = maxHistoryProvider;
    }

    public string HandlerKey => "Settings";

    public TransactionResult Execute(TransactionCommand command, TransactionDefinition definition)
    {
        var message =
            "Nastavení klienta DMS:\n\n" +
            $"Maximální počet posledních transakcí: {_maxHistoryProvider()}\n" +
            "Konfigurace: lokální JSON / vývojový režim\n" +
            "Serverová konfigurace: čeká na přípravu\n" +
            "SSO: Windows login";

        return TransactionResult.Ok(
            definition.Code,
            (string?)null,
            message);
    }
}