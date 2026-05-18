namespace DMS.Core.Sap;

public sealed class SapMaterialClassifier
{
    private readonly IReadOnlyList<SapMaterialNumberRange> _ranges;

    public SapMaterialClassifier(IReadOnlyList<SapMaterialNumberRange> ranges)
    {
        _ranges = ranges;
    }

    public SapMaterialNumberRange? Classify(string? materialNumber)
    {
        if (!long.TryParse(materialNumber, out var number))
        {
            return null;
        }

        foreach (var range in _ranges)
        {
            if (!long.TryParse(range.From, out var from))
            {
                continue;
            }

            if (!long.TryParse(range.To, out var to))
            {
                continue;
            }

            if (number >= from && number <= to)
            {
                return range;
            }
        }

        return null;
    }
}