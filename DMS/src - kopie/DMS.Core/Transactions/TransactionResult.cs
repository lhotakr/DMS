namespace DMS.Core.Transactions;

public sealed class TransactionResult
{
    public bool Success { get; init; }
    public string TransactionCode { get; init; } = string.Empty;
    public string? Parameter { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string Message { get; init; } = string.Empty;

    public static TransactionResult Ok(string transactionCode, string? parameter, string message)
    {
        var arguments = string.IsNullOrWhiteSpace(parameter) ? Array.Empty<string>() : new[] { parameter };
        return Ok(transactionCode, arguments, message);
    }

    public static TransactionResult Ok(string transactionCode, IReadOnlyList<string> arguments, string message)
    {
        return new TransactionResult
        {
            Success = true,
            TransactionCode = transactionCode,
            Parameter = arguments.FirstOrDefault(),
            Arguments = arguments.ToArray(),
            Message = message
        };
    }

    public static TransactionResult Fail(string transactionCode, string message) => new()
    {
        Success = false,
        TransactionCode = transactionCode,
        Message = message
    };
}
