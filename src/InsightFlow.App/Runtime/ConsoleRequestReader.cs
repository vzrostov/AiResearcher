using InsightFlow.App.Contracts;

namespace InsightFlow.App.Runtime;

public static class ConsoleRequestReader
{
    public static ResearchRequest Read(string[] args)
    {
        if (args.Length > 0)
        {
            return new ResearchRequest(
                Topic: string.Join(' ', args),
                Audience: "software and technology professionals",
                OutputLanguage: "Russian");
        }

        Console.Write("Research topic: ");
        var topic = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new InvalidOperationException("Research topic is required.");
        }

        Console.Write("Scope (optional): ");
        var scope = Console.ReadLine();

        return new ResearchRequest(
            Topic: topic.Trim(),
            Scope: string.IsNullOrWhiteSpace(scope) ? null : scope.Trim(),
            Audience: "software and technology professionals",
            OutputLanguage: "Russian");
    }
}
