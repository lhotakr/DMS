using System.IO;
using System.Text.Json;

namespace DMS.Desktop.Configuration.Transactions;

public sealed class TransactionManagementService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _transactionsPath;

    public TransactionManagementService(string transactionsPath)
    {
        _transactionsPath = transactionsPath;
    }

    public List<TransactionEditorItem> LoadAll()
    {
        if (!File.Exists(_transactionsPath))
        {
            return new List<TransactionEditorItem>();
        }

        var json = File.ReadAllText(_transactionsPath);

        return JsonSerializer.Deserialize<List<TransactionEditorItem>>(json, JsonOptions)
               ?? new List<TransactionEditorItem>();
    }

    public void SaveAll(IEnumerable<TransactionEditorItem> transactions)
    {
        var normalized = transactions
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .Select(x => new TransactionEditorItem
            {
                Code = x.Code.Trim().ToUpperInvariant(),
                Name = x.Name.Trim(),
                Module = x.Module.Trim(),
                Description = x.Description.Trim(),
                HandlerKey = x.HandlerKey.Trim(),
                RequiresArticleNumber = x.RequiresArticleNumber,
                IsActive = x.IsActive,
                Roles = x.RolesText
                    .Split(new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(role => role.Trim())
                    .Where(role => !string.IsNullOrWhiteSpace(role))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(role => role)
                    .ToList()
            })
            .OrderBy(x => x.Module)
            .ThenBy(x => x.Code)
            .ToList();

        var directory = Path.GetDirectoryName(_transactionsPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(_transactionsPath, json);
    }
}