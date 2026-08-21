namespace InsightFlow.App.Contracts;

public sealed record ResearchInput : BaseAgentInput
{
    public required ResearchRequest Request { get; init; }
}
