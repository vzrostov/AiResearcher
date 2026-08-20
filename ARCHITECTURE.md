# Architecture

## Design goal

InsightFlow separates semantic responsibilities between specialized agents while keeping orchestration explicit and deterministic.

## Why multiple agents

Each participant has a distinct responsibility and completion boundary:

- Researcher — evidence framing;
- Analyst — interpretation;
- FactChecker — verification;
- Critic — adversarial review;
- Editor — synthesis.

This is not equivalent to one model call with a long prompt because each stage sees the accumulated conversation and receives a dedicated system instruction for its role.

## Why sequential orchestration

A sequential workflow fits the dependency structure:

```text
research → analysis → verification → criticism → synthesis
```

Each stage depends on the output of previous stages. Parallel fan-out is more appropriate later for independent research dimensions, but it is not required to make the application multi-agent.

## Control boundary

The framework controls execution order. Agents control semantic decisions inside their assigned stage.

```text
Application / Workflow
→ determines which agent runs next

Agent / LLM
→ determines the content of its stage output
```

This is a hybrid architecture: deterministic orchestration with LLM-driven workers.

## Context flow

Microsoft Agent Framework sequential orchestration passes the prior conversation through the chain. This allows later agents to inspect earlier outputs.

The prompts therefore enforce role discipline:

- later agents should not invent missing evidence;
- FactChecker explicitly marks unsupported claims;
- Editor must not add new facts.

## External data

No fake search API is included. Real external data should be added through:

- function tools;
- MCP tools;
- provider-hosted web search;
- a dedicated retrieval service.

That keeps evidence acquisition separate from orchestration semantics.
