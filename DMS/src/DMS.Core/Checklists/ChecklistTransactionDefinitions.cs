using DMS.Core.Transactions;

namespace DMS.Core.Checklists;

public static class ChecklistTransactionDefinitions
{
    public static IReadOnlyList<TransactionDefinition> AddMissing(IEnumerable<TransactionDefinition> source)
    {
        var result = source.ToList();
        var existing = result.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Create())
        {
            if (!existing.Contains(item.Code)) result.Add(item);
        }

        return result;
    }

    private static IEnumerable<TransactionDefinition> Create()
    {
        yield return Create("CHLSET", "Nastavení checklistů", "Obecné katalogy a nastavení checklistového enginu.");
        yield return Create("CHL00", "Definice checklistů", "Správa a náhled definic checklistů.");
        yield return Create("CHL01", "Nový checklist", "Založení nového checklistu.");
        yield return Create("CHL02", "Změna checklistu", "Změna rozpracovaného checklistu.");
        yield return Create("CHL03", "Náhled checklistu", "Náhled existujícího checklistu.");
        yield return Create("CHL04", "Kopie checklistu", "Nové kolo nebo kopie checklistu.");
        yield return Create("CHL05", "Přehled checklistů", "Přehled checklistů podle typu.");
        yield return Create("CHL06", "Kontrola checklistu", "Kontrola a potvrzení checklistu.");
    }

    private static TransactionDefinition Create(string code, string name, string description) => new()
    {
        Code = code,
        Name = name,
        Module = "Checklisty",
        Description = description,
        HandlerKey = "ChecklistEngine",
        RequiresArticleNumber = false,
        IsActive = true,
        Roles = new List<string> { "DMS_ADMIN", "DMS_TECHNOLOGIE", "DMS_KVALITA" }
    };
}
