using InsightFlow.App.Configuration;
using InsightFlow.App.Prompts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Responses;

namespace InsightFlow.App.Agents;

public sealed class AgentFactory
{
    private readonly OpenAIOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConfiguration _configuration;

    public AgentFactory(
        IOptions<OpenAIOptions> options,
        ILoggerFactory loggerFactory,
        IConfiguration configuration)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _configuration = configuration;
    }

    public AgentSet Create()
    {
        var apiKey = _options.OpenAIApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAIApiKey is not set. Set the environment variable before running InsightFlow.");
        }

        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        if (string.IsNullOrWhiteSpace(model))
        {
            model = _options.Model;
        }

#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        var responsesClient = new OpenAIClient(apiKey).GetResponsesClient();
#pragma warning restore OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        return new AgentSet(
            Researcher: responsesClient.AsAIAgent(
                model: model,
                instructions: AgentPrompts.Researcher,
                name: "Researcher",
                loggerFactory: _loggerFactory),
            Analyst: responsesClient.AsAIAgent(
                model: model,
                instructions: AgentPrompts.Analyst,
                name: "Analyst",
                loggerFactory: _loggerFactory),
            FactChecker: responsesClient.AsAIAgent(
                model: model,
                instructions: AgentPrompts.FactChecker,
                name: "FactChecker",
                loggerFactory: _loggerFactory),
            Critic: responsesClient.AsAIAgent(
                model: model,
                instructions: AgentPrompts.Critic,
                name: "Critic",
                loggerFactory: _loggerFactory),
            Editor: responsesClient.AsAIAgent(
                model: model,
                instructions: AgentPrompts.Editor,
                name: "Editor",
                loggerFactory: _loggerFactory));
    }
}
