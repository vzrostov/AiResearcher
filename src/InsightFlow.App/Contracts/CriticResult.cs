namespace InsightFlow.App.Contracts;

public sealed record CriticResult : BaseAgentResult
{
    public required IReadOnlyList<CriticIssue> Issues { get; init; }

    public required IReadOnlyList<EvidenceConflict> Conflicts { get; init; }

    public required bool HasBlockingIssues { get; init; }
}

public sealed record CriticIssue(
    string Description,
    CriticSeverity Severity);

public sealed record EvidenceConflict(
    IReadOnlyList<Guid> FindingIds,
    IReadOnlyList<Guid> ConclusionIds,
    CriticSeverity Severity,
    ConflictStatus Status,
    string Description);

public enum CriticSeverity
{
    Info,
    Warning,
    Blocking
}

public enum ConflictStatus
{
    Resolved,
    Unresolved,
    InsufficientEvidence
}
