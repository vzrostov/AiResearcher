namespace InsightFlow.App.Contracts;

public sealed class WorkflowState
{
    public required Guid WorkflowId { get; init; }

    public required WorkflowStatus Status { get; set; }

    public required WorkflowStep CurrentStep { get; set; }

    public List<BaseAgentResult> Results { get; } = [];

    public string? Error { get; set; }
}
