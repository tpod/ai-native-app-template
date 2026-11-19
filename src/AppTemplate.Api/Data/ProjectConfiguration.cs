using AppTemplate.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Api.Data;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Version).IsConcurrencyToken();

        // SQLite-specific: case-insensitive names. See swap-to-postgres.
        builder.Property(p => p.Name).IsRequired().HasMaxLength(ProjectName.MaxLength).UseCollation("NOCASE");
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(p => new { p.Name, p.Status });

        builder.Ignore(p => p.DomainEvents);
        builder.HasMany(p => p.Tasks).WithOne().HasForeignKey("ProjectId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Tasks).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_tasks");
    }
}

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("TaskItems");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Title).IsRequired().HasMaxLength(TaskTitle.MaxLength);
    }
}
