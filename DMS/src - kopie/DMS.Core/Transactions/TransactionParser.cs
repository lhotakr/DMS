namespace DMS.Core.Transactions;

public static class TransactionParser
{
    public static TransactionCommand Parse(string? input)
    {
        var rawInput = input ?? string.Empty;
        var workInput = rawInput.Trim();
        var mode = "Current";

        if (workInput.StartsWith("/n", StringComparison.OrdinalIgnoreCase))
        {
            mode = "Replace";
            workInput = workInput[2..].Trim();
        }
        else if (workInput.StartsWith("/o", StringComparison.OrdinalIgnoreCase))
        {
            mode = "NewWindow";
            workInput = workInput[2..].Trim();
        }

        var parts = workInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var code = parts.Length > 0 ? parts[0].Trim().ToUpperInvariant() : string.Empty;
        var arguments = parts.Skip(1).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

        return new TransactionCommand
        {
            RawInput = rawInput,
            Mode = mode,
            Code = code,
            Parameter = arguments.FirstOrDefault(),
            Arguments = arguments
        };
    }
}
