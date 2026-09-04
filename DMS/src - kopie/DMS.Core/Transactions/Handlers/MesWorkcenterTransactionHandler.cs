using System;

namespace DMS.Core.Transactions.Handlers;

public sealed class MesWorkcenterTransactionHandler : ITransactionHandler
{
    private readonly Func<string, bool>? _workcenterExists;

    public MesWorkcenterTransactionHandler()
    {
    }

    public MesWorkcenterTransactionHandler(Func<string, bool> workcenterExists)
    {
        _workcenterExists = workcenterExists
            ?? throw new ArgumentNullException(nameof(workcenterExists));
    }

    public string HandlerKey => "MesWorkcenter";

    public TransactionResult Execute(
        TransactionCommand command,
        TransactionDefinition definition)
    {
        var workcenterCode = command.Parameter?
            .Trim()
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(workcenterCode))
        {
            return TransactionResult.Fail(
                definition.Code,
                "MESWC vyžaduje parametr pracoviště. Příklad: MESWC K14-1");
        }

        if (_workcenterExists is not null &&
            !_workcenterExists(workcenterCode))
        {
            return TransactionResult.Fail(
                definition.Code,
                $"Neplatné označení pracoviště {workcenterCode}");
        }

        return TransactionResult.Ok(
            definition.Code,
            workcenterCode,
            $"MES work-center dashboard opened for {workcenterCode}.");
    }
}
