using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InsightFlow.App.Agents;
using InsightFlow.App.Configuration;
using InsightFlow.App.Contracts;
using InsightFlow.App.Persistence;
using InsightFlow.App.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InsightFlow.App.Orchestration;

public sealed class ResearchWorkflow
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AgentFactory _agentFactory;
    private readonly WorkflowOptions _options;
    private readonly OpenAIOptions _openAIOptions;
    private readonly IDbContextFactory<InsightFlowDbContext> _dbContextFactory;
    private readonly ILogger<ResearchWorkflow> _logger;

    public ResearchWorkflow(
        AgentFactory agentFactory,
        IOptions<WorkflowOptions> options,
        IOptions<OpenAIOptions> openAIOptions,
        IDbContextFactory<InsightFlowDbContext> dbContextFactory,
        ILogger<ResearchWorkflow> logger)
    {
        _agentFactory = agentFactory;
        _options = options.Value;
        _openAIOptions = openAIOptions.Value;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public Task<WorkflowResult> RunAsync(
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

        return RunPipelineAsync(
            request,
            state,
            isResume: false,
            cancellationToken);
    }

    public async Task<WorkflowResult> ResumeAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var execution = await db.WorkflowExecutions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.WorkflowId == workflowId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Workflow '{workflowId}' was not found.");

        if (execution.Status == WorkflowStatus.Completed)
        {
            throw new InvalidOperationException(
                $"Workflow '{workflowId}' is already completed.");
        }

        var request = JsonSerializer.Deserialize<ResearchRequest>(
                          execution.RequestJson,
                          s_jsonOptions)
                      ?? throw new InvalidOperationException(
                          $"Stored request for workflow '{workflowId}' is empty.");

        var state = new WorkflowState
        {
            WorkflowId = execution.WorkflowId,
            Status = WorkflowStatus.Running,
            CurrentStep = execution.CurrentStep,
            Error = null
        };

        return await RunPipelineAsync(
            request,
            state,
            isResume: true,
            cancellationToken);
    }

    private async Task<WorkflowResult> RunPipelineAsync(
        ResearchRequest request,
        WorkflowState state,
        bool isResume,
        CancellationToken cancellationToken)
    {
        var agents = _agentFactory.Create();
        var stageOutputs = new List<AgentStageOutput>();

        var runOptions = new Microsoft.Agents.AI.ChatClientAgentRunOptions
        {
            ChatOptions = new Microsoft.Extensions.AI.ChatOptions
            {
                MaxOutputTokens = _openAIOptions.MaxOutputTokens
            }
        };

        if (isResume)
        {
            _logger.LogInformation(
                "Resuming analytical workflow {WorkflowId} from step {WorkflowStep} for topic {Topic}",
                state.WorkflowId,
                state.CurrentStep,
                request.Topic);
        }
        else
        {
            _logger.LogInformation(
                "Starting analytical workflow {WorkflowId} for topic {Topic}",
                state.WorkflowId,
                request.Topic);
        }

        await PersistStateAsync(
            state,
            request,
            result: null,
            cancellationToken);

        try
        {
            var researchResult = await ExecuteStepAsync(
                state,
                request,
                WorkflowStep.Research,
                WorkflowStep.Analysis,
                "Researcher",
                async stepId =>
                {
                    var researchInput = new ResearchInput
                    {
                        WorkflowId = state.WorkflowId,
                        StepId = stepId,
                        Request = request
                    };

                    var response = await agents.Researcher.RunAsync<ResearchPayload>(
                        SerializeInput(researchInput),
                        options: runOptions,
                        cancellationToken: cancellationToken);

                    var payload = response.Result;

                    return new ResearchResult
                    {
                        Id = Guid.NewGuid(),
                        WorkflowId = state.WorkflowId,
                        StepId = stepId,
                        ProducedByAgent = "Researcher",
                        ProducedAt = DateTimeOffset.UtcNow,
                        Findings = payload.Findings
                    };
                },
                cancellationToken);

            AddStageOutput(stageOutputs, "Researcher", researchResult);

            var analysisResult = await ExecuteStepAsync(
                state,
                request,
                WorkflowStep.Analysis,
                WorkflowStep.FactCheck,
                "Analyst",
                async stepId =>
                {
                    var response = await agents.Analyst.RunAsync<AnalysisPayload>(
                        SerializeInput(new
                        {
                            WorkflowId = state.WorkflowId,
                            StepId = stepId,
                            Research = researchResult
                        }),
                        options: runOptions,
                        cancellationToken: cancellationToken);

                    var payload = response.Result;

                    return new AnalysisResult
                    {
                        Id = Guid.NewGuid(),
                        WorkflowId = state.WorkflowId,
                        StepId = stepId,
                        ProducedByAgent = "Analyst",
                        ProducedAt = DateTimeOffset.UtcNow,
                        ParentResultIds = [researchResult.Id],
                        Conclusions = payload.Conclusions
                            .Select(conclusion => new AnalysisConclusion(
                                conclusion,
                                [researchResult.Id]))
                            .ToArray()
                    };
                },
                cancellationToken);

            AddStageOutput(stageOutputs, "Analyst", analysisResult);

            var factCheckResult = await ExecuteStepAsync(
                state,
                request,
                WorkflowStep.FactCheck,
                WorkflowStep.Critic,
                "FactChecker",
                async stepId =>
                {
                    var response = await agents.FactChecker.RunAsync<FactCheckPayload>(
                        SerializeInput(new
                        {
                            WorkflowId = state.WorkflowId,
                            StepId = stepId,
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
                        StepId = stepId,
                        ProducedByAgent = "FactChecker",
                        ProducedAt = DateTimeOffset.UtcNow,
                        ParentResultIds = [researchResult.Id, analysisResult.Id],
                        Items = payload.Items
                    };
                },
                cancellationToken);

            AddStageOutput(stageOutputs, "FactChecker", factCheckResult);

            var criticResult = await ExecuteStepAsync(
                state,
                request,
                WorkflowStep.Critic,
                WorkflowStep.Editing,
                "Critic",
                async stepId =>
                {
                    var response = await agents.Critic.RunAsync<CriticPayload>(
                        SerializeInput(new
                        {
                            WorkflowId = state.WorkflowId,
                            StepId = stepId,
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
                        StepId = stepId,
                        ProducedByAgent = "Critic",
                        ProducedAt = DateTimeOffset.UtcNow,
                        ParentResultIds = [analysisResult.Id, factCheckResult.Id],
                        Issues = payload.Issues,
                        HasBlockingIssues = payload.HasBlockingIssues
                    };
                },
                cancellationToken);

            AddStageOutput(stageOutputs, "Critic", criticResult);

            var editorResult = await ExecuteStepAsync(
                state,
                request,
                WorkflowStep.Editing,
                nextStep: null,
                "Editor",
                async stepId =>
                {
                    var editorInput = new EditorInput
                    {
                        WorkflowId = state.WorkflowId,
                        StepId = stepId,
                        Analysis = analysisResult,
                        FactCheck = factCheckResult,
                        Critic = criticResult
                    };

                    var response = await agents.Editor.RunAsync<EditorPayload>(
                        SerializeInput(editorInput),
                        options: runOptions,
                        cancellationToken: cancellationToken);

                    var payload = response.Result;

                    return new EditorResult
                    {
                        Id = Guid.NewGuid(),
                        WorkflowId = state.WorkflowId,
                        StepId = stepId,
                        ProducedByAgent = "Editor",
                        ProducedAt = DateTimeOffset.UtcNow,
                        ParentResultIds = [analysisResult.Id, factCheckResult.Id, criticResult.Id],
                        Content = payload.Content
                    };
                },
                cancellationToken);

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

            await PersistStateAsync(
                state,
                request,
                result: null,
                CancellationToken.None);

            _logger.LogError(
                exception,
                "Analytical workflow {WorkflowId} failed at step {WorkflowStep}",
                state.WorkflowId,
                state.CurrentStep);

            throw;
        }
    }

    private async Task<T> ExecuteStepAsync<T>(
        WorkflowState state,
        ResearchRequest request,
        WorkflowStep step,
        WorkflowStep? nextStep,
        string agentName,
        Func<Guid, Task<T>> action,
        CancellationToken cancellationToken)
        where T : BaseAgentResult
    {
        var stepId = CreateStepId(state.WorkflowId, step);

        var existingResult = await TryLoadResultAsync<T>(
            state.WorkflowId,
            stepId,
            cancellationToken);

        if (existingResult is not null)
        {
            _logger.LogInformation(
                "Skipping agent {AgentName}. Result already exists for WorkflowId {WorkflowId}, StepId {StepId}",
                agentName,
                state.WorkflowId,
                stepId);

            state.Results.Add(existingResult);
            ApplyStepCompletion(state, nextStep);

            await PersistStateAsync(
                state,
                request,
                result: null,
                cancellationToken);

            return existingResult;
        }

        var result = await RunAgentAsync(
            agentName,
            stepId,
            () => action(stepId));

        state.Results.Add(result);
        ApplyStepCompletion(state, nextStep);

        await PersistStateAsync(
            state,
            request,
            result,
            cancellationToken);

        return result;
    }

    private async Task<T?> TryLoadResultAsync<T>(
        Guid workflowId,
        Guid stepId,
        CancellationToken cancellationToken)
        where T : BaseAgentResult
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.AgentResults
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.WorkflowId == workflowId && x.StepId == stepId,
                cancellationToken);

        if (entity is null)
        {
            return null;
        }

        if (!string.Equals(
                entity.ResultType,
                typeof(T).Name,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Stored result type '{entity.ResultType}' does not match expected type '{typeof(T).Name}'.");
        }

        return JsonSerializer.Deserialize<T>(
                   entity.PayloadJson,
                   s_jsonOptions)
               ?? throw new InvalidOperationException(
                   $"Stored result for WorkflowId '{workflowId}', StepId '{stepId}' is empty.");
    }

    private static void ApplyStepCompletion(
        WorkflowState state,
        WorkflowStep? nextStep)
    {
        if (nextStep is null)
        {
            state.Status = WorkflowStatus.Completed;
            return;
        }

        state.CurrentStep = nextStep.Value;
    }

    private static Guid CreateStepId(
        Guid workflowId,
        WorkflowStep step)
    {
        var source = Encoding.UTF8.GetBytes($"{workflowId:N}:{step}");
        var hash = SHA256.HashData(source);

        return new Guid(hash.AsSpan(0, 16));
    }

    private async Task PersistStateAsync(
        WorkflowState state,
        ResearchRequest request,
        BaseAgentResult? result,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var execution = await db.WorkflowExecutions.FindAsync(
            [state.WorkflowId],
            cancellationToken);

        var now = DateTimeOffset.UtcNow;

        if (execution is null)
        {
            execution = new WorkflowExecutionEntity
            {
                WorkflowId = state.WorkflowId,
                Topic = request.Topic,
                RequestJson = SerializeInput(request),
                Status = state.Status,
                CurrentStep = state.CurrentStep,
                Error = state.Error,
                StartedAt = now,
                UpdatedAt = now
            };

            db.WorkflowExecutions.Add(execution);
        }
        else
        {
            execution.Topic = request.Topic;
            execution.RequestJson = SerializeInput(request);
            execution.Status = state.Status;
            execution.CurrentStep = state.CurrentStep;
            execution.Error = state.Error;
            execution.UpdatedAt = now;

            if (state.Status == WorkflowStatus.Completed)
            {
                execution.CompletedAt = now;
            }
        }

        if (result is not null)
        {
            db.AgentResults.Add(new AgentResultEntity
            {
                Id = result.Id,
                WorkflowId = result.WorkflowId,
                StepId = result.StepId,
                ProducedByAgent = result.ProducedByAgent,
                ProducedAt = result.ProducedAt,
                ResultType = result.GetType().Name,
                PayloadJson = JsonSerializer.Serialize(
                    result,
                    result.GetType(),
                    s_jsonOptions),
                ParentResultIdsJson = JsonSerializer.Serialize(
                    result.ParentResultIds,
                    s_jsonOptions)
            });
        }

        await db.SaveChangesAsync(cancellationToken);
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
