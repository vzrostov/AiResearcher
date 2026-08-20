namespace InsightFlow.App.Contracts;

public sealed record WorkflowResult(
    string FinalText,
    IReadOnlyList<AgentStageOutput> Stages);

public sealed record AgentStageOutput(
    string AgentName,
    string Text);
