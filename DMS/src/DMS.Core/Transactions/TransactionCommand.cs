namespace DMS.Core.Transactions;

/// <summary>
/// Parsed FiGUI transaction command. Arguments preserves all values following
/// the transaction code while Parameter remains backward compatible.
/// </summary>
public sealed class TransactionCommand
{
    public string RawInput { get; init; } = string.Empty;
    public string Mode { get; init; } = "Current";
    public string Code { get; init; } = string.Empty;
    public string? Parameter { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> GetArguments()
    {
        if (Arguments.Count > 0) return Arguments;
        return string.IsNullOrWhiteSpace(Parameter)
            ? Array.Empty<string>()
            : new[] { Parameter };
    }
}
