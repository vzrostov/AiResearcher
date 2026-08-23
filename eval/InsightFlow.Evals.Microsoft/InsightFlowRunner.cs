using System.Diagnostics;

namespace InsightFlow.Evals.Microsoft;

public sealed class InsightFlowRunner
{
    private const string FinalMarker = "=== FINAL REPORT ===";

    private readonly string _projectPath;

    public InsightFlowRunner(string projectPath)
    {
        _projectPath = projectPath;
    }

    public async Task<string> RunAsync(
        string input,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(_projectPath);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(input);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start InsightFlow.App.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"InsightFlow.App exited with code {process.ExitCode}.{Environment.NewLine}{stderr}");
        }

        var markerIndex = stdout.LastIndexOf(
            FinalMarker,
            StringComparison.Ordinal);

        if (markerIndex < 0)
        {
            throw new InvalidOperationException(
                $"Final report marker '{FinalMarker}' was not found in application output.");
        }

        return stdout[(markerIndex + FinalMarker.Length)..].Trim();
    }
}
