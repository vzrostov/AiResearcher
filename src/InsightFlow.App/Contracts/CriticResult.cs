namespace InsightFlow.App.Contracts;

public sealed record CriticResult : BaseAgentResult
{
    public required IReadOnlyList<CriticIssue> Issues { get; init; }

    public required bool HasBlockingIssues { get; init; }
}

public sealed record CriticIssue(
    string Description,
    CriticSeverity Severity);

public enum CriticSeverity
{
    Info,
    Warning,
    Blocking
}
