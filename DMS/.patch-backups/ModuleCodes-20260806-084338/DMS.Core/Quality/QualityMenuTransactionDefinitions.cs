using DMS.Core.Transactions;

namespace DMS.Core.Quality;

public static class QualityMenuTransactionDefinitions
{
    public static IReadOnlyList<TransactionDefinition> AddMissing(IReadOnlyList<TransactionDefinition> source)
    {
        if (source.Any(x => string.Equals(x.Code, "QAMENU", StringComparison.OrdinalIgnoreCase)))
            return source;

        var result = source.ToList();
        result.Add(new TransactionDefinition
        {
            Code = "QAMENU",
            Name = "Hlavní menu kvality",
            Module = "Kvalita",
            Description = "Spouštěcí nabídka transakcí modulu kvality.",
            HandlerKey = "SimpleMessage",
            IsActive = true,
            Roles = new List<string>()
        });
        return result;
    }
}
