namespace DMS.Core.Transactions.Handlers;

/// <summary>
/// Opens the read-only MES data point monitor.
/// The optional parameter can be a station code, device name or IP address.
/// </summary>
public sealed class MesDataPointMonitorTransactionHandler : ITransactionHandler
{
    public string HandlerKey => "MesDataPointMonitor";

    public TransactionResult Execute(
        TransactionCommand command,
        TransactionDefinition definition)
    {
        return TransactionResult.Ok(
            definition.Code,
            command.Parameter,
            string.IsNullOrWhiteSpace(command.Parameter)
                ? "MES data point monitor opened."
                : $"MES data point monitor opened for {command.Parameter}.");
    }
}
