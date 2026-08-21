namespace InsightFlow.App.Contracts;

public abstract record BaseAgentInput
{
    public required Guid WorkflowId { get; init; }

    public required Guid StepId { get; init; }
}
