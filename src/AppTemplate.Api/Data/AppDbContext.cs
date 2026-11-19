using AppTemplate.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AppTemplate.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IEnumerable<IModelContributor> contributors)
    : DbContext(options)
{
    private readonly IModelContributor[] _contributors = [.. contributors];

    public DbSet<Project> Projects => Set<Project>();

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
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), designTime, (context as AppDbContext)?.ContributorKey);
    }
}
