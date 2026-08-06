namespace DMS.Core.Domain.Units;

public sealed class UnitConversionService
{
    public decimal Convert(decimal value, UnitDefinition source, UnitDefinition target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (source.UnitDimensionId != target.UnitDimensionId)
        {
            throw new InvalidOperationException("Units belong to different dimensions.");
        }

        if (source.ScaleToBase == 0m || target.ScaleToBase == 0m)
        {
            throw new InvalidOperationException("Unit conversion scale cannot be zero.");
        }

        var baseValue = value * source.ScaleToBase + source.OffsetToBase;
        return (baseValue - target.OffsetToBase) / target.ScaleToBase;
    }
}
