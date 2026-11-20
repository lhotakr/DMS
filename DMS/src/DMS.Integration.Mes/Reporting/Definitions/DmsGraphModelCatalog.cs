using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DMS.Integration.Mes.Reporting.Definitions;

/// <summary>
/// Metadata-only data models for SYS15. Does not execute joins or modify source data.
/// The catalog is separate from MES06 report definitions and active SQL overrides.
/// </summary>
public sealed class DmsGraphModelDefinition
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public List<DmsGraphNode> Nodes { get; set; } = new();
    public List<DmsGraphRelationship> Relationships { get; set; } = new();
}

public sealed class DmsGraphNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceCode { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public double X { get; set; } = 30;
    public double Y { get; set; } = 30;
    public List<DmsGraphColumn> Columns { get; set; } = new();
}

public sealed class DmsGraphColumn
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
}

public sealed class DmsGraphRelationship
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FromNodeId { get; set; } = string.Empty;
    public string FromColumn { get; set; } = string.Empty;
    public string ToNodeId { get; set; } = string.Empty;
    public string ToColumn { get; set; } = string.Empty;
    public string Cardinality { get; set; } = "OneToMany";
    public string FilterDirection { get; set; } = "Forward";
    public bool IsActive { get; set; } = true;
}

public sealed class DmsGraphModelCatalog
{
    private readonly string _file;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public DmsGraphModelCatalog(string configRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configRoot);
        _file = Path.Combine(configRoot, "DataModel", "dms-graph-models.json");
    }

    public string FilePath => _file;

    public (List<DmsGraphModelDefinition> Models, string Fingerprint) Load()
    {
        if (!File.Exists(_file)) return (new(), string.Empty);
        var bytes = File.ReadAllBytes(_file);
        if (bytes.Length > 2_000_000)
            throw new InvalidOperationException("Graph catalog exceeds 2 MB.");
        var items = JsonSerializer.Deserialize<List<DmsGraphModelDefinition>>(bytes, Options) ?? new();
        Validate(items);
        return (items, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    public string Save(IReadOnlyList<DmsGraphModelDefinition> models, string expectedFingerprint)
    {
        Validate(models);
        // Optimistic concurrency prevents overwriting changes by another DMS administrator.
        var actual = File.Exists(_file)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_file)))
            : string.Empty;
        if (!StringComparer.Ordinal.Equals(expectedFingerprint, actual))
            throw new InvalidOperationException("Graph catalog changed on the server. Reload before saving.");

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(models, Options));
        if (bytes.Length > 2_000_000)
            throw new InvalidOperationException("Graph catalog exceeds 2 MB.");
        DmsDataSourceCatalog.SaveAtomic(_file, Encoding.UTF8.GetString(bytes));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static DmsGraphModelDefinition Clone(DmsGraphModelDefinition model) =>
        JsonSerializer.Deserialize<DmsGraphModelDefinition>(JsonSerializer.Serialize(model, Options), Options)!;

    public static void Validate(IReadOnlyList<DmsGraphModelDefinition> models)
    {
        if (models.Count > 50) throw new InvalidOperationException("Maximum 50 graph models.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            if (!Regex.IsMatch(model.Code ?? "", "^[A-Za-z][A-Za-z0-9_]{1,39}$"))
                throw new InvalidOperationException("Invalid model code (2-40 letters, digits, underscore).");
            if (!names.Add(model.Code)) throw new InvalidOperationException("Duplicate model code.");
            if (string.IsNullOrWhiteSpace(model.Name) || model.Name.Length > 120)
                throw new InvalidOperationException("Invalid model name.");
            if (model.Version != 1) throw new InvalidOperationException("Unsupported graph model version.");
            if (model.Nodes is null || model.Relationships is null || model.Nodes.Count > 60 || model.Relationships.Count > 150)
                throw new InvalidOperationException("Too many nodes/relationships or missing collections.");

            var nodes = new Dictionary<string, DmsGraphNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in model.Nodes)
            {
                if (!Guid.TryParseExact(node.Id, "N", out _) || !nodes.TryAdd(node.Id, node))
                    throw new InvalidOperationException("Invalid or duplicate node ID.");
                if (string.IsNullOrWhiteSpace(node.SourceCode) || node.SourceCode.Length > 40 ||
                    string.IsNullOrWhiteSpace(node.ObjectName) || node.ObjectName.Length > 250)
                    throw new InvalidOperationException("Invalid table/source identifier.");
                if (!double.IsFinite(node.X) || !double.IsFinite(node.Y) || node.X < 0 || node.Y < 0 || node.X > 5000 || node.Y > 5000)
                    throw new InvalidOperationException("Node outside allowed drawing surface.");
                if (node.Columns is null || node.Columns.Count > 150 || node.Columns.Count == 0)
                    throw new InvalidOperationException("A node must contain 1-150 columns.");
                if (node.Columns.Any(c => string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 200 || (c.DataType?.Length ?? 0) > 100) ||
                    node.Columns.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != node.Columns.Count)
                    throw new InvalidOperationException("Invalid or duplicate column name.");
            }
            var edges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var edge in model.Relationships)
            {
                if (!Guid.TryParseExact(edge.Id, "N", out _) || !ids.Add(edge.Id))
                    throw new InvalidOperationException("Invalid or duplicate relationship ID.");
                if (!nodes.TryGetValue(edge.FromNodeId, out var from) || !nodes.TryGetValue(edge.ToNodeId, out var to) || from.Id == to.Id)
                    throw new InvalidOperationException("Relationship references missing or identical nodes.");
                if (!from.Columns.Any(c => c.Name.Equals(edge.FromColumn, StringComparison.OrdinalIgnoreCase)) ||
                    !to.Columns.Any(c => c.Name.Equals(edge.ToColumn, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Relationship references a missing column.");
                if (edge.Cardinality is not ("OneToOne" or "OneToMany" or "ManyToOne" or "ManyToMany") ||
                    edge.FilterDirection is not ("Forward" or "Reverse" or "Both"))
                    throw new InvalidOperationException("Invalid relationship cardinality/filter direction.");
                var endpointA = edge.FromNodeId + "." + edge.FromColumn;
                var endpointB = edge.ToNodeId + "." + edge.ToColumn;
                var canonical = string.Compare(endpointA, endpointB, StringComparison.OrdinalIgnoreCase) < 0
                    ? endpointA + "|" + endpointB : endpointB + "|" + endpointA;
                if (!edges.Add(canonical)) throw new InvalidOperationException("Duplicate relationship.");
            }
        }
    }
}
