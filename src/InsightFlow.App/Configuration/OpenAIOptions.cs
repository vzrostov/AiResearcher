namespace InsightFlow.App.Configuration;

public sealed class OpenAIOptions
{
    public const string SectionName = "OpenAI";

    public string Model { get; init; } = "gpt-5-mini";
    public string OpenAIApiKey { get; init; } = string.Empty;
}
