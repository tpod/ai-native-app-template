using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Api.Data;

/// <summary>Lets a module add entities to <see cref="AppDbContext"/> without editing it.</summary>
public interface IModelContributor
{
    void Configure(ModelBuilder modelBuilder);

    /// <summary>Raw DDL that creates this contributor's tables; executed after migrations in tests.</summary>
    string SchemaSql { get; }
}
