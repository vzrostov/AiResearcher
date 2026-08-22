namespace InsightFlow.App.Contracts;

public sealed record AnalysisResult : BaseAgentResult
{
    public required IReadOnlyList<AnalysisConclusion> Conclusions { get; init; }
}

public sealed record AnalysisConclusion(
    Guid Id,
    string Conclusion,
    IReadOnlyList<Guid> SupportingFindingIds);
