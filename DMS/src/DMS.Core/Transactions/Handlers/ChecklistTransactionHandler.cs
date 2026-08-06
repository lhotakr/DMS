using DMS.Core.Checklists;

namespace DMS.Core.Transactions.Handlers;

public sealed class ChecklistTransactionHandler : ITransactionHandler
{
    public string HandlerKey => "ChecklistEngine";

    public TransactionResult Execute(TransactionCommand command, TransactionDefinition definition)
    {
        var arguments = command.GetArguments();
        var message = ChecklistCommandDescription.Build(definition.Code, arguments);
        return TransactionResult.Ok(definition.Code, arguments, message);
    }
}

internal static class ChecklistCommandDescription
{
    public static string Build(string code, IReadOnlyList<string> args) => code.ToUpperInvariant() switch
    {
        "CHLSET" => args.Count == 0 ? "Obecné nastavení checklistů." : $"Nastavení katalogu {args[0]}.",
        "CHL00" => args.Count == 0 ? "Seznam definic checklistů." : $"Definice checklistu {args[0]}.",
        "CHL01" => args.Count switch
        {
            0 => "Výběr typu checklistu k založení.",
            1 => $"Výběr objektu pro checklist {args[0]}.",
            _ => $"Nový checklist {args[0]} pro objekt {args[1]}."
        },
        "CHL05" => args.Count == 0 ? "Přehled typů checklistů." : $"Přehled checklistů typu {args[0]}.",
        _ => args.Count == 0 ? "Výběr checklistu." : $"Checklist nebo objekt {args[0]}."
    };
}
