using System.Reflection;
using AppTemplate.Api.Actions;
using AppTemplate.Api.Data;
using AppTemplate.Domain.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using Shouldly;

namespace AppTemplate.Architecture.Tests;

public sealed class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Entity<>).Assembly;
    private static readonly Assembly Api = typeof(ActionRegistryOptions).Assembly;

    [Fact]
    public void Domain_References_OnlyOneOf()
    {
        var referenced = Domain.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        referenced.ShouldNotBeEmpty();

        var disallowed = referenced
            .Where(n => !(n.StartsWith("System.", StringComparison.Ordinal)
                || n is "System" or "Microsoft.CSharp" or "netstandard" or "OneOf"))
            .ToList();

        disallowed.ShouldBeEmpty($"Domain may reference only System.*, Microsoft.CSharp, netstandard and OneOf; found: {string.Join(", ", disallowed)}");
    }

    [Fact(Skip = GuardConditions.NoAggregateReason, SkipType = typeof(GuardConditions), SkipUnless = nameof(GuardConditions.DomainHasAggregate))]
    public void DomainEntities_HaveNoPublicSetters()
    {
        var entities = Domain.GetTypes().Where(t => !t.IsAbstract && IsSubclassOfOpenGeneric(t, typeof(Entity<>))).ToList();
        entities.ShouldNotBeEmpty();

        var offenders = entities
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod is { IsPublic: true })
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();

        offenders.ShouldBeEmpty($"Entities must not expose public setters or init accessors: {string.Join(", ", offenders)}");
    }

    [Fact(Skip = GuardConditions.NoAggregateReason, SkipType = typeof(GuardConditions), SkipUnless = nameof(GuardConditions.DomainHasAggregate))]
    public void ValueObjects_AreSealedWithoutPublicParameterizedConstructors()
    {
        var valueObjects = Domain.GetTypes()
            .Where(t => t.IsClass && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueObject<,>)))
            .ToList();
        valueObjects.ShouldNotBeEmpty();

        var notSealed = valueObjects.Where(t => !t.IsSealed).Select(t => t.FullName).ToList();
        notSealed.ShouldBeEmpty($"Value objects must be sealed: {string.Join(", ", notSealed)}");

        var publicCtors = valueObjects
            .Where(t => t.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Any(c => c.GetParameters().Length > 0))
            .Select(t => t.FullName)
            .ToList();
        publicCtors.ShouldBeEmpty($"Value objects must be created through Create/FromTrusted, not a public constructor: {string.Join(", ", publicCtors)}");
    }

    [Fact]
    public void Api_DoesNotUseBannedTypes()
    {
        var result = Types.InAssembly(Api)
            .ShouldNot()
            .HaveDependencyOnAny("FluentValidation.ValidationException", "MediatR", "Mediator",
                "Microsoft.EntityFrameworkCore.InMemory", "Microsoft.EntityFrameworkCore.InMemoryDbContextOptionsExtensions")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue($"Banned dependencies in: {Names(result.FailingTypes)}");
    }

    // UseInMemoryDatabase lives in namespace Microsoft.EntityFrameworkCore, so a namespace ban cannot see it.
    // Refusing the assembly reference covers the API wherever it is called, test projects included.
    [Fact]
    public void NoAssembly_ReferencesEfCoreInMemory()
    {
        var assemblies = new[] { Domain, Api, typeof(AppTemplate.Api.Tests.Infrastructure.CoversAttribute).Assembly };
        assemblies.Length.ShouldBe(3);

        var offenders = assemblies
            .Where(a => a.GetReferencedAssemblies().Any(r => r.Name == "Microsoft.EntityFrameworkCore.InMemory"))
            .Select(a => a.GetName().Name)
            .ToList();

        offenders.ShouldBeEmpty($"Use SQLite in-memory, not the EF Core InMemory provider: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void NoResultType_IsDefinedOrUsed()
    {
        var defined = new[] { Domain, Api }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name == "Result" || t.Name.StartsWith("Result`", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();
        defined.ShouldBeEmpty($"Do not define Result types; return OneOf<T, errors>: {string.Join(", ", defined)}");

        foreach (var assembly in new[] { Domain, Api })
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny("OneOf.Types.Result", "OneOf.Types.Result`1")
                .GetResult();
            result.IsSuccessful.ShouldBeTrue($"{assembly.GetName().Name} depends on OneOf.Types.Result: {Names(result.FailingTypes)}");
        }
    }

    [Fact(Skip = GuardConditions.NoActionReason, SkipType = typeof(GuardConditions), SkipUnless = nameof(GuardConditions.ApiHasActions))]
    public void Registry_ApiAssembly_BuildsWithoutViolations()
    {
        var options = new ActionRegistryOptions();
        var candidates = options.Assemblies.SelectMany(a => a.GetTypes()).Concat(options.Types);

        var registry = ActionRegistry.Build(candidates, HasValidator);

        registry.All.ShouldNotBeEmpty();
    }

    [Fact(Skip = GuardConditions.NoAggregateReason, SkipType = typeof(GuardConditions), SkipUnless = nameof(GuardConditions.DomainHasAggregate))]
    public void AggregateRoots_VersionIsConcurrencyToken()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(connection).Options;
        using var context = new SqliteAppDbContext(options, []);

        var roots = context.Model.GetEntityTypes()
            .Where(e => typeof(IAggregateRoot).IsAssignableFrom(e.ClrType))
            .ToList();
        roots.ShouldNotBeEmpty();

        foreach (var root in roots)
        {
            var version = root.FindProperty(nameof(IAggregateRoot.Version));
            version.ShouldNotBeNull($"{root.ClrType.Name} must map Version");
            version.IsConcurrencyToken.ShouldBeTrue($"{root.ClrType.Name}.Version must be a concurrency token (preview double-apply safety)");
        }
    }

    internal static bool HasValidator(Type input) =>
        Api.GetTypes().Any(t => !t.IsAbstract && IsValidatorFor(t, input));

    private static bool IsValidatorFor(Type type, Type input)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(FluentValidation.AbstractValidator<>) && b.GenericTypeArguments[0] == input)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSubclassOfOpenGeneric(Type type, Type definition)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (b.IsGenericType && b.GetGenericTypeDefinition() == definition)
            {
                return true;
            }
        }

        return false;
    }

    private static string Names(IEnumerable<IType>? types) => string.Join(", ", (types ?? []).Select(t => t.FullName));
}
