namespace InsightFlow.App.Contracts;

public sealed record FactCheckResult : BaseAgentResult
{
    public required IReadOnlyList<FactCheckItem> Items { get; init; }
}

public sealed record FactCheckItem(
    string Claim,
    FactCheckStatus Status,
    string? Comment = null);

public enum FactCheckStatus
{
    Verified,
    Unsupported,
    Contradicted
}
