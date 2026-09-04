using DMS.Core.Checklists;
using System.IO;

namespace DMS.Desktop.Services.Checklists;

public sealed class ChecklistInstanceRepository
{
    private readonly string _instancesRoot;

    public ChecklistInstanceRepository(string instancesRoot)
    {
        _instancesRoot = instancesRoot;
        Directory.CreateDirectory(_instancesRoot);
    }

    public IReadOnlyList<ChecklistInstance> LoadAll() => Directory
        .EnumerateFiles(_instancesRoot, "*.json", SearchOption.AllDirectories)
        .Where(path => !Path.GetFileName(path).Contains(".bak-", StringComparison.OrdinalIgnoreCase))
        .Select(AtomicChecklistJsonStore.Load<ChecklistInstance>)
        .Where(x => x is not null)
        .Cast<ChecklistInstance>()
        .OrderByDescending(x => x.ModifiedAt)
        .ToList();

    public ChecklistInstance? Find(string checklistNumber) => LoadAll().FirstOrDefault(x =>
        string.Equals(x.ChecklistNumber, checklistNumber, StringComparison.OrdinalIgnoreCase));

    public void Save(ChecklistInstance instance)
    {
        if (string.IsNullOrWhiteSpace(instance.ChecklistNumber)) instance.ChecklistNumber = GenerateNumber(instance.NumberPrefix, instance.DefinitionCode);
        instance.ModifiedAt = DateTimeOffset.Now;
        var yearRoot = Path.Combine(_instancesRoot, instance.CreatedAt.Year.ToString());
        AtomicChecklistJsonStore.Save(Path.Combine(yearRoot, instance.ChecklistNumber + ".json"), instance);
    }

    private string GenerateNumber(string? numberPrefix, string definitionCode)
    {
        var year = DateTime.Today.Year;
        var safePrefix = SanitizePrefix(numberPrefix, definitionCode);
        var prefix = $"{safePrefix}-{year}-";

        var max = LoadAll()
            .Select(x => x.ChecklistNumber)
            .Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(x => int.TryParse(x[prefix.Length..], out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{max + 1:000000}";
    }

    private static string SanitizePrefix(string? numberPrefix, string definitionCode)
    {
        var value = string.IsNullOrWhiteSpace(numberPrefix)
            ? definitionCode
            : numberPrefix.Trim();

        var sanitized = new string(value
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')
            .ToArray());

        if (string.IsNullOrWhiteSpace(sanitized))
            throw new InvalidOperationException("Checklist number prefix is empty or invalid.");

        return sanitized;
    }
}
