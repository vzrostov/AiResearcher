namespace InsightFlow.App.Configuration;

public sealed class WorkflowOptions
{
    public const string SectionName = "Workflow";

    public bool EmitAgentOutput { get; init; } = true;
}
