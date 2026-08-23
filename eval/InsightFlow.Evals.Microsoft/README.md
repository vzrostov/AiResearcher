# InsightFlow.Evals.Microsoft

Evaluation harness for `InsightFlow.App` using the Microsoft .NET AI evaluation stack.

## What it measures

The project runs the shared `../datasets/core.json` dataset through the real `InsightFlow.App` process and calculates:

- `Relevance`
- `Groundedness`
- `Completeness`
- `Consistency` — implemented with Microsoft's built-in `CoherenceEvaluator` as the closest built-in proxy
- `Task adherence`
- `Hard pass rate` — deterministic checks from the dataset

Microsoft quality evaluators use an LLM judge and return scores on a 1..5 scale. This project normalizes them to 0..1 before comparing them with the shared thresholds.

`Groundedness` currently uses the dataset's `grounding_context`. When InsightFlow has real source provenance, replace this reference context with the actual evidence/sources produced by the workflow.

## Prerequisites

- .NET 8 SDK
- working `InsightFlow.App`
- `OPENAI_API_KEY`
- optional `EVAL_MODEL` (default: `gpt-5-mini`)
- optional `INSIGHTFLOW_PROJECT` pointing to `InsightFlow.App.csproj`

Example on Windows PowerShell:

```powershell
$env:OPENAI_API_KEY = "..."
$env:EVAL_MODEL = "gpt-5-mini"
$env:INSIGHTFLOW_PROJECT = "C:\src\aiResearcher\src\InsightFlow.App\InsightFlow.App.csproj"
```

## Manual run

From this directory:

```powershell
dotnet restore
dotnet run
```

The run creates:

```text
bin/.../report.json
```

and exits with:

- `0` — quality gate passed
- `1` — regression or minimum-quality threshold failed

## Establish / approve a baseline

For the first approved version:

```powershell
dotnet run -- --update-baseline
```

This writes the current aggregate metrics into `baseline.json`.

Commit `baseline.json` to Git.

After that, normal runs compare the current metrics with:

1. absolute minimums from `../eval-thresholds.json`;
2. the approved `baseline.json`;
3. maximum allowed regression per metric;
4. critical-case hard gates.

Do **not** automatically update the baseline in CI. Update it only after a reviewed/approved change.

## CI / GitLab pipeline

Example job:

```yaml
insightflow-evals-microsoft:
  stage: test
  image: mcr.microsoft.com/dotnet/sdk:8.0
  variables:
    EVAL_MODEL: "gpt-5-mini"
    INSIGHTFLOW_PROJECT: "$CI_PROJECT_DIR/src/InsightFlow.App/InsightFlow.App.csproj"
  script:
    - dotnet restore tests/InsightFlow.Evals.Microsoft/InsightFlow.Evals.Microsoft.csproj
    - dotnet run --project tests/InsightFlow.Evals.Microsoft/InsightFlow.Evals.Microsoft.csproj
  artifacts:
    when: always
    paths:
      - tests/InsightFlow.Evals.Microsoft/bin/**/report.json
```

Store `OPENAI_API_KEY` as a protected/masked CI variable.

Because this is an LLM-based evaluation, a tiny score change is expected. The job does not fail merely because a metric decreases; it fails only when the configured tolerance or minimum threshold is violated.

## Thresholds

Shared policy is in:

```text
../eval-thresholds.json
```

Example:

```json
"relevance": 0.75
```

means the aggregate normalized relevance score must be at least `0.75`.

`max_regression = 0.05` means:

```text
baseline 0.86
current  0.83  -> PASS, regression 0.03

baseline 0.86
current  0.79  -> FAIL, regression 0.07
```

## Reports

The project intentionally emits a simple portable `report.json` and console summary.

Microsoft also provides `Microsoft.Extensions.AI.Evaluation.Reporting` and the `dotnet aieval` reporting tool. Add those later if you want Microsoft-native historical report storage/UI; they are not required for the first regression harness.

## Important

This harness launches the actual application as a child process. That keeps the eval project independent of internal DI/orchestration implementation.

The expected application CLI is:

```text
dotnet run --project <InsightFlow.App.csproj> -- "<question>"
```

and the final answer must follow the marker:

```text
=== FINAL REPORT ===
```
