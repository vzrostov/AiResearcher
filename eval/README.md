# InsightFlow evaluation projects

Two independent regression-evaluation harnesses for the same `InsightFlow.App`.

```text
datasets/core.json
        |
        +--> InsightFlow.Evals.Microsoft
        |
        +--> InsightFlow.Evals.LangSmith
```

Both use the same canonical dataset and the same threshold policy, but maintain independent baselines because the evaluators/rubrics are different.

Projects:

- `InsightFlow.Evals.Microsoft` — C#/.NET, Microsoft.Extensions.AI.Evaluation.
- `InsightFlow.Evals.LangSmith` — Python LangSmith/OpenEvals harness that evaluates the same .NET application.

Read each project's `README.md` for manual and CI instructions.
