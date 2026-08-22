using System.Text;
using InsightFlow.App.Contracts;

namespace InsightFlow.App.Logging;

public sealed class WorkflowDiagramWriter
{
    private readonly string _logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

    public Task StartAsync(
        Guid workflowId,
        ResearchRequest request,
        CancellationToken cancellationToken)
    {
        var topic = request.Topic.ReplaceLineEndings(" ");

        return AppendIfMissingAsync(
            workflowId,
            marker: "USER",
            $"USER{Environment.NewLine}" +
            $" │{Environment.NewLine}" +
            $" │ 1 request {topic}{Environment.NewLine}" +
            $" ▼{Environment.NewLine}",
            cancellationToken);
    }

    public Task ResearchCompletedAsync(
        Guid workflowId,
        ResearchResult result,
        CancellationToken cancellationToken) =>
        AppendIfMissingAsync(
            workflowId,
            marker: "Researcher",
            $"Researcher{Environment.NewLine}" +
            $" │{Environment.NewLine}" +
            $" ├─ {result.Sources.Count} Sources{Environment.NewLine}" +
            $" └─ {result.Findings.Count} Findings{Environment.NewLine}" +
            $"       │{Environment.NewLine}" +
            $"       ▼{Environment.NewLine}",
            cancellationToken);

    public Task AnalysisCompletedAsync(
        Guid workflowId,
        AnalysisResult result,
        CancellationToken cancellationToken) =>
        AppendIfMissingAsync(
            workflowId,
            marker: "Analyst",
            $"Analyst{Environment.NewLine}" +
            $" │{Environment.NewLine}" +
            $" └─ {result.Conclusions.Count} Conclusions{Environment.NewLine}" +
            $"       │{Environment.NewLine}" +
            $"       ▼{Environment.NewLine}",
            cancellationToken);

    public Task FactCheckCompletedAsync(
        Guid workflowId,
        FactCheckResult result,
        CancellationToken cancellationToken) =>
        AppendIfMissingAsync(
            workflowId,
            marker: "FactChecker",
            $"FactChecker{Environment.NewLine}" +
            $" │{Environment.NewLine}" +
            $" └─ {result.Items.Count} FactCheckItems{Environment.NewLine}" +
            $"       │{Environment.NewLine}" +
            $"       ▼{Environment.NewLine}",
            cancellationToken);

    public Task CriticCompletedAsync(
        Guid workflowId,
        CriticResult result,
        CancellationToken cancellationToken) =>
        AppendIfMissingAsync(
            workflowId,
            marker: "Critic",
            $"Critic{Environment.NewLine}" +
            $" │{Environment.NewLine}" +
            $" ├─ {result.Issues.Count} Issues{Environment.NewLine}" +
            $" └─ {result.Conflicts.Count} Conflicts{Environment.NewLine}" +
            $"       │{Environment.NewLine}" +
            $"       ▼{Environment.NewLine}",
            cancellationToken);

    public Task QualityCheckerCompletedAsync(
        Guid workflowId,
        QualityCheckerResult result,
        CancellationToken cancellationToken) =>
        AppendIfMissingAsync(
            workflowId,
            marker: "QualityChecker",
            $"QualityChecker{Environment.NewLine}" +
            $" │{Environment.NewLine}" +
            $" └─ 1 QualityCheckerResult ({result.Decision}){Environment.NewLine}" +
            $"       │{Environment.NewLine}" +
            $"       ▼{Environment.NewLine}",
            cancellationToken);

    public Task EditorCompletedAsync(
        Guid workflowId,
        CancellationToken cancellationToken) =>
        AppendIfMissingAsync(
            workflowId,
            marker: "Editor",
            $"Editor{Environment.NewLine}" +
            $" │{Environment.NewLine}" +
            $" └─ 1 EditorResult{Environment.NewLine}" +
            $"       │{Environment.NewLine}" +
            $"       ▼{Environment.NewLine}" +
            $"FINAL REPORT{Environment.NewLine}",
            cancellationToken);

    public Task RejectedAsync(
        Guid workflowId,
        CancellationToken cancellationToken) =>
        AppendIfMissingAsync(
            workflowId,
            marker: "REJECTED",
            $"REJECTED{Environment.NewLine}",
            cancellationToken);

    private async Task AppendIfMissingAsync(
        Guid workflowId,
        string marker,
        string text,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_logsDirectory);

        var path = GetPath(workflowId);

        if (File.Exists(path))
        {
            var existing = await File.ReadAllTextAsync(
                path,
                Encoding.UTF8,
                cancellationToken);

            if (ContainsLine(existing, marker))
            {
                return;
            }
        }

        await File.AppendAllTextAsync(
            path,
            text,
            Encoding.UTF8,
            cancellationToken);
    }

    private string GetPath(Guid workflowId) =>
        Path.Combine(
            _logsDirectory,
            $"workflow-{workflowId:N}.txt");

    private static bool ContainsLine(string text, string marker) =>
        text.Split(
                ["\r\n", "\n"],
                StringSplitOptions.None)
            .Any(line => string.Equals(line, marker, StringComparison.Ordinal));
}
