namespace DMS.Core.Recipes;

public sealed class RecipeNormalizationService
{
    public void NormalizeLayer(RecipeLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if (layer.ProcessOnly)
        {
            layer.SourceTotalGrams = 0m;
            layer.Components.Clear();
            return;
        }

        var sourceComponents = layer.Components
            .Where(component => !component.IsHardener)
            .ToList();

        var sourceTotal = sourceComponents.Sum(component => component.SourceGrams);
        layer.SourceTotalGrams = sourceTotal;

        if (sourceTotal <= 0m)
        {
            return;
        }

        foreach (var component in sourceComponents)
        {
            component.BomGrams =
                component.SourceGrams /
                sourceTotal *
                layer.BaseQuantityGrams;
        }
    }

    public RecipeHardenerRule? ApplyScreenPrintHardener(
        RecipeLayer layer,
        RecipeImportSettings settings,
        ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(warnings);

        // Re-processing the same layer must not duplicate a previously
        // generated hardener. Source hardener rows are retained.
        layer.Components.RemoveAll(component =>
            component.IsHardener &&
            component.SourceGrams <= 0m &&
            string.Equals(
                component.HardenerStatus,
                "AUTO ADDED",
                StringComparison.OrdinalIgnoreCase));

        foreach (var component in layer.Components)
        {
            component.IsHardener = false;
            component.HardenerStatus = string.Empty;
            component.GeneratedByRule = string.Empty;
        }

        var families = layer.Components
            .Select(component =>
                RecipeTextNormalizer.TrySplitScreenPrintCode(component.SourceText)?.Family)
            .Where(family => !string.IsNullOrWhiteSpace(family))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var matchingRules = settings.HardenerRules
            .Where(rule => rule.IsActive)
            .Where(rule =>
                families.Contains(
                    rule.Family,
                    StringComparer.OrdinalIgnoreCase))
            .ToList();

        // Mark hardener rows that are already physically present in the source
        // document. Comparison deliberately ignores spaces and punctuation:
        // "UV-HV 8", "UV HV 8" and "UVHV8" are equivalent here.
        foreach (var component in layer.Components)
        {
            var compactSource =
                RecipeTextNormalizer.NormalizeCompact(component.SourceText);

            var matchingHardenerRule = matchingRules.FirstOrDefault(rule =>
                string.Equals(
                    compactSource,
                    RecipeTextNormalizer.NormalizeCompact(rule.HardenerText),
                    StringComparison.OrdinalIgnoreCase));

            if (matchingHardenerRule is not null)
            {
                component.IsHardener = true;
            }
        }

        // Normalize the color recipe to 1 kg WITHOUT the hardener.
        NormalizeLayer(layer);

        var distinctRules = matchingRules
            .GroupBy(
                rule => $"{rule.RatioPercent:0.######}|{rule.HardenerText}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var sourceHardeners = layer.Components
            .Where(component => component.IsHardener)
            .ToList();

        if (distinctRules.Count == 0)
        {
            if (sourceHardeners.Count > 0)
            {
                ApplySourceHardenerQuantitiesWithoutRule(
                    layer,
                    sourceHardeners);

                warnings.Add(
                    "Hardener is present in the source recipe, but no active hardener rule matches the detected color family.");
            }

            return null;
        }

        if (distinctRules.Count > 1)
        {
            ApplySourceHardenerQuantitiesWithoutRule(
                layer,
                sourceHardeners);

            warnings.Add(
                "Multiple hardener families/rules detected: " +
                string.Join(
                    ", ",
                    matchingRules.Select(rule =>
                        $"{rule.Family}={rule.RatioPercent:0.###}%/{rule.HardenerText}")));

            foreach (var hardener in sourceHardeners)
            {
                hardener.HardenerStatus = "SOURCE / CHECK";
            }

            return null;
        }

        var selected = distinctRules[0];
        var expectedGrams =
            layer.BaseQuantityGrams *
            selected.RatioPercent /
            100m;

        if (sourceHardeners.Count > 0)
        {
            if (sourceHardeners.Count > 1)
            {
                warnings.Add(
                    $"Multiple source hardener rows detected for {selected.HardenerText}: {sourceHardeners.Count}.");
            }

            foreach (var hardener in sourceHardeners)
            {
                var actualPercent =
                    layer.SourceTotalGrams <= 0m
                        ? 0m
                        : hardener.SourceGrams /
                          layer.SourceTotalGrams *
                          100m;

                hardener.BomGrams =
                    layer.SourceTotalGrams <= 0m
                        ? 0m
                        : hardener.SourceGrams /
                          layer.SourceTotalGrams *
                          layer.BaseQuantityGrams;

                var difference =
                    Math.Abs(
                        actualPercent -
                        selected.RatioPercent);

                var ok =
                    difference <=
                    settings.HardenerTolerancePercent;

                hardener.HardenerStatus =
                    ok
                        ? "SOURCE OK"
                        : "SOURCE MISMATCH";

                hardener.GeneratedByRule =
                    $"{selected.Family} {selected.RatioPercent:0.###}% / source {actualPercent:0.###}%";

                if (!ok)
                {
                    warnings.Add(
                        $"Hardener {hardener.SourceText}: source ratio {actualPercent:0.###}% differs from rule " +
                        $"{selected.Family} {selected.RatioPercent:0.###}% by {difference:0.###} percentage point(s).");
                }
            }

            return selected;
        }

        if (!settings.AddMissingHardener)
        {
            warnings.Add(
                $"Hardener {selected.HardenerText} is missing for family {selected.Family}; " +
                $"expected {selected.RatioPercent:0.###}% ({expectedGrams:0.###} g per 1 kg base).");

            return selected;
        }

        layer.Components.Add(new RecipeComponent
        {
            SourceText = selected.HardenerText,
            SourceGrams = 0m,
            BomGrams = expectedGrams,
            IsHardener = true,
            HardenerStatus = "AUTO ADDED",
            GeneratedByRule =
                $"{selected.Family} {selected.RatioPercent:0.###}%"
        });

        return selected;
    }

    private static void ApplySourceHardenerQuantitiesWithoutRule(
        RecipeLayer layer,
        IReadOnlyCollection<RecipeComponent> hardeners)
    {
        foreach (var hardener in hardeners)
        {
            hardener.BomGrams =
                layer.SourceTotalGrams <= 0m
                    ? 0m
                    : hardener.SourceGrams /
                      layer.SourceTotalGrams *
                      layer.BaseQuantityGrams;

            hardener.HardenerStatus = "SOURCE / CHECK";
        }
    }
}
