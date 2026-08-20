using InsightFlow.App.Agents;
using InsightFlow.App.Configuration;
using InsightFlow.App.Contracts;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InsightFlow.App.Orchestration;

public sealed class ResearchWorkflow
{
    private readonly AgentFactory _agentFactory;
    private readonly WorkflowOptions _options;
    private readonly ILogger<ResearchWorkflow> _logger;

    public ResearchWorkflow(
        AgentFactory agentFactory,
        IOptions<WorkflowOptions> options,
        ILogger<ResearchWorkflow> logger)
    {
        _agentFactory = agentFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WorkflowResult> RunAsync(
        ResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Topic);

        var agents = _agentFactory.Create();
        var workflow = AgentWorkflowBuilder.BuildSequential(agents.InExecutionOrder);

        var input = new List<ChatMessage>
        {
            new(ChatRole.User, request.ToPrompt())
        };

        var stageOutputs = new List<AgentStageOutput>();
        var finalMessages = new List<ChatMessage>();

        _logger.LogInformation(
            "Starting analytical workflow for topic {Topic} with {AgentCount} agents",
            request.Topic,
            agents.InExecutionOrder.Count);

        cancellationToken.ThrowIfCancellationRequested();

        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, input);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        string? currentAgent = null;
        var currentBuffer = new System.Text.StringBuilder();

        await foreach (var evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case AgentResponseUpdateEvent update:
                    if (!string.Equals(currentAgent, update.ExecutorId, StringComparison.Ordinal))
                    {
                        FlushStage(stageOutputs, currentAgent, currentBuffer);
                        currentAgent = update.ExecutorId;

                        if (_options.EmitAgentOutput)
                        {
                            Console.WriteLine();
                            Console.WriteLine($"--- {currentAgent} ---");
                        }
                    }

                    if (!string.IsNullOrEmpty(update.Update.Text))
                    {
                        currentBuffer.Append(update.Update.Text);

                        if (_options.EmitAgentOutput)
                        {
                            Console.Write(update.Update.Text);
                        }
                    }
                    break;

                case WorkflowOutputEvent output:
                    FlushStage(stageOutputs, currentAgent, currentBuffer);

                    var messages = output.As<List<ChatMessage>>();
                    if (messages is not null)
                    {
                        finalMessages.AddRange(messages);
                    }
                    break;
            }
        }

        var finalText = finalMessages.LastOrDefault(static m => m.Role == ChatRole.Assistant)?.Text
            ?? stageOutputs.LastOrDefault()?.Text
            ?? string.Empty;

        _logger.LogInformation(
            "Analytical workflow completed for topic {Topic}; stages captured: {StageCount}",
            request.Topic,
            stageOutputs.Count);

        return new WorkflowResult(finalText, stageOutputs);
    }

    private static void FlushStage(
        ICollection<AgentStageOutput> outputs,
        string? agentName,
        System.Text.StringBuilder buffer)
    {
        if (string.IsNullOrWhiteSpace(agentName) || buffer.Length == 0)
        {
            return;
        }

        outputs.Add(new AgentStageOutput(agentName, buffer.ToString().Trim()));
        buffer.Clear();
    }
}
