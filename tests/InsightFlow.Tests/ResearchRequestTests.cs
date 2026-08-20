using InsightFlow.App.Contracts;
using Xunit;

namespace InsightFlow.Tests;

public sealed class ResearchRequestTests
{
    [Fact]
    public void ToPrompt_IncludesTopicAndScope()
    {
        var request = new ResearchRequest(
            Topic: "Small modular reactors",
            Scope: "Europe through 2040",
            Audience: "executives",
            OutputLanguage: "English");

        var prompt = request.ToPrompt();

        Assert.Contains("Small modular reactors", prompt);
        Assert.Contains("Europe through 2040", prompt);
        Assert.Contains("executives", prompt);
        Assert.Contains("English", prompt);
    }

    [Fact]
    public void ToPrompt_UsesDefaultsForOptionalValues()
    {
        var request = new ResearchRequest("AI regulation");

        var prompt = request.ToPrompt();

        Assert.Contains("not specified", prompt);
        Assert.Contains("professional reader", prompt);
        Assert.Contains("Russian", prompt);
    }
}
