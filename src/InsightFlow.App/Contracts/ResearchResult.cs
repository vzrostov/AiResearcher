namespace InsightFlow.App.Contracts;

public sealed record ResearchResult : BaseAgentResult
{
    public required IReadOnlyList<SourceReference> Sources { get; init; }

    public required IReadOnlyList<ResearchFinding> Findings { get; init; }
}

public sealed record ResearchFinding(
    Guid Id,
    string Claim,
    string Evidence,
    IReadOnlyList<Guid> SourceIds,
    double? Confidence);
