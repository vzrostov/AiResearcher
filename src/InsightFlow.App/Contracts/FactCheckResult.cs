namespace InsightFlow.App.Contracts;

public sealed record FactCheckResult : BaseAgentResult
{
    public required IReadOnlyList<FactCheckItem> Items { get; init; }
}

public sealed record FactCheckItem(
    Guid ConclusionId,
    string Claim,
    FactCheckStatus Status,
    string? Comment);

public enum FactCheckStatus
{
    Verified,
    Unsupported,
    Contradicted
}
