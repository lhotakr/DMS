using DMS.Core.Sap.Validation;

namespace DMS.Core.Sap;

public sealed class SapTechnicalArticleSummaryService
{
    private readonly IReadOnlyList<SapMaterial> _materials;
    private readonly IReadOnlyList<SapBom> _boms;
    private readonly IReadOnlyList<SapRouting> _routings;
    private readonly IReadOnlyList<SapWorkCenter> _workCenters;
    private readonly SapValidationRuleSet? _validationRuleSet;
    private readonly SapSimpleValidationEngine _validationEngine = new();

    public SapTechnicalArticleSummaryService(
    IReadOnlyList<SapMaterial> materials,
    IReadOnlyList<SapBom> boms,
    IReadOnlyList<SapRouting> routings,
    IReadOnlyList<SapWorkCenter> workCenters,
    SapValidationRuleSet? validationRuleSet = null)
    {
        _materials = materials;
        _boms = boms;
        _routings = routings;
        _workCenters = workCenters;
        _validationRuleSet = validationRuleSet;
    }

    public SapTechnicalArticleSummary Build(string articleNumber)
    {
        var normalizedArticleNumber = NormalizeMaterialNumber(articleNumber);

        var summary = new SapTechnicalArticleSummary
        {
            ArticleNumber = normalizedArticleNumber,
            Material = _materials.FirstOrDefault(item =>
                IsSameMaterial(item.MaterialNumber, normalizedArticleNumber))
        };

        summary.Boms9200 = _boms
            .Where(item => IsSameMaterial(item.MaterialNumber, normalizedArticleNumber))
            .Where(item => item.Plant == "9200")
            .ToList();

        summary.Boms2000 = _boms
            .Where(item => IsSameMaterial(item.MaterialNumber, normalizedArticleNumber))
            .Where(item => item.Plant == "2000")
            .ToList();

        summary.Routings9200 = _routings
            .Where(item => IsSameMaterial(item.MaterialNumber, normalizedArticleNumber))
            .Where(item => item.Plant == "9200")
            .ToList();

        summary.Routings2000 = _routings
            .Where(item => IsSameMaterial(item.MaterialNumber, normalizedArticleNumber))
            .Where(item => item.Plant == "2000")
            .ToList();

        EnrichBoms(summary);
        EnrichRoutings(summary);
        BuildVariants(summary);
        Validate(summary);

        return summary;
    }

    public IReadOnlyList<SapTechnicalBomItemRow> BuildBomRows(
        IReadOnlyList<SapBom> boms,
        string plant)
    {
        return boms
            .SelectMany(bom => bom.Items
                .OrderBy(item => item.Position)
                .Select(item => CreateBomRow(plant, item)))
            .ToList();
    }

    public IReadOnlyList<SapTechnicalRoutingOperationRow> BuildRoutingRows(
        IReadOnlyList<SapRouting> routings,
        string plant)
    {
        return routings
            .SelectMany(routing => routing.Operations
                .OrderBy(operation => operation.OperationNumber)
                .Select(operation => CreateRoutingRow(plant, operation)))
            .ToList();
    }

    private static void BuildVariants(SapTechnicalArticleSummary summary)
    {
        var plants = summary.Boms9200
            .Select(item => item.Plant)
            .Concat(summary.Boms2000.Select(item => item.Plant))
            .Concat(summary.Routings9200.Select(item => item.Plant))
            .Concat(summary.Routings2000.Select(item => item.Plant))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item)
            .ToList();

        foreach (var plant in plants)
        {
            var boms = plant == "9200"
                ? summary.Boms9200
                : summary.Boms2000;

            var routings = plant == "9200"
                ? summary.Routings9200
                : summary.Routings2000;

            var alternatives = boms
                .Select(item => NormalizeAlternative(item.Alternative))
                .Concat(routings.Select(item => NormalizeAlternative(item.Alternative)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item)
                .ToList();

            foreach (var alternative in alternatives)
            {
                summary.Variants.Add(new SapTechnicalVariantSummary
                {
                    Plant = plant,
                    Alternative = alternative,
                    Boms = boms
                        .Where(item => NormalizeAlternative(item.Alternative) == alternative)
                        .ToList(),
                    Routings = routings
                        .Where(item => NormalizeAlternative(item.Alternative) == alternative)
                        .ToList()
                });
            }
        }
    }

    private static string NormalizeAlternative(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        return int.TryParse(text, out var number)
            ? number.ToString("00")
            : text;
    }
    private void EnrichBoms(SapTechnicalArticleSummary summary)
    {
        foreach (var bom in summary.Boms9200.Concat(summary.Boms2000))
        {
            foreach (var item in bom.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.ComponentDescription))
                {
                    continue;
                }

                var material = _materials.FirstOrDefault(material =>
                    IsSameMaterial(material.MaterialNumber, item.ComponentNumber));

                item.ComponentDescription =
                    material?.Description
                    ?? item.ItemText
                    ?? string.Empty;
            }
        }
    }

    private void EnrichRoutings(SapTechnicalArticleSummary summary)
    {
        foreach (var routing in summary.Routings9200.Concat(summary.Routings2000))
        {
            foreach (var operation in routing.Operations)
            {
                if (!string.IsNullOrWhiteSpace(operation.WorkCenterText))
                {
                    continue;
                }

                var workCenter = _workCenters.FirstOrDefault(item =>
                    string.Equals(
                        item.ObjectId,
                        operation.WorkCenterObjectId,
                        StringComparison.OrdinalIgnoreCase));

                if (workCenter is null)
                {
                    continue;
                }

                operation.WorkCenter = workCenter.WorkCenter;
                operation.WorkCenterText = workCenter.DisplayText;
            }
        }
    }

    private static SapTechnicalRoutingOperationRow CreateRoutingRow(
        string plant,
        SapRoutingOperation operation)
    {
        var workCenterDisplay = string.IsNullOrWhiteSpace(operation.WorkCenterText)
            ? operation.WorkCenter
            : $"{operation.WorkCenterText} ({operation.WorkCenter})";

        var shiftTakt = CalculateShiftTakt(operation.BaseQuantity);

        if (plant == "9200")
        {
            return new SapTechnicalRoutingOperationRow
            {
                Plant = plant,
                OperationNumber = operation.OperationNumber,
                WorkCenterDisplay = workCenterDisplay,
                Description = operation.Description,
                ShiftTakt = FormatDecimal(shiftTakt),
                InfoRecord = operation.ControlKey.Equals("ZPP5", StringComparison.OrdinalIgnoreCase)
                    ? operation.InfoRecord
                    : string.Empty,
                HasWarning = operation.ControlKey.Equals("ZPP5", StringComparison.OrdinalIgnoreCase)
                             && string.IsNullOrWhiteSpace(operation.InfoRecord)
            };
        }

        return new SapTechnicalRoutingOperationRow
        {
            Plant = plant,
            OperationNumber = operation.OperationNumber,
            WorkCenterDisplay = workCenterDisplay,
            Description = operation.Description,
            ScrapPercent = FormatDecimal(operation.ScrapPercent),
            SetupTime = FormatDecimal(operation.SetupTime),
            ShiftTakt = FormatDecimal(shiftTakt),
            PersonnelCount = FormatDecimal(operation.Vgw04),
            HasWarning = false
        };
    }

    private static SapTechnicalBomItemRow CreateBomRow(
        string plant,
        SapBomItem item)
    {
        return new SapTechnicalBomItemRow
        {
            Plant = plant,
            Position = item.Position,
            ItemCategory = item.ItemCategory,
            ComponentDescription = item.ComponentDescription,
            ComponentNumber = item.ComponentNumber,
            Quantity = FormatDecimal(item.Quantity),
            ScrapPercent = plant == "9200"
                ? FormatDecimal(item.ScrapPercent)
                : string.Empty,
            Unit = item.Unit,
            IsFixedQuantity = item.IsFixedQuantity ? "Ano" : "",
            HasWarning = item.Quantity is null
        };
    }

    private void Validate(SapTechnicalArticleSummary summary)
    {
        ValidateByJsonRules(summary);
    }

    private void ValidateByJsonRules(SapTechnicalArticleSummary summary)
    {
        if (_validationRuleSet is null || _validationRuleSet.Rules.Count == 0)
        {
            return;
        }

        ValidateArticleSummaryByJsonRules(summary);
        ValidateBomHeadersByJsonRules(summary);
        ValidateBomItemsByJsonRules(summary);
        ValidateCrossPlantByJsonRules(summary);
        ValidateDecorationByJsonRules(summary);
    }

    private void ValidateArticleSummaryByJsonRules(SapTechnicalArticleSummary summary)
    {
        var context = new SapArticleSummaryValidationContext
        {
            ArticleNumber = summary.ArticleNumber,
            MaterialFound = summary.Material is not null,
            Bom9200Count = summary.Boms9200.Count,
            Bom2000Count = summary.Boms2000.Count,
            Routing9200Count = summary.Routings9200.Count,
            Routing2000Count = summary.Routings2000.Count
        };

        AddFindingsToSummary(
            summary,
            _validationEngine.Validate(
                _validationRuleSet!.Rules,
                "ARTICLE_SUMMARY",
                context));
    }

    private static bool IsTextItem(SapBomItem item)
    {
        return item.ItemCategory.Equals("T", StringComparison.OrdinalIgnoreCase)
               || string.IsNullOrWhiteSpace(item.ComponentNumber);
    }

    private static string? TryExtractDecorationToken(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var parts = description
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Příklad:
        // 14543 100 50 RD P.Rabanne C.Mont.
        // Token dekorace je často čtvrtý výraz.
        if (parts.Length < 4)
        {
            return null;
        }

        var token = parts[3].Trim().ToUpperInvariant();

        if (token.Length is < 1 or > 6)
        {
            return null;
        }

        if (!token.All(char.IsLetter))
        {
            return null;
        }

        return token;
    }

    private static string GetDecorationDifference(
        string articleDecorationCode,
        string componentDecorationCode)
    {
        if (string.IsNullOrWhiteSpace(articleDecorationCode) ||
            string.IsNullOrWhiteSpace(componentDecorationCode))
        {
            return string.Empty;
        }

        var article = articleDecorationCode.ToUpperInvariant();
        var component = componentDecorationCode.ToUpperInvariant();

        var missingFromComponent = article
            .Where(letter => !component.Contains(letter))
            .Distinct()
            .ToArray();

        return new string(missingFromComponent);
    }

    private void ValidateCrossPlantByJsonRules(SapTechnicalArticleSummary summary)
    {
        var lastZpp2Scrap = summary.Routings2000
            .SelectMany(routing => routing.Operations)
            .Where(operation => operation.ControlKey.Equals("ZPP2", StringComparison.OrdinalIgnoreCase))
            .OrderBy(operation => operation.OperationNumber)
            .LastOrDefault()
            ?.ScrapPercent;

        foreach (var bom in summary.Boms9200)
        {
            foreach (var item in bom.Items)
            {
                if (IsTextItem(item))
                {
                    continue;
                }

                var context = new SapCrossPlantValidationContext
                {
                    ArticleNumber = summary.ArticleNumber,
                    LastZpp2Scrap2000 = lastZpp2Scrap,
                    BomNumber9200 = bom.BomNumber,
                    Position9200 = item.Position,
                    ComponentNumber9200 = item.ComponentNumber,
                    ComponentScrap9200 = item.ScrapPercent,
                    BomAlternative9200 = bom.Alternative,
                    IsSortingAlternative9200 = IsSortingAlternative(bom.Plant, bom.Alternative)
                };

                AddFindingsToSummary(
                    summary,
                    _validationEngine.Validate(
                        _validationRuleSet!.Rules,
                        "CROSS_PLANT",
                        context));
            }
        }
    }

    private void ValidateDecorationByJsonRules(SapTechnicalArticleSummary summary)
    {
        var articleDescription = summary.Material?.Description ?? string.Empty;
        var articleDecorationCode = TryExtractDecorationToken(articleDescription);

        if (string.IsNullOrWhiteSpace(articleDecorationCode))
        {
            return;
        }

        var routing2000TechnologyCodes = ExtractRouting2000TechnologyCodes(summary);
        var routing2000TechnologiesText = string.Join(", ", routing2000TechnologyCodes.OrderBy(item => item));

        foreach (var bom in summary.Boms9200)
        {
            foreach (var item in bom.Items)
            {
                var componentDecorationCode = TryExtractDecorationToken(item.ComponentDescription);

                if (string.IsNullOrWhiteSpace(componentDecorationCode))
                {
                    continue;
                }

                var difference = GetDecorationDifference(
                    articleDecorationCode,
                    componentDecorationCode);

                var isCovered = IsDecorationDifferenceCovered(
                    difference,
                    routing2000TechnologyCodes);

                var context = new SapDecorationValidationContext
                {
                    ArticleNumber = summary.ArticleNumber,
                    BomNumber9200 = bom.BomNumber,
                    Position9200 = item.Position,
                    ArticleDecorationCode = articleDecorationCode,
                    ComponentDecorationCode = componentDecorationCode,
                    DecorationDifference = difference,
                    IsDecorationDifferenceCoveredByRouting2000 = isCovered,
                    Routing2000Technologies = routing2000TechnologiesText,
                    BomAlternative9200 = bom.Alternative,
                    IsSortingAlternative9200 = IsSortingAlternative(bom.Plant, bom.Alternative)
                };

                AddFindingsToSummary(
                    summary,
                    _validationEngine.Validate(
                        _validationRuleSet!.Rules,
                        "DECORATION_CHECK",
                        context));
            }
        }
    }
    private static HashSet<string> ExtractRouting2000TechnologyCodes(
    SapTechnicalArticleSummary summary)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var routing in summary.Routings2000)
        {
            foreach (var operation in routing.Operations)
            {
                var text = (
                    (operation.Description ?? string.Empty) + " " +
                    (operation.WorkCenter ?? string.Empty) + " " +
                    (operation.WorkCenterText ?? string.Empty)
                ).ToUpperInvariant();

                if (ContainsAny(text, "SÍTOTISK", "SITOTISK", "SCREEN", "DRUCK", "PRINT"))
                {
                    result.Add("D");
                }

                if (ContainsAny(text, "HORKÁ RAŽBA", "HORKA RAZBA", "RAŽBA", "RAZBA", "HOT", "STAMP", "PRÄGEN", "PRAGEN"))
                {
                    result.Add("P");
                }

                if (ContainsAny(text, "POSTŘIK", "POSTRIK", "SPRAY", "SPRITZ"))
                {
                    result.Add("B");
                }

                if (ContainsAny(text, "METALIZACE", "METALLIZATION", "METALLISIERUNG", "MET"))
                {
                    result.Add("E");
                }

                if (ContainsAny(text, "LEPENÍ", "LEPENI", "GLUE", "KLEBEN"))
                {
                    result.Add("K");
                }

                if (ContainsAny(text, "TŘÍDĚNÍ", "TRIDENI", "SORT", "SORTIER"))
                {
                    result.Add("N");
                }

                if (ContainsAny(text, "MÜNDUNG", "MUNDUNG", "MÜND", "MUND"))
                {
                    result.Add("V");
                }
            }
        }

        return result;
    }

    private static bool IsDecorationDifferenceCovered(
        string decorationDifference,
        HashSet<string> routingTechnologyCodes)
    {
        if (string.IsNullOrWhiteSpace(decorationDifference))
        {
            return true;
        }

        foreach (var code in decorationDifference.ToUpperInvariant())
        {
            if (!routingTechnologyCodes.Contains(code.ToString()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        foreach (var value in values)
        {
            if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
    private void ValidateBomHeadersByJsonRules(SapTechnicalArticleSummary summary)
    {
        foreach (var bom in summary.Boms9200.Concat(summary.Boms2000))
        {
            var context = new SapBomHeaderValidationContext
            {
                Plant = bom.Plant,
                BomNumber = bom.BomNumber,
                Alternative = bom.Alternative,
                BomUsage = bom.BomUsage,
                BaseQuantity = bom.BaseQuantity,
                BaseUnit = bom.BaseUnit
            };

            AddFindingsToSummary(
                summary,
                _validationEngine.Validate(
                    _validationRuleSet!.Rules,
                    "BOM_HEADER",
                    context));
        }
    }
    private void ValidateBomItemsByJsonRules(SapTechnicalArticleSummary summary)
    {
        foreach (var bom in summary.Boms9200.Concat(summary.Boms2000))
        {
            foreach (var item in bom.Items)
            {
                var context = new SapBomItemValidationContext
                {
                    ArticleNumber = summary.ArticleNumber,

                    Plant = bom.Plant,
                    BomNumber = bom.BomNumber,
                    Alternative = bom.Alternative,

                    Position = item.Position,
                    ItemCategory = item.ItemCategory,
                    ComponentNumber = item.ComponentNumber,
                    ComponentDescription = item.ComponentDescription,

                    Quantity = item.Quantity,
                    Unit = item.Unit,

                    ScrapPercent = item.ScrapPercent,
                    IsFixedQuantity = item.IsFixedQuantity,
                    IsTextItem = IsTextItem(item),

                    IsSelfComponent = IsSameMaterial(summary.ArticleNumber, item.ComponentNumber),
                    IsSortingAlternative = IsSortingAlternative(bom.Plant, bom.Alternative)
                };

                AddFindingsToSummary(
                    summary,
                    _validationEngine.Validate(
                        _validationRuleSet!.Rules,
                        "BOM_ITEM",
                        context));
            }
        }
    }

    private static string NormalizeTechMaterialNumberForCompare(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        return text.All(char.IsDigit)
            ? text.PadLeft(10, '0')
            : text;
    }

    private static bool IsSortingAlternative(string plant, string alternative)
    {
        return string.Equals(plant, "9200", StringComparison.OrdinalIgnoreCase)
               && string.Equals(NormalizeAlternativeForCompare(alternative), "11", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAlternativeForCompare(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        return int.TryParse(text, out var number)
            ? number.ToString("00")
            : text;
    }

    private static void AddFindingsToSummary(
    SapTechnicalArticleSummary summary,
    IReadOnlyList<SapValidationFinding> findings)
    {
        foreach (var finding in findings)
        {
            if (string.Equals(finding.Severity, "Error", StringComparison.OrdinalIgnoreCase))
            {
                summary.CriticalErrors.Add(finding.Message);
                continue;
            }

            summary.Warnings.Add(finding.Message);
        }
    }

    private static decimal? CalculateShiftTakt(decimal? baseQuantity)
    {
        return baseQuantity is null
            ? null
            : baseQuantity.Value * 7.5m;
    }

    private static string FormatDecimal(decimal? value)
    {
        return value is null
            ? string.Empty
            : value.Value.ToString("0.##");
    }

    private static bool IsSameMaterial(string left, string right)
    {
        return string.Equals(
            NormalizeMaterialNumber(left),
            NormalizeMaterialNumber(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMaterialNumber(string value)
    {
        value = value.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.All(char.IsDigit)
            ? value.PadLeft(10, '0')
            : value;
    }

    private static bool IsSameCounterOrMissing(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return true;
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

}