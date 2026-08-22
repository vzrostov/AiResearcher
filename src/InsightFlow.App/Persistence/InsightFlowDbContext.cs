using InsightFlow.App.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InsightFlow.App.Persistence;

public sealed class InsightFlowDbContext(
    DbContextOptions<InsightFlowDbContext> options)
    : DbContext(options)
{
    public DbSet<WorkflowExecutionEntity> WorkflowExecutions => Set<WorkflowExecutionEntity>();

    public DbSet<AgentResultEntity> AgentResults => Set<AgentResultEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkflowExecutionEntity>(entity =>
        {
            entity.ToTable("WorkflowExecutions");

            entity.HasKey(x => x.WorkflowId);

            entity.Property(x => x.Topic).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.CurrentStep).HasConversion<string>();

            entity.HasMany(x => x.Results)
                .WithOne(x => x.Workflow)
                .HasForeignKey(x => x.WorkflowId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentResultEntity>(entity =>
        {
            entity.ToTable("AgentResults");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.ProducedByAgent).IsRequired();
            entity.Property(x => x.ResultType).IsRequired();
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.Property(x => x.ParentResultIdsJson).IsRequired();

            entity.HasIndex(x => new { x.WorkflowId, x.StepId })
                .IsUnique();
        });
    }
}
