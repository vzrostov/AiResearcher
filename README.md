# InsightFlow

InsightFlow is a multi-agent analytical workflow that turns a research topic into a reviewed analytical report.

The application uses Microsoft Agent Framework to coordinate several specialized LLM agents in a deterministic sequential workflow while keeping each agent's responsibility explicit.

## Workflow

```text
User request
   ↓
Researcher
   ↓
Analyst
   ↓
FactChecker
   ↓
Critic
   ↓
QualityChecker
   ↓
Editor
   ↓
Final report
```

### Agent responsibilities and data flow

The **Researcher** receives the original research request and produces a `ResearchResult` containing `Sources` and `Findings`. Each finding carries a `Claim`, supporting `Evidence`, a `Confidence` value, and `SourceIds` that link it to the relevant external sources.

The **Analyst** receives the complete `ResearchResult`, including the collected sources and findings, and synthesizes them into higher-level `Conclusions`. Each `AnalysisConclusion` must reference the exact `ResearchFinding.Id` values that support it.

The **FactChecker** receives both `ResearchResult` and `AnalysisResult`. It uses the original findings and evidence to verify every analytical conclusion exactly once and classifies each one as `Verified`, `Unsupported`, or `Contradicted`.

The **Critic** receives `ResearchResult`, `AnalysisResult`, and `FactCheckResult`. It evaluates the full evidence chain, identifies logical or methodological weaknesses, records `Issues`, and detects explicit `Conflicts` between findings or conclusions.

The **QualityChecker** receives the outputs of the Researcher, Analyst, FactChecker, and Critic. It does not use an LLM; instead, deterministic C# rules evaluate blocking issues, unresolved conflicts, verified conclusions, and source coverage to decide whether the workflow may continue.

The **Editor** receives only `AnalysisResult`, `FactCheckResult`, `CriticResult`, and `QualityCheckerResult`. It does not re-read raw findings or sources directly; it produces the final report from the already analyzed and reviewed material without introducing new facts.

The **Workflow** coordinates the fixed execution order, persists state and agent results in SQLite, supports idempotent resume behavior, and writes the workflow progress diagram to the `logs` directory as each stage completes.

## Technology

- .NET 8
- C#
- Microsoft Agent Framework
- Microsoft Agent Framework Workflows
- OpenAI Responses client
- Microsoft.Extensions.Hosting
- xUnit

## Configuration

Set an OpenAI API key in the environment:

### PowerShell

```powershell
$env:OpenAIApiKey = "your-key"
$env:OPENAI_MODEL = "gpt-5-mini"
```

`OPENAI_MODEL` is optional. If it is not specified, the value from `appsettings.json` is used.

## Build

```powershell
dotnet restore
dotnet build
```

## Run

Interactive mode:

```powershell
dotnet run --project src/InsightFlow.App
```

Or pass a topic directly:

```powershell
dotnet run --project src/InsightFlow.App -- "Prospects for small modular reactors in Europe through 2040"
```

## Tests

```powershell
dotnet test
```

## Architecture notes

The workflow is intentionally deterministic at the orchestration level: the order of agents is fixed, while each individual agent remains an LLM-driven component responsible for semantic analysis inside its role.

This keeps the control flow observable and prevents free-form agent-to-agent routing from becoming the dominant source of complexity.

The current workflow does not claim to perform independent external fact retrieval. The FactChecker verifies consistency and support within the information available to the workflow. External evidence acquisition should be connected through a tool or MCP server rather than simulated by prompts.

## Repository layout

```text
InsightFlow.sln
src/
  InsightFlow.App/
    Agents/
    Configuration/
    Contracts/
    Orchestration/
    Prompts/
    Runtime/
tests/
  InsightFlow.Tests/
```
