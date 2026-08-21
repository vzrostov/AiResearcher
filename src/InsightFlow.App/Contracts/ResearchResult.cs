namespace InsightFlow.App.Contracts;

public sealed record ResearchResult : BaseAgentResult
{
    public required IReadOnlyList<ResearchFinding> Findings { get; init; }
}

public sealed record ResearchFinding(
    string Claim,
    string Evidence,
    string? Source = null,
    double? Confidence = null);
