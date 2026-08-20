namespace InsightFlow.App.Contracts;

public sealed record ResearchRequest(
    string Topic,
    string? Scope = null,
    string? Audience = null,
    string? OutputLanguage = null)
{
    public string ToPrompt()
    {
        var scope = string.IsNullOrWhiteSpace(Scope) ? "not specified" : Scope;
        var audience = string.IsNullOrWhiteSpace(Audience) ? "professional reader" : Audience;
        var language = string.IsNullOrWhiteSpace(OutputLanguage) ? "Russian" : OutputLanguage;

        return $"""
               Research topic: {Topic}
               Scope: {scope}
               Audience: {audience}
               Output language: {language}

               Produce a concise but evidence-oriented analytical report.
               Clearly distinguish established facts, assumptions, and conclusions.
               Do not invent citations or claim that a source was checked unless it appears in the conversation.
               """;
    }
}
