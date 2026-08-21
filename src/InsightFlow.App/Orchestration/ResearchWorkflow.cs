using InsightFlow.App.Agents;
using InsightFlow.App.Configuration;
using InsightFlow.App.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace InsightFlow.App.Orchestration;

public sealed class ResearchWorkflow
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly AgentFactory _agentFactory;
    private readonly WorkflowOptions _options;
    private readonly OpenAIOptions _openAIOptions;
    private readonly ILogger<ResearchWorkflow> _logger;

    public ResearchWorkflow(
        AgentFactory agentFactory,
        IOptions<WorkflowOptions> options,
        IOptions<OpenAIOptions> openAIOptions,
        ILogger<ResearchWorkflow> logger)
    {
        _agentFactory = agentFactory;
        _options = options.Value;
        _openAIOptions = openAIOptions.Value;
        _logger = logger;
    }

    public async Task<WorkflowResult> RunAsync(
        ResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Topic);

        var workflowId = Guid.NewGuid();
        var agents = _agentFactory.Create();
        var stageOutputs = new List<AgentStageOutput>();

        var runOptions = new ChatClientAgentRunOptions
        {
            ChatOptions = new ChatOptions
            {
                MaxOutputTokens = _openAIOptions.MaxOutputTokens
            }
        };

        _logger.LogInformation(
            "Starting analytical workflow {WorkflowId} for topic {Topic}",
            workflowId,
            request.Topic);

        var researchInput = new ResearchInput
        {
            WorkflowId = workflowId,
            StepId = Guid.NewGuid(),
            Request = request
        };

        var researchResult = await RunAgentAsync(
            "Researcher",
            researchInput.StepId,
            async () =>
            {
                var response = await agents.Researcher.RunAsync<ResearchPayload>(
                    SerializeInput(researchInput),
                    options: runOptions,
                    cancellationToken: cancellationToken);

                var payload = response.Result; 

                return new ResearchResult
                {
                    Id = Guid.NewGuid(),
                    WorkflowId = workflowId,
                    StepId = researchInput.StepId,
                    ProducedByAgent = "Researcher",
                    ProducedAt = DateTimeOffset.UtcNow,
                    Findings = payload.Findings
                };
            });
        AddStageOutput(stageOutputs, "Researcher", researchResult);


        var analysisStepId = Guid.NewGuid();

        var analysisResult = await RunAgentAsync(
            "Analyst",
            analysisStepId,
            async () =>
            {
                var response = await agents.Analyst.RunAsync<AnalysisPayload>(
                    SerializeInput(new
                    {
                        WorkflowId = workflowId,
                        StepId = analysisStepId,
                        Research = researchResult
                    }),
                    options: runOptions,
                    cancellationToken: cancellationToken);

                var payload = response.Result;

                return new AnalysisResult
                {
                    Id = Guid.NewGuid(),
                    WorkflowId = workflowId,
                    StepId = analysisStepId,
                    ProducedByAgent = "Analyst",
                    ProducedAt = DateTimeOffset.UtcNow,
                    ParentResultIds = [researchResult.Id],
                    Conclusions = payload.Conclusions
                        .Select(conclusion => new AnalysisConclusion(
                            conclusion,
                            [researchResult.Id]))
                        .ToArray()
                };
            });

        AddStageOutput(stageOutputs, "Analyst", analysisResult);


        var factCheckStepId = Guid.NewGuid();

        var factCheckResult = await RunAgentAsync(
            "FactChecker",
            factCheckStepId,
            async () =>
            {
                var response = await agents.FactChecker.RunAsync<FactCheckPayload>(
                    SerializeInput(new
                    {
                        WorkflowId = workflowId,
                        StepId = factCheckStepId,
                        Research = researchResult,
                        Analysis = analysisResult
                    }),
                    options: runOptions,
                    cancellationToken: cancellationToken);

                var payload = response.Result;

                return new FactCheckResult
                {
                    Id = Guid.NewGuid(),
                    WorkflowId = workflowId,
                    StepId = factCheckStepId,
                    ProducedByAgent = "FactChecker",
                    ProducedAt = DateTimeOffset.UtcNow,
                    ParentResultIds = [researchResult.Id, analysisResult.Id],
                    Items = payload.Items
                };
            });

        AddStageOutput(stageOutputs, "FactChecker", factCheckResult);


        var criticStepId = Guid.NewGuid();

        var criticResult = await RunAgentAsync(
            "Critic",
            criticStepId,
            async () =>
            {
                var response = await agents.Critic.RunAsync<CriticPayload>(
                    SerializeInput(new
                    {
                        WorkflowId = workflowId,
                        StepId = criticStepId,
                        Analysis = analysisResult,
                        FactCheck = factCheckResult
                    }),
                    options: runOptions,
                    cancellationToken: cancellationToken);

                var payload = response.Result;

                return new CriticResult
                {
                    Id = Guid.NewGuid(),
                    WorkflowId = workflowId,
                    StepId = criticStepId,
                    ProducedByAgent = "Critic",
                    ProducedAt = DateTimeOffset.UtcNow,
                    ParentResultIds = [analysisResult.Id, factCheckResult.Id],
                    Issues = payload.Issues,
                    HasBlockingIssues = payload.HasBlockingIssues
                };
            });

        AddStageOutput(stageOutputs, "Critic", criticResult);


        var editorInput = new EditorInput
        {
            WorkflowId = workflowId,
            StepId = Guid.NewGuid(),
            Analysis = analysisResult,
            FactCheck = factCheckResult,
            Critic = criticResult
        };

        var editorResult = await RunAgentAsync(
            "Editor",
            editorInput.StepId,
            async () =>
            {
                var response = await agents.Editor.RunAsync<EditorPayload>(
                    SerializeInput(editorInput),
                    options: runOptions,
                    cancellationToken: cancellationToken);

                var payload = response.Result;

                return new EditorResult
                {
                    Id = Guid.NewGuid(),
                    WorkflowId = workflowId,
                    StepId = editorInput.StepId,
                    ProducedByAgent = "Editor",
                    ProducedAt = DateTimeOffset.UtcNow,
                    ParentResultIds = [analysisResult.Id, factCheckResult.Id, criticResult.Id],
                    Content = payload.Content
                };
            });

        AddStageOutput(stageOutputs, "Editor", editorResult);

        _logger.LogInformation(
            "Analytical workflow {WorkflowId} completed for topic {Topic}",
            workflowId,
            request.Topic);

        return new WorkflowResult(editorResult.Content, stageOutputs);
    }


    private async Task<T> RunAgentAsync<T>(
        string agentName,
        Guid stepId,
        Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Agent {AgentName} execution was cancelled. StepId: {StepId}",
                agentName,
                stepId);

            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Agent {AgentName} execution failed. StepId: {StepId}",
                agentName,
                stepId);

            throw;
        }
    }

    private string SerializeInput<T>(T input) =>
        JsonSerializer.Serialize(input, s_jsonOptions);

    private void AddStageOutput<T>(
        ICollection<AgentStageOutput> outputs,
        string agentName,
        T result)
    {
        var text = result is EditorResult editor
            ? editor.Content
            : JsonSerializer.Serialize(result, s_jsonOptions);

        outputs.Add(new AgentStageOutput(agentName, text));

        if (_options.EmitAgentOutput)
        {
            Console.WriteLine();
            Console.WriteLine($"--- {agentName} ---");
            Console.WriteLine(text);
        }
    }

    private sealed record ResearchPayload(List<ResearchFinding> Findings);

    private sealed record AnalysisPayload(List<string> Conclusions);

    private sealed record FactCheckPayload(List<FactCheckItem> Items);

    private sealed record CriticPayload(
        List<CriticIssue> Issues,
        bool HasBlockingIssues);

    private sealed record EditorPayload(string Content);
}
