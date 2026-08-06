using DMS.Core.Transactions;

namespace DMS.Core.Quality;

public static class QualityMenuTransactionDefinitions
{
    public static IReadOnlyList<TransactionDefinition> AddMissing(IReadOnlyList<TransactionDefinition> source)
    {
        var result = source.ToList();
        var existing = result.FirstOrDefault(x =>
            string.Equals(x.Code, "QAMENU", StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            return result;
        }

        result.Add(new TransactionDefinition
        {
            Code = "QAMENU",
            Name = "Hlavní menu kvality",
            Module = "QUALITY",
            Description = "Spouštěcí nabídka transakcí modulu kvality.",
            HandlerKey = "SimpleMessage",
            IsActive = true,
            Roles = new List<string>()
        });

        return result;
    }
}
