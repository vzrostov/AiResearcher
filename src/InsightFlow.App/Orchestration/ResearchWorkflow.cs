using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InsightFlow.App.Agents;
using InsightFlow.App.Configuration;
using InsightFlow.App.Contracts;
using InsightFlow.App.Persistence;
using InsightFlow.App.Persistence.Entities;
using InsightFlow.App.Quality;
using InsightFlow.App.Logging;
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
    private readonly QualityChecker _qualityChecker;
    private readonly WorkflowDiagramWriter _workflowDiagramWriter;
    private readonly ILogger<ResearchWorkflow> _logger;

    public ResearchWorkflow(
        AgentFactory agentFactory,
        IOptions<WorkflowOptions> options,
        IOptions<OpenAIOptions> openAIOptions,
        IDbContextFactory<InsightFlowDbContext> dbContextFactory,
        QualityChecker qualityChecker,
        WorkflowDiagramWriter workflowDiagramWriter,
        ILogger<ResearchWorkflow> logger)
    {
        _agentFactory = agentFactory;
        _options = options.Value;
        _openAIOptions = openAIOptions.Value;
        _dbContextFactory = dbContextFactory;
        _qualityChecker = qualityChecker;
        _workflowDiagramWriter = workflowDiagramWriter;
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
                    ValidateResearchPayload(payload);

                    var sources = payload.Sources
                        .Select(source => new SourceReference(
                            Guid.NewGuid(),
                            source.Url,
                            source.Title,
                            source.PublishedAt,
                            DateTimeOffset.UtcNow))
                        .ToArray();

                    var sourceByKey = payload.Sources
                        .Select((source, index) => new { source.Key, Source = sources[index] })
                        .ToDictionary(x => x.Key, x => x.Source, StringComparer.Ordinal);

                    var findings = payload.Findings
                        .Select(finding => new ResearchFinding(
                            Guid.NewGuid(),
                            finding.Claim,
                            finding.Evidence,
                            finding.SourceKeys
                                .Select(key => sourceByKey[key].Id)
                                .Distinct()
                                .ToArray(),
                            finding.Confidence))
                        .ToArray();

                    return new ResearchResult
                    {
                        Id = Guid.NewGuid(),
                        WorkflowId = state.WorkflowId,
                        StepId = stepId,
                        ProducedByAgent = "Researcher",
                        ProducedAt = DateTimeOffset.UtcNow,
                        Sources = sources,
                        Findings = findings
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
                    ValidateAnalysisPayload(payload, researchResult);

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
                                Guid.NewGuid(),
                                conclusion.Conclusion,
                                conclusion.SupportingFindingIds.Distinct().ToArray()))
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
                    ValidateFactCheckPayload(payload, analysisResult);

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
                            Research = researchResult,
                            Analysis = analysisResult,
                            FactCheck = factCheckResult
                        }),
                        options: runOptions,
                        cancellationToken: cancellationToken);

                    var payload = response.Result;
                    ValidateCriticPayload(payload, researchResult, analysisResult);

                    return new CriticResult
                    {
                        Id = Guid.NewGuid(),
                        WorkflowId = state.WorkflowId,
                        StepId = stepId,
                        ProducedByAgent = "Critic",
                        ProducedAt = DateTimeOffset.UtcNow,
                        ParentResultIds = [researchResult.Id, analysisResult.Id, factCheckResult.Id],
                        Issues = payload.Issues,
                        Conflicts = payload.Conflicts,
                        HasBlockingIssues = payload.HasBlockingIssues
                    };
                },
                cancellationToken);

            AddStageOutput(stageOutputs, "Critic", criticResult);

            var qualityCheckerResult = _qualityChecker.Evaluate(
                researchResult,
                analysisResult,
                factCheckResult,
                criticResult);

            AddStageOutput(stageOutputs, "QualityChecker", qualityCheckerResult);

            if (qualityCheckerResult.Decision == QualityCheckerDecision.Reject)
            {
                state.Status = WorkflowStatus.Completed;

                await PersistStateAsync(
                    state,
                    request,
                    result: null,
                    cancellationToken);

                _logger.LogWarning(
                    "Analytical workflow {WorkflowId} was rejected by QualityChecker: {Reasons}",
                    state.WorkflowId,
                    string.Join("; ", qualityCheckerResult.Reasons));

                await _workflowDiagramWriter.WriteAsync(
                    state.WorkflowId,
                    request,
                    researchResult,
                    analysisResult,
                    factCheckResult,
                    criticResult,
                    qualityCheckerResult,
                    editorResult: null,
                    cancellationToken);

                return new WorkflowResult(
                    BuildQualityCheckerRejection(qualityCheckerResult),
                    stageOutputs);
            }

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
                        Critic = criticResult,
                        QualityChecker = qualityCheckerResult
                    };

                    var response = await agents.Editor.RunAsync<EditorPayload>(
                        SerializeInput(editorInput),
                        options: runOptions,
                        cancellationToken: cancellationToken);

                    var payload = response.Result;
                    ValidateEditorPayload(payload);

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

            await _workflowDiagramWriter.WriteAsync(
                state.WorkflowId,
                request,
                researchResult,
                analysisResult,
                factCheckResult,
                criticResult,
                qualityCheckerResult,
                editorResult,
                cancellationToken);

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

    private static void ValidateResearchPayload(ResearchPayload? payload)
    {
        if (payload?.Findings is null || payload.Findings.Count == 0)
        {
            throw new InvalidOperationException(
                "Researcher returned no findings.");
        }

        if (payload.Sources is null)
        {
            throw new InvalidOperationException(
                "Researcher returned null sources.");
        }

        foreach (var source in payload.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.Key))
            {
                throw new InvalidOperationException(
                    "Researcher returned a source with an empty key.");
            }

            if (string.IsNullOrWhiteSpace(source.Url) &&
                string.IsNullOrWhiteSpace(source.Title))
            {
                throw new InvalidOperationException(
                    $"Researcher source '{source.Key}' has neither URL nor title.");
            }
        }

        var duplicateSourceKey = payload.Sources
            .GroupBy(source => source.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateSourceKey is not null)
        {
            throw new InvalidOperationException(
                $"Researcher returned duplicate source key '{duplicateSourceKey.Key}'.");
        }

        var sourceKeys = payload.Sources
            .Select(source => source.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var finding in payload.Findings)
        {
            if (string.IsNullOrWhiteSpace(finding.Claim))
            {
                throw new InvalidOperationException(
                    "Researcher returned a finding with an empty claim.");
            }

            if (string.IsNullOrWhiteSpace(finding.Evidence))
            {
                throw new InvalidOperationException(
                    "Researcher returned a finding with empty evidence.");
            }

            if (finding.SourceKeys is null)
            {
                throw new InvalidOperationException(
                    "Researcher returned null source keys for a finding.");
            }

            if (finding.SourceKeys.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException(
                    "Researcher finding contains an empty source key.");
            }

            var unknownSource = finding.SourceKeys
                .FirstOrDefault(key => !sourceKeys.Contains(key));

            if (unknownSource is not null)
            {
                throw new InvalidOperationException(
                    $"Researcher finding references unknown source key '{unknownSource}'.");
            }

            if (finding.Confidence is < 0 or > 1)
            {
                throw new InvalidOperationException(
                    "Researcher returned a confidence value outside the range 0..1.");
            }
        }
    }

    private static void ValidateAnalysisPayload(
        AnalysisPayload? payload,
        ResearchResult researchResult)
    {
        if (payload?.Conclusions is null || payload.Conclusions.Count == 0)
        {
            throw new InvalidOperationException(
                "Analyst returned no conclusions.");
        }

        var findingIds = researchResult.Findings
            .Select(finding => finding.Id)
            .ToHashSet();

        foreach (var conclusion in payload.Conclusions)
        {
            if (string.IsNullOrWhiteSpace(conclusion.Conclusion))
            {
                throw new InvalidOperationException(
                    "Analyst returned an empty conclusion.");
            }

            if (conclusion.SupportingFindingIds is null ||
                conclusion.SupportingFindingIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "Analyst returned a conclusion without supporting findings.");
            }

            if (conclusion.SupportingFindingIds.Any(id => !findingIds.Contains(id)))
            {
                throw new InvalidOperationException(
                    "Analyst referenced an unknown finding.");
            }
        }
    }

    private static void ValidateFactCheckPayload(
        FactCheckPayload? payload,
        AnalysisResult analysisResult)
    {
        if (payload?.Items is null || payload.Items.Count == 0)
        {
            throw new InvalidOperationException(
                "FactChecker returned no items.");
        }

        var conclusionIds = analysisResult.Conclusions
            .Select(conclusion => conclusion.Id)
            .ToHashSet();

        foreach (var item in payload.Items)
        {
            if (item.ConclusionId == Guid.Empty ||
                !conclusionIds.Contains(item.ConclusionId))
            {
                throw new InvalidOperationException(
                    $"FactChecker referenced unknown conclusion '{item.ConclusionId}'.");
            }

            if (string.IsNullOrWhiteSpace(item.Claim))
            {
                throw new InvalidOperationException(
                    "FactChecker returned an item with an empty claim.");
            }

            if (!Enum.IsDefined(item.Status))
            {
                throw new InvalidOperationException(
                    $"FactChecker returned an unknown status '{item.Status}'.");
            }
        }

        var duplicateConclusion = payload.Items
            .GroupBy(item => item.ConclusionId)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateConclusion is not null)
        {
            throw new InvalidOperationException(
                $"FactChecker checked conclusion '{duplicateConclusion.Key}' more than once.");
        }

        var checkedConclusionIds = payload.Items
            .Select(item => item.ConclusionId)
            .ToHashSet();

        var missingConclusionIds = conclusionIds
            .Except(checkedConclusionIds)
            .ToArray();

        if (missingConclusionIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"FactChecker did not check {missingConclusionIds.Length} conclusion(s): " +
                string.Join(", ", missingConclusionIds));
        }

        if (payload.Items.Count != analysisResult.Conclusions.Count)
        {
            throw new InvalidOperationException(
                "FactChecker must return exactly one item for every analytical conclusion.");
        }
    }

    private static void ValidateCriticPayload(
        CriticPayload? payload,
        ResearchResult researchResult,
        AnalysisResult analysisResult)
    {
        if (payload is null)
        {
            throw new InvalidOperationException(
                "Critic returned an empty result.");
        }

        if (payload.Issues is null)
        {
            throw new InvalidOperationException(
                "Critic returned null issues.");
        }

        if (payload.Conflicts is null)
        {
            throw new InvalidOperationException(
                "Critic returned null conflicts.");
        }

        foreach (var issue in payload.Issues)
        {
            if (string.IsNullOrWhiteSpace(issue.Description))
            {
                throw new InvalidOperationException(
                    "Critic returned an issue with an empty description.");
            }

            if (!Enum.IsDefined(issue.Severity))
            {
                throw new InvalidOperationException(
                    $"Critic returned an unknown severity '{issue.Severity}'.");
            }
        }

        var findingIds = researchResult.Findings
            .Select(finding => finding.Id)
            .ToHashSet();
        var conclusionIds = analysisResult.Conclusions
            .Select(conclusion => conclusion.Id)
            .ToHashSet();

        foreach (var conflict in payload.Conflicts)
        {
            if (string.IsNullOrWhiteSpace(conflict.Description))
            {
                throw new InvalidOperationException(
                    "Critic returned a conflict with an empty description.");
            }

            if (!Enum.IsDefined(conflict.Severity) ||
                !Enum.IsDefined(conflict.Status))
            {
                throw new InvalidOperationException(
                    "Critic returned an unknown conflict severity or status.");
            }

            if (conflict.FindingIds is null || conflict.ConclusionIds is null)
            {
                throw new InvalidOperationException(
                    "Critic conflict returned null finding/conclusion references.");
            }

            if (conflict.FindingIds.Count + conflict.ConclusionIds.Count < 2)
            {
                throw new InvalidOperationException(
                    "Critic conflict must reference at least two findings/conclusions.");
            }

            if (conflict.FindingIds.Any(id => !findingIds.Contains(id)))
            {
                throw new InvalidOperationException(
                    "Critic conflict references an unknown finding.");
            }

            if (conflict.ConclusionIds.Any(id => !conclusionIds.Contains(id)))
            {
                throw new InvalidOperationException(
                    "Critic conflict references an unknown conclusion.");
            }
        }

        var hasBlockingIssue = payload.Issues.Any(
            issue => issue.Severity == CriticSeverity.Blocking);

        var hasBlockingConflict = payload.Conflicts.Any(
            conflict => conflict.Severity == CriticSeverity.Blocking &&
                        conflict.Status != ConflictStatus.Resolved);

        if (payload.HasBlockingIssues != (hasBlockingIssue || hasBlockingConflict))
        {
            throw new InvalidOperationException(
                "Critic returned inconsistent HasBlockingIssues value.");
        }
    }

    private static void ValidateEditorPayload(EditorPayload? payload)
    {
        if (string.IsNullOrWhiteSpace(payload?.Content))
        {
            throw new InvalidOperationException(
                "Editor returned empty content.");
        }
    }

    private static string BuildQualityCheckerRejection(QualityCheckerResult result) =>
        "QualityChecker rejected the result:" + Environment.NewLine +
        string.Join(Environment.NewLine, result.Reasons.Select(reason => $"- {reason}"));

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

    private sealed record ResearchPayload(
        List<ResearchSourcePayload> Sources,
        List<ResearchFindingPayload> Findings);

    private sealed record ResearchSourcePayload(
        string Key,
        string? Url,
        string? Title,
        DateTimeOffset? PublishedAt);

    private sealed record ResearchFindingPayload(
        string Claim,
        string Evidence,
        List<string> SourceKeys,
        double? Confidence);

    private sealed record AnalysisPayload(
        List<AnalysisConclusionPayload> Conclusions);

    private sealed record AnalysisConclusionPayload(
        string Conclusion,
        List<Guid> SupportingFindingIds);

    private sealed record FactCheckPayload(List<FactCheckItem> Items);

    private sealed record CriticPayload(
        List<CriticIssue> Issues,
        List<EvidenceConflict> Conflicts,
        bool HasBlockingIssues);

    private sealed record EditorPayload(string Content);
}
