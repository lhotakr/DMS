using DMS.Core.Security;

namespace DMS.Core.Transactions.Handlers;

public sealed class SystemInfoTransactionHandler : ITransactionHandler
{
    private readonly Func<DmsUserContext> _currentUserProvider;

    public SystemInfoTransactionHandler(Func<DmsUserContext> currentUserProvider)
    {
        _currentUserProvider = currentUserProvider;
    }

    public string HandlerKey => "SystemInfo";

    public TransactionResult Execute(TransactionCommand command, TransactionDefinition definition)
    {
        var user = _currentUserProvider();

        var message =
            $"Windows login: {user.WindowsLogin}\n" +
            $"DMS uživatel: {user.DisplayName}\n" +
            $"Role: {string.Join(", ", user.Roles)}";

        return TransactionResult.Ok(
            definition.Code,
            (string?)null,
            message);
    }
}