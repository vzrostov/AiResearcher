using System.Text.Json.Serialization;

namespace InsightFlow.Evals.Microsoft;

public sealed record EvalCase(
    string Id,
    string Category,
    string Input,
    [property: JsonPropertyName("expected_summary")] string ExpectedSummary,
    [property: JsonPropertyName("grounding_context")] string GroundingContext,
    [property: JsonPropertyName("required_phrases_any")] string[] RequiredPhrasesAny,
    [property: JsonPropertyName("forbidden_phrases")] string[] ForbiddenPhrases,
    bool Critical);

public sealed record ThresholdConfig(
    [property: JsonPropertyName("min_scores")] Dictionary<string, double> MinScores,
    [property: JsonPropertyName("max_regression")] Dictionary<string, double> MaxRegression,
    [property: JsonPropertyName("critical_cases_must_pass")] bool CriticalCasesMustPass);

public sealed record CaseReport(
    string Id,
    bool HardPass,
    Dictionary<string, double> Metrics,
    string? Error);

public sealed record EvalReport(
    DateTimeOffset CreatedAt,
    string Dataset,
    Dictionary<string, double> Metrics,
    IReadOnlyList<CaseReport> Cases,
    bool Passed,
    IReadOnlyList<string> Failures);

public sealed record Baseline(
    string Dataset,
    Dictionary<string, double> Metrics);
