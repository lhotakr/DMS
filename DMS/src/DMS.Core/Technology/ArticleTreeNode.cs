public sealed class ArticleTreeNode
{
    public string ArticleCode { get; init; } = "";
    public string? SapNumber { get; init; }
    public string? Description { get; init; }

    public string? KText { get; init; }
    public string? StageCode { get; init; }   // R, RB, RBD, RBDE...

    public int Level { get; set; }

    public bool IsCurrent { get; set; }
    public bool IsSibling { get; set; }
    public bool IsPredecessor { get; set; }
    public bool IsSuccessor { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
}