using Microsoft.Agents.AI;

namespace InsightFlow.App.Agents;

public sealed record AgentSet(
    AIAgent Researcher,
    AIAgent Analyst,
    AIAgent FactChecker,
    AIAgent Critic,
    AIAgent Editor)
{
    public IReadOnlyList<AIAgent> InExecutionOrder =>
    [
        Researcher,
        Analyst,
        FactChecker,
        Critic,
        Editor
    ];
}
