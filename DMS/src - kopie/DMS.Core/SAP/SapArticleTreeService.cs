using System.Text.RegularExpressions;

namespace DMS.Core.Sap;

/// <summary>
/// Builds a read-only article transformation graph from the imported SAP BOM cache.
/// Topology is derived from plant 9200 BOM relations. Decoration/stage
/// values (R, RB, RBD, RBDE, ...) are metadata used for display and to refine
/// same-level sibling placement without inventing new BOM relations.
/// </summary>
public sealed class SapArticleTreeService
{
    public const string DefaultPlant = "9200";
    private const int MaxTraversalDepth = 20;

    private static readonly Regex StageTokenRegex = new(
        @"(?<![A-Z0-9])R[A-Z]{0,5}(?![A-Z0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly IReadOnlyDictionary<string, SapMaterial> _materials;
    private readonly SapMaterialUsageOverviewService _usageService;
    private readonly Dictionary<string, SapMaterialUsageOverview> _overviewCache =
        new(StringComparer.OrdinalIgnoreCase);

    public SapArticleTreeService(SapStoragePaths storagePaths)
    {
        ArgumentNullException.ThrowIfNull(storagePaths);

        var materials = new JsonSapMaterialRepository(storagePaths.SapMaterialsFilePath).LoadAll();
        var boms = new JsonSapBomRepository(storagePaths.SapBomSnapshotsFilePath).LoadAll();

        _materials = materials
            .Where(item => !string.IsNullOrWhiteSpace(item.MaterialNumber))
            .GroupBy(item => item.MaterialNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        _usageService = new SapMaterialUsageOverviewService(materials, boms);
    }

    public SapArticleTreeGraph BuildGraph(
        string articleNumber,
        int successorDepth = 2,
        bool includeSiblings = true,
        string plant = DefaultPlant)
    {
        articleNumber = (articleNumber ?? string.Empty).Trim();
        plant = string.IsNullOrWhiteSpace(plant) ? DefaultPlant : plant.Trim();
        successorDepth = successorDepth <= 0 ? 1 : Math.Min(successorDepth, MaxTraversalDepth);

        var nodes = new Dictionary<string, SapArticleTreeNode>(StringComparer.OrdinalIgnoreCase);
        var edges = new Dictionary<string, SapArticleTreeEdge>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        AddOrUpdateNode(nodes, articleNumber, 0, isCurrent: true, isSibling: false);

        TraversePredecessors(
            articleNumber,
            0,
            plant,
            nodes,
            edges,
            warnings,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { articleNumber });

        TraverseSuccessors(
            articleNumber,
            0,
            successorDepth,
            plant,
            nodes,
            edges,
            warnings,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { articleNumber });

        if (includeSiblings)
        {
            AddSiblings(articleNumber, plant, nodes, edges);
        }

        foreach (var node in nodes.Values)
        {
            node.HasKnownPredecessors = GetPredecessorEdges(node.MaterialNumber, plant).Count > 0;
            node.HasKnownSuccessors = GetSuccessorEdges(node.MaterialNumber, plant).Count > 0;
        }

        return new SapArticleTreeGraph
        {
            CurrentArticleNumber = articleNumber,
            Nodes = nodes.Values
                .OrderBy(node => node.Level)
                .ThenBy(node => node.IsCurrent ? 0 : 1)
                .ThenBy(node => StageSortKey(node.StageCode))
                .ThenBy(node => node.MaterialNumber, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Edges = edges.Values
                .OrderBy(edge => edge.FromMaterialNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(edge => edge.ToMaterialNumber, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private void TraversePredecessors(
        string materialNumber,
        int level,
        string plant,
        IDictionary<string, SapArticleTreeNode> nodes,
        IDictionary<string, SapArticleTreeEdge> edges,
        ICollection<string> warnings,
        HashSet<string> path)
    {
        if (-level >= MaxTraversalDepth)
        {
            warnings.Add($"Predecessor traversal reached safety depth {MaxTraversalDepth} at {materialNumber}.");
            return;
        }

        foreach (var edge in GetPredecessorEdges(materialNumber, plant))
        {
            if (!AddEdge(edges, edge))
            {
                continue;
            }

            AddOrUpdateNode(nodes, edge.FromMaterialNumber, level - 1, false, false);

            if (path.Contains(edge.FromMaterialNumber))
            {
                warnings.Add($"BOM cycle detected: {edge.FromMaterialNumber} -> {materialNumber}.");
                continue;
            }

            var nextPath = new HashSet<string>(path, StringComparer.OrdinalIgnoreCase)
            {
                edge.FromMaterialNumber
            };

            TraversePredecessors(
                edge.FromMaterialNumber,
                level - 1,
                plant,
                nodes,
                edges,
                warnings,
                nextPath);
        }
    }

    private void TraverseSuccessors(
        string materialNumber,
        int level,
        int remainingDepth,
        string plant,
        IDictionary<string, SapArticleTreeNode> nodes,
        IDictionary<string, SapArticleTreeEdge> edges,
        ICollection<string> warnings,
        HashSet<string> path)
    {
        if (remainingDepth <= 0)
        {
            return;
        }

        foreach (var edge in GetSuccessorEdges(materialNumber, plant))
        {
            if (!AddEdge(edges, edge))
            {
                continue;
            }

            AddOrUpdateNode(nodes, edge.ToMaterialNumber, level + 1, false, false);

            if (path.Contains(edge.ToMaterialNumber))
            {
                warnings.Add($"BOM cycle detected: {materialNumber} -> {edge.ToMaterialNumber}.");
                continue;
            }

            var nextPath = new HashSet<string>(path, StringComparer.OrdinalIgnoreCase)
            {
                edge.ToMaterialNumber
            };

            TraverseSuccessors(
                edge.ToMaterialNumber,
                level + 1,
                remainingDepth - 1,
                plant,
                nodes,
                edges,
                warnings,
                nextPath);
        }
    }

    private void AddSiblings(
        string currentArticle,
        string plant,
        IDictionary<string, SapArticleTreeNode> nodes,
        IDictionary<string, SapArticleTreeEdge> edges)
    {
        var directPredecessors = GetPredecessorEdges(currentArticle, plant);

        foreach (var predecessor in directPredecessors)
        {
            foreach (var siblingEdge in GetSuccessorEdges(predecessor.FromMaterialNumber, plant))
            {
                if (!AddEdge(edges, siblingEdge))
                {
                    continue;
                }

                if (string.Equals(
                        siblingEdge.ToMaterialNumber,
                        currentArticle,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativeLevel = InferRelativeLevelFromStage(currentArticle, siblingEdge.ToMaterialNumber);
                AddOrUpdateNode(
                    nodes,
                    siblingEdge.ToMaterialNumber,
                    relativeLevel,
                    isCurrent: false,
                    isSibling: relativeLevel == 0);
            }
        }
    }

    private IReadOnlyList<SapArticleTreeEdge> GetPredecessorEdges(string materialNumber, string plant)
    {
        var result = new List<SapArticleTreeEdge>();
        var overview = GetOverview(materialNumber);

        foreach (var bom in overview.OwnBomVariants)
        {
            if (!string.Equals(bom.Plant, plant, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var item in bom.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ComponentNumber) ||
                    string.Equals(item.ComponentNumber, materialNumber, StringComparison.OrdinalIgnoreCase) ||
                    !IsArticleTreeMaterial(
                        item.ComponentNumber,
                        item.ComponentKind,
                        item.ComponentDescription))
                {
                    continue;
                }

                result.Add(new SapArticleTreeEdge
                {
                    FromMaterialNumber = item.ComponentNumber,
                    ToMaterialNumber = materialNumber,
                    Plant = bom.Plant,
                    BomNumber = Convert.ToString(bom.BomNumber),
                    Alternative = Convert.ToString(bom.Alternative),
                    Position = Convert.ToString(item.Position),
                    Quantity = ToNullableDecimal(item.Quantity),
                    Unit = Convert.ToString(item.Unit)
                });
            }
        }

        return result
            .GroupBy(edge => EdgeKey(edge.FromMaterialNumber, edge.ToMaterialNumber), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private IReadOnlyList<SapArticleTreeEdge> GetSuccessorEdges(string materialNumber, string plant)
    {
        var overview = GetOverview(materialNumber);

        return overview.UsedAsComponent
            .Where(row =>
                string.Equals(row.Plant, plant, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(row.ParentMaterialNumber) &&
                !string.Equals(row.ParentMaterialNumber, materialNumber, StringComparison.OrdinalIgnoreCase) &&
                IsArticleTreeMaterial(
                    row.ParentMaterialNumber,
                    row.ParentMaterialKind,
                    row.ParentDescription))
            .Select(row => new SapArticleTreeEdge
            {
                FromMaterialNumber = materialNumber,
                ToMaterialNumber = row.ParentMaterialNumber,
                Plant = row.Plant,
                BomNumber = Convert.ToString(row.BomNumber),
                Alternative = Convert.ToString(row.Alternative),
                Position = Convert.ToString(row.Position),
                Quantity = ToNullableDecimal(row.Quantity),
                Unit = Convert.ToString(row.Unit)
            })
            .GroupBy(edge => EdgeKey(edge.FromMaterialNumber, edge.ToMaterialNumber), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private SapMaterialUsageOverview GetOverview(string materialNumber)
    {
        if (_overviewCache.TryGetValue(materialNumber, out var cached))
        {
            return cached;
        }

        var overview = _usageService.BuildOverview(materialNumber);
        _overviewCache[materialNumber] = overview;
        return overview;
    }

    private bool IsArticleTreeMaterial(
        string materialNumber,
        string? suppliedKind,
        string? suppliedDescription)
    {
        if (_materials.TryGetValue(materialNumber, out var material))
        {
            if (string.Equals(
                    material.MaterialKind,
                    nameof(SapMaterialKind.GlassArticle),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(ExtractStageCode(material)))
            {
                return true;
            }
        }

        if (string.Equals(
                suppliedKind,
                nameof(SapMaterialKind.GlassArticle),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(ExtractStageCode(suppliedDescription));
    }

    private SapArticleTreeNode AddOrUpdateNode(
        IDictionary<string, SapArticleTreeNode> nodes,
        string materialNumber,
        int level,
        bool isCurrent,
        bool isSibling)
    {
        if (nodes.TryGetValue(materialNumber, out var existing))
        {
            if (isCurrent)
            {
                existing.Level = 0;
                existing.IsCurrent = true;
                existing.IsSibling = false;
                return existing;
            }

            if (existing.IsCurrent)
            {
                return existing;
            }

            if (level < 0)
            {
                existing.Level = existing.Level < 0
                    ? Math.Min(existing.Level, level)
                    : level;
                existing.IsSibling = false;
            }
            else if (level > 0)
            {
                existing.Level = existing.Level > 0
                    ? Math.Max(existing.Level, level)
                    : level;
                existing.IsSibling = false;
            }
            else if (existing.Level == 0 && isSibling)
            {
                existing.IsSibling = true;
            }

            return existing;
        }

        _materials.TryGetValue(materialNumber, out var material);

        var node = new SapArticleTreeNode
        {
            MaterialNumber = materialNumber,
            Description = material?.Description ?? string.Empty,
            OldMaterialNumber = material?.OldMaterialNumber,
            MaterialKind = material?.MaterialKind,
            StageCode = material is null
                ? string.Empty
                : ExtractStageCode(material),
            Level = isCurrent ? 0 : level,
            IsCurrent = isCurrent,
            IsSibling = !isCurrent && level == 0 && isSibling
        };

        nodes[materialNumber] = node;
        return node;
    }

    private int InferRelativeLevelFromStage(string currentArticle, string candidateMaterialNumber)
    {
        if (string.Equals(currentArticle, candidateMaterialNumber, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var currentStage = GetStageCode(currentArticle);
        var candidateStage = GetStageCode(candidateMaterialNumber);

        if (string.IsNullOrWhiteSpace(currentStage) || string.IsNullOrWhiteSpace(candidateStage))
        {
            return 0;
        }

        currentStage = currentStage.ToUpperInvariant();
        candidateStage = candidateStage.ToUpperInvariant();

        if (string.Equals(currentStage, candidateStage, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (candidateStage.StartsWith(currentStage, StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(1, candidateStage.Length - currentStage.Length);
        }

        if (currentStage.StartsWith(candidateStage, StringComparison.OrdinalIgnoreCase))
        {
            return -Math.Max(1, currentStage.Length - candidateStage.Length);
        }

        var prefixLength = CommonPrefixLength(currentStage, candidateStage);
        if (prefixLength > 0)
        {
            var difference = candidateStage.Length - currentStage.Length;
            if (difference != 0)
            {
                return difference;
            }
        }

        return 0;
    }

    private string GetStageCode(string materialNumber)
    {
        if (_materials.TryGetValue(materialNumber, out var material))
        {
            return ExtractStageCode(material);
        }

        return string.Empty;
    }

    private static bool AddEdge(
        IDictionary<string, SapArticleTreeEdge> edges,
        SapArticleTreeEdge edge)
    {
        if (string.IsNullOrWhiteSpace(edge.FromMaterialNumber) ||
            string.IsNullOrWhiteSpace(edge.ToMaterialNumber) ||
            string.Equals(edge.FromMaterialNumber, edge.ToMaterialNumber, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var key = EdgeKey(edge.FromMaterialNumber, edge.ToMaterialNumber);
        if (edges.ContainsKey(key))
        {
            return false;
        }

        edges[key] = edge;
        return true;
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var limit = Math.Min(left.Length, right.Length);
        var index = 0;

        while (index < limit && char.ToUpperInvariant(left[index]) == char.ToUpperInvariant(right[index]))
        {
            index++;
        }

        return index;
    }

    private static decimal? ToNullableDecimal(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToDecimal(value);
        }
        catch
        {
            return null;
        }
    }

    private static string EdgeKey(string from, string to) => $"{from}\u001f{to}";

    private static string StageSortKey(string? stage)
    {
        if (string.IsNullOrWhiteSpace(stage))
        {
            return "ZZZZZZ";
        }

        return stage.Length.ToString("D2") + stage.ToUpperInvariant();
    }

    private static string ExtractStageCode(SapMaterial material)
    {
        // KTEXT/MAKT description is the preferred source because it can already
        // contain the complete R / RB / RBD / RBDE-style stage token.
        var fromText = ExtractStageCode(material.Description);
        if (!string.IsNullOrWhiteSpace(fromText))
        {
            return fromText;
        }

        if (!string.IsNullOrWhiteSpace(material.GlassInfo?.DecorationChain))
        {
            var chain = material.GlassInfo.DecorationChain.Trim().ToUpperInvariant();
            return chain.StartsWith("R", StringComparison.OrdinalIgnoreCase)
                ? chain
                : "R" + chain;
        }

        return string.Empty;
    }

    private static string ExtractStageCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var matches = StageTokenRegex.Matches(text.ToUpperInvariant());
        if (matches.Count == 0)
        {
            return string.Empty;
        }

        // Prefer the shortest R-chain token. This avoids accidentally choosing
        // a longer ordinary word beginning with R when KTEXT contains both.
        return matches
            .Select(match => match.Value.ToUpperInvariant())
            .OrderBy(value => value.Length)
            .ThenBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault() ?? string.Empty;
    }
}
