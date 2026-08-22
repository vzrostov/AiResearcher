using InsightFlow.App.Contracts;

namespace InsightFlow.App.Persistence.Entities;

public sealed class WorkflowExecutionEntity
{
    public Guid WorkflowId { get; set; }

    public required string Topic { get; set; }

    public required string RequestJson { get; set; }

    public WorkflowStatus Status { get; set; }

    public WorkflowStep CurrentStep { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public List<AgentResultEntity> Results { get; set; } = [];
}
