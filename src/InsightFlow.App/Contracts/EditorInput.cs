namespace InsightFlow.App.Contracts;

public sealed record EditorInput : BaseAgentInput
{
    public required AnalysisResult Analysis { get; init; }

    public required FactCheckResult FactCheck { get; init; }

    public required CriticResult Critic { get; init; }
}
