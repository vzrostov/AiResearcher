namespace InsightFlow.App.Contracts;

public abstract record BaseAgentResult
{
    public required Guid Id { get; init; }

    public required Guid WorkflowId { get; init; }

    public required Guid StepId { get; init; }

    public required string ProducedByAgent { get; init; }

    public required DateTimeOffset ProducedAt { get; init; }

    public IReadOnlyList<Guid> ParentResultIds { get; init; } = [];
}
