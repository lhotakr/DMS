namespace DMS.Core.Transactions;

/// <summary>
/// Definice transakce načtená z konfigurace nebo později z databáze.
/// Obsahuje metadata transakce, ne její výkonnou logiku.
/// </summary>
public sealed class TransactionDefinition
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Module { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string HandlerKey { get; init; } = string.Empty;
    public bool RequiresArticleNumber { get; init; }
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Role, které smějí transakci spustit.
    /// Pokud je seznam prázdný, transakce je dostupná všem přihlášeným uživatelům.
    /// </summary>
    public List<string> Roles { get; init; } = new();
}