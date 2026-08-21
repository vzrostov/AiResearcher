namespace InsightFlow.App.Contracts;

public sealed record EditorResult : BaseAgentResult
{
    public required string Content { get; init; }
}
