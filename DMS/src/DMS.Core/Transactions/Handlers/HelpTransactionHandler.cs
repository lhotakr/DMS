namespace DMS.Core.Transactions.Handlers;

public sealed class HelpTransactionHandler : ITransactionHandler
{
    private readonly Func<IReadOnlyList<TransactionDefinition>> _getDefinitions;

    public HelpTransactionHandler(
        Func<IReadOnlyList<TransactionDefinition>> getDefinitions)
    {
        _getDefinitions = getDefinitions;
    }

    public string HandlerKey => "Help";

    public TransactionResult Execute(
        TransactionCommand command,
        TransactionDefinition definition)
    {
        var definitions = _getDefinitions()
            .OrderBy(item => item.Module)
            .ThenBy(item => item.Code)
            .ToList();

        if (definitions.Count == 0)
        {
            return TransactionResult.Ok(
                definition.Code,
                command.Parameter,
                "Nejsou dostupné žádné transakce.");
        }

        var lines = new List<string>
        {
            "Dostupné transakce:",
            "",
            "Kód      Modul          Název                         Parametr",
            "---------------------------------------------------------------"
        };

        foreach (var item in definitions)
        {
            var parameter = item.RequiresArticleNumber
                ? "artikl"
                : "-";

            lines.Add(
                $"{item.Code,-8} {item.Module,-14} {item.Name,-28} {parameter}");
        }

        lines.Add("");
        lines.Add("Příklady:");
        lines.Add("ART03 1000015148");
        lines.Add("DOC03 1000015148");
        lines.Add("/oART03 1000015148");

        return TransactionResult.Ok(
            definition.Code,
            command.Parameter,
            string.Join(Environment.NewLine, lines));
    }
}