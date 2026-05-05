using System;
using System.Collections.Generic;
using System.Text;

namespace DMS.Core.Transactions;

/// <summary>
/// Reprezentuje jeden příkaz zadaný uživatelem v transakčním řádku DMS FiGUI.
/// Například: /nART03 1000018165.
/// </summary>
public sealed class TransactionCommand
{
    public string RawInput { get; init; } = string.Empty;
    public string Mode { get; init; } = "Current";
    public string Code { get; init; } = string.Empty;
    public string? Parameter { get; init; }
}