from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path
from typing import Any

from langsmith import Client
from openevals.llm import create_llm_as_judge

ROOT = Path(__file__).resolve().parent
DATASET_PATH = ROOT.parent / "datasets" / "core.json"
THRESHOLDS_PATH = ROOT.parent / "eval-thresholds.json"
BASELINE_PATH = ROOT / "baseline.json"
REPORT_PATH = ROOT / "report.json"

DATASET_NAME = os.getenv("LANGSMITH_DATASET", "insightflow-core")
EXPERIMENT_PREFIX = os.getenv("LANGSMITH_EXPERIMENT_PREFIX", "insightflow")
EVAL_MODEL = os.getenv("EVAL_MODEL", "openai:gpt-5-mini")

FINAL_MARKER = "=== FINAL REPORT ==="


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def run_insightflow(question: str) -> str:
    project = os.getenv(
        "INSIGHTFLOW_PROJECT",
        str((ROOT.parent.parent / "src" / "InsightFlow.App" / "InsightFlow.App.csproj").resolve()),
    )

    completed = subprocess.run(
        ["dotnet", "run", "--project", project, "--", question],
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )

    if completed.returncode != 0:
        raise RuntimeError(
            f"InsightFlow.App exited with {completed.returncode}\n{completed.stderr}"
        )

    marker_index = completed.stdout.rfind(FINAL_MARKER)
    if marker_index < 0:
        raise RuntimeError(f"Final report marker '{FINAL_MARKER}' was not found.")

    return completed.stdout[marker_index + len(FINAL_MARKER) :].strip()


def target(inputs: dict[str, Any]) -> dict[str, Any]:
    return {"answer": run_insightflow(inputs["question"])}


def rubric(metric: str, definition: str) -> str:
    return f"""
You are evaluating an analytical multi-agent system.

Metric: {metric}
Definition: {definition}

Question:
{{inputs}}

System answer:
{{outputs}}

Reference / acceptance information:
{{reference_outputs}}

Return a score from 0.0 to 1.0.
0.0 means completely unacceptable for this metric.
1.0 means excellent.
Use the reference information only as an evaluation aid; do not reward phrase matching by itself.
"""


RELEVANCE = create_llm_as_judge(
    prompt=rubric(
        "relevance",
        "How directly and usefully the answer addresses the user's actual request without drifting into unrelated material.",
    ),
    feedback_key="relevance",
    model=EVAL_MODEL,
)

GROUNDEDNESS = create_llm_as_judge(
    prompt=rubric(
        "groundedness",
        "How well factual claims are supported by the supplied grounding context and how well unsupported certainty is avoided.",
    ),
    feedback_key="groundedness",
    model=EVAL_MODEL,
)

COMPLETENESS = create_llm_as_judge(
    prompt=rubric(
        "completeness",
        "How well the answer covers the important aspects described by the expected summary.",
    ),
    feedback_key="completeness",
    model=EVAL_MODEL,
)

CONSISTENCY = create_llm_as_judge(
    prompt=rubric(
        "consistency",
        "Whether the answer is internally consistent and avoids mutually contradictory claims or conclusions.",
    ),
    feedback_key="consistency",
    model=EVAL_MODEL,
)

TASK_ADHERENCE = create_llm_as_judge(
    prompt=rubric(
        "task_adherence",
        "Whether the system follows the requested task, constraints, intent and expected behavior, including challenging invalid premises when appropriate.",
    ),
    feedback_key="task_adherence",
    model=EVAL_MODEL,
)


def wrap_judge(judge):
    def evaluator(
        inputs: dict[str, Any],
        outputs: dict[str, Any],
        reference_outputs: dict[str, Any],
    ):
        return judge(
            inputs=inputs,
            outputs=outputs,
            reference_outputs=reference_outputs,
        )

    return evaluator


def hard_pass_evaluator(
    inputs: dict[str, Any],
    outputs: dict[str, Any],
    reference_outputs: dict[str, Any],
):
    answer = (outputs.get("answer") or "").strip()
    required = reference_outputs.get("required_phrases_any") or []
    forbidden = reference_outputs.get("forbidden_phrases") or []

    passed = bool(answer)

    if passed and required:
        passed = any(phrase.lower() in answer.lower() for phrase in required)

    if passed and forbidden:
        passed = not any(phrase.lower() in answer.lower() for phrase in forbidden)

    return {
        "key": "hard_pass",
        "score": 1.0 if passed else 0.0,
    }


def ensure_dataset(client: Client, cases: list[dict[str, Any]]) -> None:
    if client.has_dataset(dataset_name=DATASET_NAME):
        return

    dataset = client.create_dataset(
        dataset_name=DATASET_NAME,
        description="Canonical InsightFlow regression evaluation dataset.",
    )

    client.create_examples(
        inputs=[{"question": case["input"]} for case in cases],
        outputs=[
            {
                "expected_summary": case["expected_summary"],
                "grounding_context": case["grounding_context"],
                "required_phrases_any": case["required_phrases_any"],
                "forbidden_phrases": case["forbidden_phrases"],
                "critical": case["critical"],
                "case_id": case["id"],
                "category": case["category"],
            }
            for case in cases
        ],
        dataset_id=dataset.id,
    )


def aggregate_experiment(client: Client, experiment_name: str) -> dict[str, float]:
    results = client.get_experiment_results(project_name=experiment_name)

    sums: dict[str, float] = {}
    counts: dict[str, int] = {}

    for row in results:
        for feedback in row.get("feedback_stats", {}).values():
            key = feedback.get("key")
            score = feedback.get("avg")
            if key is None or score is None:
                continue
            sums[key] = sums.get(key, 0.0) + float(score)
            counts[key] = counts.get(key, 0) + 1

    return {
        key: sums[key] / counts[key]
        for key in sums
        if counts[key] > 0
    }


def quality_gate(
    metrics: dict[str, float],
    thresholds: dict[str, Any],
    baseline: dict[str, Any],
) -> list[str]:
    failures: list[str] = []

    for metric, minimum in thresholds["min_scores"].items():
        key = "hard_pass" if metric == "hard_pass_rate" else metric
        value = metrics.get(key, 0.0)
        if value < minimum:
            failures.append(
                f"{metric}: {value:.3f} is below minimum {minimum:.3f}"
            )

    for metric, baseline_value in baseline.get("metrics", {}).items():
        key = "hard_pass" if metric == "hard_pass_rate" else metric
        current = metrics.get(key)
        tolerance = thresholds["max_regression"].get(metric)

        if current is None or tolerance is None:
            continue

        regression = baseline_value - current
        if regression > tolerance:
            failures.append(
                f"{metric}: regression {regression:.3f} exceeds allowed {tolerance:.3f}"
            )

    return failures


def normalize_metrics(metrics: dict[str, float]) -> dict[str, float]:
    normalized = dict(metrics)
    if "hard_pass" in normalized:
        normalized["hard_pass_rate"] = normalized.pop("hard_pass")
    return normalized


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--update-baseline", action="store_true")
    args = parser.parse_args()

    cases = load_json(DATASET_PATH)
    thresholds = load_json(THRESHOLDS_PATH)
    baseline = load_json(BASELINE_PATH)

    client = Client()
    ensure_dataset(client, cases)

    experiment = client.evaluate(
        target,
        data=DATASET_NAME,
        evaluators=[
            wrap_judge(RELEVANCE),
            wrap_judge(GROUNDEDNESS),
            wrap_judge(COMPLETENESS),
            wrap_judge(CONSISTENCY),
            wrap_judge(TASK_ADHERENCE),
            hard_pass_evaluator,
        ],
        experiment_prefix=EXPERIMENT_PREFIX,
        max_concurrency=1,
    )

    experiment_name = getattr(experiment, "experiment_name", None)
    if not experiment_name:
        experiment_name = getattr(experiment, "project_name", None)

    if not experiment_name:
        raise RuntimeError(
            "Could not determine LangSmith experiment name from evaluation result."
        )

    metrics = normalize_metrics(aggregate_experiment(client, experiment_name))
    failures = quality_gate(metrics, thresholds, baseline)

    report = {
        "dataset": DATASET_NAME,
        "experiment": experiment_name,
        "metrics": metrics,
        "passed": not failures,
        "failures": failures,
    }

    REPORT_PATH.write_text(
        json.dumps(report, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )

    print("\n=== LANGSMITH EVAL REPORT ===")
    print(f"Experiment: {experiment_name}")
    for key, value in sorted(metrics.items()):
        base = baseline.get("metrics", {}).get(key)
        if base is None:
            print(f"{key:20} {value:.3f}")
        else:
            print(
                f"{key:20} {value:.3f}  baseline={base:.3f}  delta={value-base:+.3f}"
            )

    print("\nQUALITY GATE:", "PASS" if not failures else "FAIL")
    for failure in failures:
        print("-", failure)

    if args.update_baseline:
        BASELINE_PATH.write_text(
            json.dumps(
                {"dataset": "core", "metrics": metrics},
                indent=2,
                ensure_ascii=False,
            ),
            encoding="utf-8",
        )
        print(f"\nBaseline updated: {BASELINE_PATH}")
        return 0

    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
