namespace InsightFlow.App.Contracts;

public sealed record SourceReference(
    Guid Id,
    string? Url,
    string? Title,
    DateTimeOffset? PublishedAt,
    DateTimeOffset RetrievedAt);
