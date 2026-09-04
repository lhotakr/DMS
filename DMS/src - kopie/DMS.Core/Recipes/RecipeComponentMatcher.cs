using DMS.Core.Sap;

namespace DMS.Core.Recipes;

public sealed class RecipeComponentMatcher
{
    public IReadOnlyList<SapMaterial> FilterSapComponents(
        IEnumerable<SapMaterial> materials,
        RecipeImportSettings settings)
    {
        return materials
            .Where(material =>
                material.MaterialNumber.StartsWith(
                    settings.SapComponentPrefix,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(material => material.MaterialNumber)
            .ToList();
    }

    public void AutoMatch(
        RecipeImportResult result,
        IReadOnlyList<SapMaterial> allMaterials,
        RecipeImportSettings settings)
    {
        var materials = FilterSapComponents(allMaterials, settings);

        foreach (var component in result.AllComponents)
        {
            var candidates = GetCandidates(component, result.Kind, materials, settings, 3);

            if (candidates.Count == 0)
            {
                ClearMatch(component);
                continue;
            }

            var best = candidates[0];
            var second = candidates.Count > 1 ? candidates[1] : null;
            var gap = second is null ? 1d : best.Score - second.Score;

            var decisive =
                second is null ||
                string.Equals(best.Reason, "ALIAS", StringComparison.OrdinalIgnoreCase) ||
                gap >= settings.AutoMatchMinimumGap;

            if (best.Score >= settings.AutoMatchThreshold && decisive)
            {
                Assign(component, best.Material, best.Score, best.Reason);
            }
            else
            {
                ClearMatch(component);
                component.MatchScore = best.Score;
                component.MatchMethod = "REVIEW";
            }
        }
    }

    public IReadOnlyList<RecipeMaterialCandidate> GetCandidates(
        RecipeComponent component,
        RecipeImportKind kind,
        IReadOnlyList<SapMaterial> materials,
        RecipeImportSettings settings,
        int limit = 20,
        string? extraFilter = null)
    {
        ArgumentNullException.ThrowIfNull(component);

        var alias = settings.Aliases.FirstOrDefault(rule =>
            string.Equals(
                RecipeTextNormalizer.NormalizeTokenSignature(rule.SourceText),
                RecipeTextNormalizer.NormalizeTokenSignature(component.SourceText),
                StringComparison.OrdinalIgnoreCase));

        if (alias is not null)
        {
            var aliasMaterial = materials.FirstOrDefault(material =>
                string.Equals(material.MaterialNumber, alias.MaterialNumber, StringComparison.OrdinalIgnoreCase));

            if (aliasMaterial is not null)
            {
                return new[]
                {
                    new RecipeMaterialCandidate
                    {
                        Material = aliasMaterial,
                        Score = 1d,
                        Reason = "ALIAS"
                    }
                };
            }
        }

        var query = materials.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(extraFilter))
        {
            var filter = RecipeTextNormalizer.NormalizeText(extraFilter);
            query = query.Where(material =>
                RecipeTextNormalizer.NormalizeText(material.MaterialNumber).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                RecipeTextNormalizer.NormalizeText(material.Description).Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        return query
            .Select(material => Score(component.SourceText, material, kind))
            .Where(candidate => candidate.Score > 0d)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Material.MaterialNumber)
            .Take(limit)
            .ToList();
    }

    public void Assign(
        RecipeComponent component,
        SapMaterial material,
        double score = 1d,
        string method = "MANUAL")
    {
        component.SapMaterialNumber = material.MaterialNumber;
        component.SapDescription = material.Description;
        component.MatchScore = score;
        component.MatchMethod = method;
    }

    private static RecipeMaterialCandidate Score(
        string sourceText,
        SapMaterial material,
        RecipeImportKind kind)
    {
        if (kind == RecipeImportKind.ScreenPrinting)
        {
            var split = RecipeTextNormalizer.TrySplitScreenPrintCode(sourceText);

            if (split is not null)
            {
                var descriptionCompact = RecipeTextNormalizer.NormalizeCompact(material.Description);
                var sourceFamily = RecipeTextNormalizer.NormalizeCompact(split.Value.Family);
                var sourceKey = RecipeTextNormalizer.NormalizeCompact(split.Value.Key);

                if (descriptionCompact.Contains(sourceFamily, StringComparison.OrdinalIgnoreCase) &&
                    descriptionCompact.EndsWith(sourceKey, StringComparison.OrdinalIgnoreCase))
                {
                    return new RecipeMaterialCandidate
                    {
                        Material = material,
                        Score = 1d,
                        Reason = "SCREEN_FAMILY_SUFFIX"
                    };
                }

                if (descriptionCompact.Contains(sourceFamily, StringComparison.OrdinalIgnoreCase) &&
                    descriptionCompact.Contains(sourceKey, StringComparison.OrdinalIgnoreCase))
                {
                    return new RecipeMaterialCandidate
                    {
                        Material = material,
                        Score = 0.94d,
                        Reason = "SCREEN_FAMILY_CONTAINS"
                    };
                }
            }
        }

        var sourceSignature = RecipeTextNormalizer.NormalizeTokenSignature(sourceText);
        var targetSignature = RecipeTextNormalizer.NormalizeTokenSignature(material.Description);

        if (sourceSignature.Length > 0 &&
            string.Equals(sourceSignature, targetSignature, StringComparison.OrdinalIgnoreCase))
        {
            return new RecipeMaterialCandidate
            {
                Material = material,
                Score = 1d,
                Reason = "TOKENS_EXACT"
            };
        }

        var sourceTokens = RecipeTextNormalizer.Tokenize(sourceText).ToHashSet(StringComparer.Ordinal);
        var targetTokens = RecipeTextNormalizer.Tokenize(material.Description).ToHashSet(StringComparer.Ordinal);

        if (sourceTokens.Count == 0 || targetTokens.Count == 0)
        {
            return new RecipeMaterialCandidate { Material = material };
        }

        var intersection = sourceTokens.Intersect(targetTokens, StringComparer.Ordinal).Count();
        var union = sourceTokens.Union(targetTokens, StringComparer.Ordinal).Count();
        var jaccard = union == 0 ? 0d : (double)intersection / union;

        var sourceCompact = RecipeTextNormalizer.NormalizeCompact(sourceText);
        var targetCompact = RecipeTextNormalizer.NormalizeCompact(material.Description);
        var containsBonus =
            sourceCompact.Length >= 4 &&
            (targetCompact.Contains(sourceCompact, StringComparison.OrdinalIgnoreCase) ||
             sourceCompact.Contains(targetCompact, StringComparison.OrdinalIgnoreCase))
                ? 0.12d
                : 0d;

        var score = Math.Min(0.99d, jaccard + containsBonus);

        return new RecipeMaterialCandidate
        {
            Material = material,
            Score = score,
            Reason = "TOKEN_SIMILARITY"
        };
    }

    private static void ClearMatch(RecipeComponent component)
    {
        component.SapMaterialNumber = string.Empty;
        component.SapDescription = string.Empty;
        component.MatchScore = 0d;
        component.MatchMethod = string.Empty;
    }
}
