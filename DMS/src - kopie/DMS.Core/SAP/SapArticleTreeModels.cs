namespace DMS.Core.Sap;

public sealed class SapArticleTreeGraph
{
    public string CurrentArticleNumber { get; init; } = string.Empty;
    public IReadOnlyList<SapArticleTreeNode> Nodes { get; init; } = Array.Empty<SapArticleTreeNode>();
    public IReadOnlyList<SapArticleTreeEdge> Edges { get; init; } = Array.Empty<SapArticleTreeEdge>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class SapArticleTreeNode
{
    public string MaterialNumber { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? OldMaterialNumber { get; init; }
    public string? MaterialKind { get; init; }
    public string StageCode { get; init; } = string.Empty;
    public int Level { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsSibling { get; set; }
    public bool IsPredecessor => Level < 0;
    public bool IsSuccessor => Level > 0;
    public bool HasKnownPredecessors { get; set; }
    public bool HasKnownSuccessors { get; set; }
}

public sealed class SapArticleTreeEdge
{
    public string FromMaterialNumber { get; init; } = string.Empty;
    public string ToMaterialNumber { get; init; } = string.Empty;
    public string Plant { get; init; } = string.Empty;
    public string? BomNumber { get; init; }
    public string? Alternative { get; init; }
    public string? Position { get; init; }
    public decimal? Quantity { get; init; }
    public string? Unit { get; init; }
}
