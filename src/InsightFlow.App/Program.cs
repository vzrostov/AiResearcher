using InsightFlow.App.Agents;
using InsightFlow.App.Configuration;
using InsightFlow.App.Orchestration;
using InsightFlow.App.Persistence;
using InsightFlow.App.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.json",
        optional: true,
        reloadOnChange: false)
    .AddEnvironmentVariables();

builder.Services
    .AddOptions<OpenAIOptions>()
    .Bind(builder.Configuration.GetSection(OpenAIOptions.SectionName));

builder.Services
    .AddOptions<WorkflowOptions>()
    .Bind(builder.Configuration.GetSection(WorkflowOptions.SectionName));

builder.Services.AddDbContextFactory<InsightFlowDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("InsightFlow")
        ?? "Data Source=insightflow.db"));

builder.Services.AddSingleton<AgentFactory>();
builder.Services.AddSingleton<ResearchWorkflow>();

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});

using var host = builder.Build();

await using (var db = await host.Services
    .GetRequiredService<IDbContextFactory<InsightFlowDbContext>>()
    .CreateDbContextAsync())
{
    await db.Database.EnsureCreatedAsync();
}

try
{
    var request = ConsoleRequestReader.Read(args);
    var workflow = host.Services.GetRequiredService<ResearchWorkflow>();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cts.Cancel();
    };

    var result = await workflow.RunAsync(request, cts.Token);

    Console.WriteLine();
    Console.WriteLine("=== FINAL REPORT ===");
    Console.WriteLine(result.FinalText);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Workflow cancelled.");
    Environment.ExitCode = 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
}
