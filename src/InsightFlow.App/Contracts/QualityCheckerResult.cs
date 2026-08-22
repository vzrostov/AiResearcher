namespace InsightFlow.App.Contracts;

public sealed record QualityCheckerResult(
    QualityCheckerDecision Decision,
    double SourceCoverage,
    IReadOnlyList<string> Reasons);

public enum QualityCheckerDecision
{
    Pass,
    Warning,
    Reject
}
