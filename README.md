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
Editor
   ↓
Final report
```

### Researcher

Creates the evidence-oriented research brief, identifies missing information, and separates evidence from assumptions.

### Analyst

Builds the analytical model, identifies drivers and trade-offs, and marks claims that require verification.

### FactChecker

Reviews factual claims and classifies them as supported, partially supported, unsupported, or inference.

### Critic

Looks for one-sided reasoning, hidden assumptions, logical jumps, and missing alternatives.

### Editor

Produces the final report using only claims that survive the previous stages.

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
$env:OPENAI_API_KEY = "your-key"
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
