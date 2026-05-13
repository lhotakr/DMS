using System.Text.Json;

namespace DMS.Core.Transactions;

/// <summary>
/// Načítá definice transakcí z externího JSON souboru.
/// Později může být nahrazen databázovým repository.
/// </summary>
public sealed class TransactionDefinitionLoader
{
    public IReadOnlyList<TransactionDefinition> LoadFromJson(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"Soubor s definicí transakcí nebyl nalezen: {filePath}",
                filePath);
        }

        var json = File.ReadAllText(filePath);

        var definitions = JsonSerializer.Deserialize<List<TransactionDefinition>>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        return definitions?
            .Where(transaction => transaction.IsActive)
            .ToList()
            ?? new List<TransactionDefinition>();
    }
}