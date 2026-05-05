using System;
using System.Collections.Generic;
using System.Text;

namespace DMS.Core.Transactions;

/// <summary>
/// Převádí text z transakčního řádku na strukturovaný příkaz.
/// Parser neřeší oprávnění ani spouštění obrazovek, pouze rozpozná režim,
/// kód transakce a volitelný parametr.
/// </summary>
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

        var code = parts.Length > 0
            ? parts[0].Trim().ToUpperInvariant()
            : string.Empty;

        var parameter = parts.Length > 1
            ? parts[1].Trim()
            : null;

        return new TransactionCommand
        {
            RawInput = rawInput,
            Mode = mode,
            Code = code,
            Parameter = parameter
        };
    }
}