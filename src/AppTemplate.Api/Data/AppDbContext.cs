using AppTemplate.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AppTemplate.Api.Data;

/// <summary>
/// The unit of work every action and query uses. Abstract: the app runs on <see cref="PostgresAppDbContext"/>, and
/// the fast test suite on <see cref="SqliteAppDbContext"/>. Each provider subclass owns its migration set and
/// translates provider-neutral annotations (such as <see cref="CaseInsensitiveText"/>) into its own mapping.
/// </summary>
public abstract class AppDbContext(DbContextOptions options, IEnumerable<IModelContributor> contributors)
    : DbContext(options)
{
    private readonly IModelContributor[] _contributors = [.. contributors];

    public DbSet<Project> Projects => Set<Project>();

    /// <summary>Append-only business history. Only the action pipeline adds rows.</summary>
    public DbSet<HistoryEntry> History => Set<HistoryEntry>();

    internal string ContributorKey => string.Join('|', _contributors.Select(c => c.GetType().AssemblyQualifiedName));

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.ReplaceService<IModelCacheKeyFactory, ContributorModelCacheKeyFactory>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => ValueObjectConventions.Apply(configurationBuilder, typeof(Project).Assembly);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        foreach (var contributor in _contributors)
        {
            contributor.Configure(modelBuilder);
        }
    }

    private sealed class ContributorModelCacheKeyFactory : IModelCacheKeyFactory
    {
        // Keyed on the runtime type, so the Postgres and SQLite models are cached separately.
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), designTime, (context as AppDbContext)?.ContributorKey);
    }
}
