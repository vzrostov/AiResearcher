using System.Text.Json;
using InsightFlow.Evals.Microsoft;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using OpenAI;

#pragma warning disable AIEVAL001

var updateBaseline = args.Contains(
    "--update-baseline",
    StringComparer.OrdinalIgnoreCase);

var baseDirectory = AppContext.BaseDirectory;
var datasetPath = Path.Combine(baseDirectory, "datasets", "core.json");
var thresholdsPath = Path.Combine(baseDirectory, "eval-thresholds.json");
var baselinePath = Path.Combine(baseDirectory, "baseline.json");
var reportPath = Path.Combine(baseDirectory, "report.json");

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true
};

var cases = JsonSerializer.Deserialize<List<EvalCase>>(
    await File.ReadAllTextAsync(datasetPath),
    jsonOptions) ?? [];

var thresholds = JsonSerializer.Deserialize<ThresholdConfig>(
    await File.ReadAllTextAsync(thresholdsPath),
    jsonOptions) ?? throw new InvalidOperationException("Thresholds are missing.");

var baseline = JsonSerializer.Deserialize<Baseline>(
    await File.ReadAllTextAsync(baselinePath),
    jsonOptions) ?? new Baseline("core", []);

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("OPENAI_API_KEY is required.");

var evalModel = Environment.GetEnvironmentVariable("EVAL_MODEL")
    ?? "gpt-5-mini";

var insightFlowProject = Environment.GetEnvironmentVariable("INSIGHTFLOW_PROJECT")
    ?? Path.GetFullPath(
        Path.Combine(
            baseDirectory,
            "..", "..", "..", "..", "..",
            "src", "InsightFlow.App", "InsightFlow.App.csproj"));

IChatClient evalClient =
    new OpenAIClient(apiKey)
        .GetChatClient(evalModel)
        .AsIChatClient();

var chatConfiguration = new ChatConfiguration(evalClient);
var runner = new InsightFlowRunner(insightFlowProject);

IEvaluator relevanceEvaluator = new RelevanceEvaluator();
IEvaluator groundednessEvaluator = new GroundednessEvaluator();
IEvaluator completenessEvaluator = new CompletenessEvaluator();
IEvaluator consistencyEvaluator = new CoherenceEvaluator();
IEvaluator taskAdherenceEvaluator = new TaskAdherenceEvaluator();

var caseReports = new List<CaseReport>();

foreach (var testCase in cases)
{
    Console.WriteLine($"Running {testCase.Id}...");

    try
    {
        var answer = await runner.RunAsync(testCase.Input, CancellationToken.None);

        var hardPass = EvaluateHardPass(testCase, answer);

        var relevance = await relevanceEvaluator.EvaluateAsync(
            testCase.Input,
            answer,
            chatConfiguration);

        var groundedness = await groundednessEvaluator.EvaluateAsync(
            testCase.Input,
            answer,
            chatConfiguration,
            additionalContext:
            [
                new GroundednessEvaluatorContext(testCase.GroundingContext)
            ]);

        var completeness = await completenessEvaluator.EvaluateAsync(
            testCase.Input,
            answer,
            chatConfiguration,
            additionalContext:
            [
                new CompletenessEvaluatorContext(testCase.ExpectedSummary)
            ]);

        var consistency = await consistencyEvaluator.EvaluateAsync(
            testCase.Input,
            answer,
            chatConfiguration);

        var taskAdherence = await taskAdherenceEvaluator.EvaluateAsync(
            testCase.Input,
            answer,
            chatConfiguration);

        var metrics = new Dictionary<string, double>
        {
            ["relevance"] = Normalize(GetFirstNumericMetric(relevance)),
            ["groundedness"] = Normalize(GetFirstNumericMetric(groundedness)),
            ["completeness"] = Normalize(GetFirstNumericMetric(completeness)),
            // Microsoft has Coherence, not a dedicated Consistency evaluator.
            // We intentionally use Coherence as the closest built-in proxy.
            ["consistency"] = Normalize(GetFirstNumericMetric(consistency)),
            ["task_adherence"] = Normalize(GetFirstNumericMetric(taskAdherence))
        };

        caseReports.Add(new CaseReport(
            testCase.Id,
            hardPass,
            metrics,
            Error: null));
    }
    catch (Exception ex)
    {
        caseReports.Add(new CaseReport(
            testCase.Id,
            HardPass: false,
            Metrics: [],
            Error: ex.Message));
    }
}

var aggregate = Aggregate(caseReports);
aggregate["hard_pass_rate"] =
    caseReports.Count == 0
        ? 0
        : caseReports.Count(x => x.HardPass) / (double)caseReports.Count;

var failures = EvaluateQualityGate(
    cases,
    caseReports,
    aggregate,
    thresholds,
    baseline);

var report = new EvalReport(
    DateTimeOffset.UtcNow,
    "core",
    aggregate,
    caseReports,
    Passed: failures.Count == 0,
    Failures: failures);

await File.WriteAllTextAsync(
    reportPath,
    JsonSerializer.Serialize(report, jsonOptions));

PrintReport(report, baseline);

if (updateBaseline)
{
    var newBaseline = new Baseline("core", aggregate);

    await File.WriteAllTextAsync(
        baselinePath,
        JsonSerializer.Serialize(newBaseline, jsonOptions));

    Console.WriteLine();
    Console.WriteLine($"Baseline updated: {baselinePath}");
    return;
}

Environment.ExitCode = report.Passed ? 0 : 1;

static bool EvaluateHardPass(EvalCase testCase, string answer)
{
    if (string.IsNullOrWhiteSpace(answer))
    {
        return false;
    }

    if (testCase.RequiredPhrasesAny.Length > 0 &&
        !testCase.RequiredPhrasesAny.Any(
            phrase => answer.Contains(
                phrase,
                StringComparison.OrdinalIgnoreCase)))
    {
        return false;
    }

    if (testCase.ForbiddenPhrases.Any(
            phrase => answer.Contains(
                phrase,
                StringComparison.OrdinalIgnoreCase)))
    {
        return false;
    }

    return true;
}

static double GetFirstNumericMetric(EvaluationResult result)
{
    var metric = result.Metrics.Values
        .OfType<NumericMetric>()
        .FirstOrDefault();

    return metric?.Value
        ?? throw new InvalidOperationException(
            "Evaluator did not return a numeric metric.");
}

static double Normalize(double score)
{
    // Built-in Microsoft quality evaluators use a 1..5 scale.
    return Math.Clamp((score - 1.0) / 4.0, 0.0, 1.0);
}

static Dictionary<string, double> Aggregate(
    IReadOnlyList<CaseReport> reports)
{
    var names = reports
        .SelectMany(x => x.Metrics.Keys)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    return names.ToDictionary(
        name => name,
        name =>
        {
            var values = reports
                .Where(x => x.Metrics.TryGetValue(name, out _))
                .Select(x => x.Metrics[name])
                .ToArray();

            return values.Length == 0 ? 0 : values.Average();
        },
        StringComparer.Ordinal);
}

static List<string> EvaluateQualityGate(
    IReadOnlyList<EvalCase> cases,
    IReadOnlyList<CaseReport> caseReports,
    IReadOnlyDictionary<string, double> aggregate,
    ThresholdConfig thresholds,
    Baseline baseline)
{
    var failures = new List<string>();

    foreach (var (metric, minimum) in thresholds.MinScores)
    {
        if (!aggregate.TryGetValue(metric, out var value) ||
            value < minimum)
        {
            failures.Add(
                $"{metric}: {value:0.000} is below minimum {minimum:0.000}");
        }
    }

    foreach (var (metric, baselineValue) in baseline.Metrics)
    {
        if (!aggregate.TryGetValue(metric, out var current) ||
            !thresholds.MaxRegression.TryGetValue(metric, out var tolerance))
        {
            continue;
        }

        var regression = baselineValue - current;
        if (regression > tolerance)
        {
            failures.Add(
                $"{metric}: regression {regression:0.000} exceeds allowed {tolerance:0.000}");
        }
    }

    if (thresholds.CriticalCasesMustPass)
    {
        var criticalIds = cases
            .Where(x => x.Critical)
            .Select(x => x.Id)
            .ToHashSet(StringComparer.Ordinal);

        var failedCritical = caseReports
            .Where(x => criticalIds.Contains(x.Id) && !x.HardPass)
            .Select(x => x.Id);

        foreach (var id in failedCritical)
        {
            failures.Add($"Critical case failed hard gate: {id}");
        }
    }

    return failures;
}

static void PrintReport(
    EvalReport report,
    Baseline baseline)
{
    Console.WriteLine();
    Console.WriteLine("=== MICROSOFT EVAL REPORT ===");

    foreach (var (metric, value) in report.Metrics.OrderBy(x => x.Key))
    {
        baseline.Metrics.TryGetValue(metric, out var baselineValue);
        var delta = baseline.Metrics.ContainsKey(metric)
            ? value - baselineValue
            : (double?)null;

        Console.WriteLine(
            delta is null
                ? $"{metric,-20} {value:0.000}"
                : $"{metric,-20} {value:0.000}  baseline={baselineValue:0.000}  delta={delta:+0.000;-0.000;0.000}");
    }

    Console.WriteLine();
    Console.WriteLine(report.Passed ? "QUALITY GATE: PASS" : "QUALITY GATE: FAIL");

    foreach (var failure in report.Failures)
    {
        Console.WriteLine($"- {failure}");
    }
}
