# InsightFlow.Evals.LangSmith

Independent LangSmith evaluation harness for the same `InsightFlow.App` and the same canonical dataset used by the Microsoft project.

The harness is Python because the official LangSmith evaluation SDK is Python/JavaScript oriented. `InsightFlow.App` itself remains C#/.NET; this project simply launches it as the system-under-test.

## What it measures

- `Relevance`
- `Groundedness`
- `Completeness`
- `Consistency`
- `Task adherence`
- `Hard pass rate`

The five semantic metrics use LLM-as-a-judge rubrics through OpenEvals and are logged to a LangSmith experiment.

`Hard pass rate` is deterministic.

## Shared dataset

Both eval projects use:

```text
../datasets/core.json
```

The local canonical dataset is uploaded to LangSmith as:

```text
insightflow-core
```

by default.

Override with:

```powershell
$env:LANGSMITH_DATASET = "my-dataset-name"
```

The LangSmith copy is an execution artifact. The JSON file in the repository should remain the source of truth.

## Prerequisites

- Python 3.11+
- `uv` recommended (or regular `pip`)
- .NET 8 SDK because the harness launches `InsightFlow.App`
- LangSmith account/API key
- OpenAI API key for the LLM judges and for InsightFlow itself

Environment variables:

```powershell
$env:LANGSMITH_API_KEY = "..."
$env:OPENAI_API_KEY = "..."
$env:EVAL_MODEL = "openai:gpt-5-mini"
$env:INSIGHTFLOW_PROJECT = "C:\src\aiResearcher\src\InsightFlow.App\InsightFlow.App.csproj"
```

Optional:

```powershell
$env:LANGSMITH_DATASET = "insightflow-core"
$env:LANGSMITH_EXPERIMENT_PREFIX = "insightflow"
```

## Manual run with uv

```powershell
uv sync
uv run python run_evals.py
```

Without `uv`:

```powershell
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -e .
python run_evals.py
```

The run:

1. ensures the shared dataset exists in LangSmith;
2. runs every case through the real .NET application;
3. logs an experiment to LangSmith;
4. runs the semantic evaluators;
5. creates local `report.json`;
6. compares results to thresholds and baseline;
7. exits with code `1` if the quality gate fails.

You can inspect the detailed experiment and every example in the LangSmith UI.

## Establish / approve a baseline

First approved run:

```powershell
uv run python run_evals.py --update-baseline
```

This updates:

```text
baseline.json
```

Commit the approved baseline.

Do not update the baseline automatically in CI.

## CI / GitLab pipeline

Example:

```yaml
insightflow-evals-langsmith:
  stage: test
  image: python:3.12
  before_script:
    - apt-get update
    - apt-get install -y wget
    # Install .NET 8 SDK in your real CI image, or use a custom image
    # that already contains both Python and .NET.
    - pip install uv
  variables:
    EVAL_MODEL: "openai:gpt-5-mini"
    INSIGHTFLOW_PROJECT: "$CI_PROJECT_DIR/src/InsightFlow.App/InsightFlow.App.csproj"
  script:
    - cd tests/InsightFlow.Evals.LangSmith
    - uv sync
    - uv run python run_evals.py
  artifacts:
    when: always
    paths:
      - tests/InsightFlow.Evals.LangSmith/report.json
```

For a real pipeline, prefer a custom CI image containing both Python and .NET 8 instead of installing .NET on every job.

Store these as protected/masked CI variables:

```text
LANGSMITH_API_KEY
OPENAI_API_KEY
```

## Regression policy

The shared policy is:

```text
../eval-thresholds.json
```

A run fails when:

- a metric is below its absolute minimum;
- regression relative to the approved baseline exceeds its tolerance;
- the hard pass rate is too low.

A `-0.01` change does not automatically fail the pipeline if the allowed regression is `0.05`.

## LangSmith report vs local report

LangSmith is the detailed report:

- every dataset case;
- output;
- feedback/evaluator scores;
- experiment history;
- comparisons between experiments.

`report.json` exists only to give CI a simple portable artifact and exit-code decision.

## Cost warning

This demo runs five LLM judges per dataset example. With 8 cases that is roughly 40 judge calls in addition to the actual InsightFlow pipeline calls.

For regular CI you may later:

- run a smaller smoke-eval dataset on every commit;
- run the full dataset nightly or before release;
- use one multi-metric judge instead of five separate judges;
- cache system-under-test outputs when only evaluator code changes.

Those are optimizations; the current layout keeps each metric explicit and easy to understand.

## Important: dataset portability

The same questions and acceptance data are shared with the Microsoft harness, but the absolute scores are **not interchangeable**.

Use:

```text
Microsoft baseline -> compare with future Microsoft runs
LangSmith baseline -> compare with future LangSmith runs
```

Do not compare `Microsoft relevance = 0.82` directly with `LangSmith relevance = 0.82` as if they were the same measurement instrument.
