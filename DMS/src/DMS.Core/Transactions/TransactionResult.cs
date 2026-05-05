using System;
using System.Collections.Generic;
using System.Text;

namespace DMS.Core.Transactions;

/// <summary>
/// Výsledek zpracování transakce.
/// V první fázi vracíme jen informaci pro UI.
/// Později může obsahovat typ obrazovky, oprávnění, chyby a další metadata.
/// </summary>
public sealed class TransactionResult
{
    public bool Success { get; init; }
    public string TransactionCode { get; init; } = string.Empty;
    public string? Parameter { get; init; }
    public string Message { get; init; } = string.Empty;

    public static TransactionResult Ok(string transactionCode, string? parameter, string message)
    {
        return new TransactionResult
        {
            Success = true,
            TransactionCode = transactionCode,
            Parameter = parameter,
            Message = message
        };
    }

    public static TransactionResult Fail(string transactionCode, string message)
    {
        return new TransactionResult
        {
            Success = false,
            TransactionCode = transactionCode,
            Message = message
        };
    }
}