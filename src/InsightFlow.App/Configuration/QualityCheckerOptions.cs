namespace InsightFlow.App.Configuration;

public sealed class QualityCheckerOptions
{
    public const string SectionName = "QualityChecker";

    public double MinimumSourceCoverage { get; init; } = 0.7;

    public bool RequireVerifiedClaims { get; init; } = true;
}
