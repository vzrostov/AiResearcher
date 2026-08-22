using InsightFlow.App.Configuration;
using InsightFlow.App.Contracts;
using Microsoft.Extensions.Options;

namespace InsightFlow.App.Quality;

public sealed class QualityChecker
{
    private readonly QualityCheckerOptions _options;

    public QualityChecker(IOptions<QualityCheckerOptions> options)
    {
        _options = options.Value;

        if (_options.MinimumSourceCoverage is < 0 or > 1)
        {
            throw new InvalidOperationException(
                "QualityChecker MinimumSourceCoverage must be in the range 0..1.");
        }
    }

    public QualityCheckerResult Evaluate(
        ResearchResult research,
        AnalysisResult analysis,
        FactCheckResult factCheck,
        CriticResult critic)
    {
        var rejectReasons = new List<string>();
        var warningReasons = new List<string>();

        if (critic.Issues.Any(issue => issue.Severity == CriticSeverity.Blocking))
        {
            rejectReasons.Add("Critic reported a blocking issue.");
        }

        if (critic.Conflicts.Any(conflict =>
                conflict.Severity == CriticSeverity.Blocking &&
                conflict.Status != ConflictStatus.Resolved))
        {
            rejectReasons.Add("A blocking evidence conflict is unresolved.");
        }

        var verifiedCount = factCheck.Items.Count(
            item => item.Status == FactCheckStatus.Verified);

        if (_options.RequireVerifiedClaims && verifiedCount == 0)
        {
            rejectReasons.Add("No analytical conclusion was verified.");
        }

        var sourceCoverage = CalculateSourceCoverage(research, analysis);

        if (sourceCoverage < _options.MinimumSourceCoverage)
        {
            warningReasons.Add(
                $"Source coverage {sourceCoverage:P0} is below the configured minimum {_options.MinimumSourceCoverage:P0}.");
        }

        if (factCheck.Items.Any(item => item.Status == FactCheckStatus.Unsupported))
        {
            warningReasons.Add("Fact-check contains unsupported conclusions.");
        }

        if (factCheck.Items.Any(item => item.Status == FactCheckStatus.Contradicted))
        {
            warningReasons.Add("Fact-check contains contradicted conclusions.");
        }

        if (critic.Conflicts.Any(conflict =>
                conflict.Severity != CriticSeverity.Blocking &&
                conflict.Status != ConflictStatus.Resolved))
        {
            warningReasons.Add("Non-blocking evidence conflicts remain unresolved.");
        }

        if (rejectReasons.Count > 0)
        {
            return new QualityCheckerResult(
                QualityCheckerDecision.Reject,
                sourceCoverage,
                [.. rejectReasons, .. warningReasons]);
        }

        if (warningReasons.Count > 0)
        {
            return new QualityCheckerResult(
                QualityCheckerDecision.Warning,
                sourceCoverage,
                warningReasons);
        }

        return new QualityCheckerResult(
            QualityCheckerDecision.Pass,
            sourceCoverage,
            []);
    }

    private static double CalculateSourceCoverage(
        ResearchResult research,
        AnalysisResult analysis)
    {
        if (analysis.Conclusions.Count == 0)
        {
            return 0;
        }

        var findingsById = research.Findings.ToDictionary(finding => finding.Id);

        var sourcedConclusions = analysis.Conclusions.Count(conclusion =>
            conclusion.SupportingFindingIds.Any(findingId =>
                findingsById.TryGetValue(findingId, out var finding) &&
                finding.SourceIds.Count > 0));

        return (double)sourcedConclusions / analysis.Conclusions.Count;
    }
}
