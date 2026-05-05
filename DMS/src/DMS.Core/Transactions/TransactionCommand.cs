using System;
using System.Collections.Generic;
using System.Text;

namespace DMS.Core.Transactions;

/// <summary>
/// Reprezentuje jeden příkaz zadaný uživatelem do transakčního řádku DMS FiGUI.
/// Například: /nART03 1000018165.
/// </summary>
public sealed class TransactionCommand
{
    public string RawInput { get; init; } = string.Empty;

    /// <summary>
    /// Režim otevření transakce.
    /// Current = aktuální pracovní plocha
    /// Replace = nahradit aktuální pohled
    /// NewWindow = nové okno / záložka
    /// </summary>
    public string Mode { get; init; } = "Current";

    /// <summary>
    /// Kód transakce, například ART03, DOC03, SCR10.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Volitelný parametr transakce, například SAP číslo artiklu.
    /// </summary>
    public string? Parameter { get; init; }
}