namespace InsightFlow.App.Persistence.Entities;

public sealed class AgentResultEntity
{
    public Guid Id { get; set; }

    public Guid WorkflowId { get; set; }

    public Guid StepId { get; set; }

    public required string ProducedByAgent { get; set; }

    public DateTimeOffset ProducedAt { get; set; }

    public required string ResultType { get; set; }

    public required string PayloadJson { get; set; }

    public required string ParentResultIdsJson { get; set; }

    public WorkflowExecutionEntity Workflow { get; set; } = null!;
}
