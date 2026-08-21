using InsightFlow.App.Agents;
using InsightFlow.App.Configuration;
using InsightFlow.App.Contracts;
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

        var state = new WorkflowState
        {
            WorkflowId = Guid.NewGuid(),
            Status = WorkflowStatus.Running,
            CurrentStep = WorkflowStep.Research
        };

        var agents = _agentFactory.Create();
        var stageOutputs = new List<AgentStageOutput>();

        var runOptions = new Microsoft.Agents.AI.ChatClientAgentRunOptions
        {
            ChatOptions = new Microsoft.Extensions.AI.ChatOptions
            {
                MaxOutputTokens = _openAIOptions.MaxOutputTokens
            }
        };

        _logger.LogInformation(
            "Starting analytical workflow {WorkflowId} for topic {Topic}",
            state.WorkflowId,
            request.Topic);

        try
        {
            var researchInput = new ResearchInput
            {
                WorkflowId = state.WorkflowId,
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
                        WorkflowId = state.WorkflowId,
                        StepId = researchInput.StepId,
                        ProducedByAgent = "Researcher",
                        ProducedAt = DateTimeOffset.UtcNow,
                        Findings = payload.Findings
                    };
                });

            state.Results.Add(researchResult);
            AddStageOutput(stageOutputs, "Researcher", researchResult);

            state.CurrentStep = WorkflowStep.Analysis;
            var analysisStepId = Guid.NewGuid();

            var analysisResult = await RunAgentAsync(
                "Analyst",
                analysisStepId,
                async () =>
                {
                    var response = await agents.Analyst.RunAsync<AnalysisPayload>(
                        SerializeInput(new
                        {
                            WorkflowId = state.WorkflowId,
                            StepId = analysisStepId,
                            Research = researchResult
                        }),
                        options: runOptions,
                        cancellationToken: cancellationToken);

                    var payload = response.Result;

                    return new AnalysisResult
                    {
                        Id = Guid.NewGuid(),
                        WorkflowId = state.WorkflowId,
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

            state.Results.Add(analysisResult);
            AddStageOutput(stageOutputs, "Analyst", analysisResult);

            state.CurrentStep = WorkflowStep.FactCheck;
            var factCheckStepId = Guid.NewGuid();

            var factCheckResult = await RunAgentAsync(
                "FactChecker",
                factCheckStepId,
                async () =>
                {
                    var response = await agents.FactChecker.RunAsync<FactCheckPayload>(
                        SerializeInput(new
                        {
                            WorkflowId = state.WorkflowId,
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
                        WorkflowId = state.WorkflowId,
                        StepId = factCheckStepId,
                        ProducedByAgent = "FactChecker",
                        ProducedAt = DateTimeOffset.UtcNow,
                        ParentResultIds = [researchResult.Id, analysisResult.Id],
                        Items = payload.Items
                    };
                });

            state.Results.Add(factCheckResult);
            AddStageOutput(stageOutputs, "FactChecker", factCheckResult);

            state.CurrentStep = WorkflowStep.Critic;
            var criticStepId = Guid.NewGuid();

            var criticResult = await RunAgentAsync(
                "Critic",
                criticStepId,
                async () =>
                {
                    var response = await agents.Critic.RunAsync<CriticPayload>(
                        SerializeInput(new
                        {
                            WorkflowId = state.WorkflowId,
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
                        WorkflowId = state.WorkflowId,
                        StepId = criticStepId,
                        ProducedByAgent = "Critic",
                        ProducedAt = DateTimeOffset.UtcNow,
                        ParentResultIds = [analysisResult.Id, factCheckResult.Id],
                        Issues = payload.Issues,
                        HasBlockingIssues = payload.HasBlockingIssues
                    };
                });

            state.Results.Add(criticResult);
            AddStageOutput(stageOutputs, "Critic", criticResult);

            state.CurrentStep = WorkflowStep.Editing;

            var editorInput = new EditorInput
            {
                WorkflowId = state.WorkflowId,
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
                        WorkflowId = state.WorkflowId,
                        StepId = editorInput.StepId,
                        ProducedByAgent = "Editor",
                        ProducedAt = DateTimeOffset.UtcNow,
                        ParentResultIds = [analysisResult.Id, factCheckResult.Id, criticResult.Id],
                        Content = payload.Content
                    };
                });

            state.Results.Add(editorResult);
            state.Status = WorkflowStatus.Completed;

            AddStageOutput(stageOutputs, "Editor", editorResult);

            _logger.LogInformation(
                "Analytical workflow {WorkflowId} completed for topic {Topic}",
                state.WorkflowId,
                request.Topic);

            return new WorkflowResult(editorResult.Content, stageOutputs);
        }
        catch (Exception exception)
        {
            state.Status = WorkflowStatus.Failed;
            state.Error = exception.Message;

            _logger.LogError(
                exception,
                "Analytical workflow {WorkflowId} failed at step {WorkflowStep}",
                state.WorkflowId,
                state.CurrentStep);

            throw;
        }
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
