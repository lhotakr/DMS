namespace DMS.Core.Security;

public sealed record DmsAuthorizationResult
{
    public bool Allowed { get; init; }
    public bool IsPublic { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public IReadOnlyList<string> RequiredRoles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MatchingRoles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingRoles { get; init; } = Array.Empty<string>();
}
