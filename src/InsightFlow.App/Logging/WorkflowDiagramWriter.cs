using System.Text;
using InsightFlow.App.Contracts;

namespace InsightFlow.App.Logging;

public sealed class WorkflowDiagramWriter
{
    private readonly string _logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

    public async Task WriteAsync(
        Guid workflowId,
        ResearchRequest request,
        ResearchResult research,
        AnalysisResult analysis,
        FactCheckResult factCheck,
        CriticResult critic,
        QualityCheckerResult qualityChecker,
        EditorResult? editorResult,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_logsDirectory);

        var topic = request.Topic.ReplaceLineEndings(" ");
        var builder = new StringBuilder();

        builder.AppendLine("USER");
        builder.AppendLine(" │");
        builder.AppendLine($" │ 1 request {topic}");
        builder.AppendLine(" ▼");
        builder.AppendLine("Researcher");
        builder.AppendLine(" │");
        builder.AppendLine($" ├─ {research.Sources.Count} Sources");
        builder.AppendLine($" └─ {research.Findings.Count} Findings");
        builder.AppendLine("       │");
        builder.AppendLine("       ▼");
        builder.AppendLine("Analyst");
        builder.AppendLine(" │");
        builder.AppendLine($" └─ {analysis.Conclusions.Count} Conclusions");
        builder.AppendLine("       │");
        builder.AppendLine("       ▼");
        builder.AppendLine("FactChecker");
        builder.AppendLine(" │");
        builder.AppendLine($" └─ {factCheck.Items.Count} FactCheckItems");
        builder.AppendLine("       │");
        builder.AppendLine("       ▼");
        builder.AppendLine("Critic");
        builder.AppendLine(" │");
        builder.AppendLine($" ├─ {critic.Issues.Count} Issues");
        builder.AppendLine($" └─ {critic.Conflicts.Count} Conflicts");
        builder.AppendLine("       │");
        builder.AppendLine("       ▼");
        builder.AppendLine("QualityChecker");
        builder.AppendLine(" │");
        builder.AppendLine($" └─ 1 QualityCheckerResult ({qualityChecker.Decision})");
        builder.AppendLine("       │");
        builder.AppendLine("       ▼");

        if (editorResult is null)
        {
            builder.AppendLine("REJECTED");
        }
        else
        {
            builder.AppendLine("Editor");
            builder.AppendLine(" │");
            builder.AppendLine(" └─ 1 EditorResult");
            builder.AppendLine("       │");
            builder.AppendLine("       ▼");
            builder.AppendLine("FINAL REPORT");
        }

        var path = Path.Combine(
            _logsDirectory,
            $"workflow-{workflowId:N}.txt");

        await File.WriteAllTextAsync(
            path,
            builder.ToString(),
            Encoding.UTF8,
            cancellationToken);
    }
}
