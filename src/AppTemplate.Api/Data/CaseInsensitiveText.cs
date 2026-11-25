using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Api.Data;

/// <summary>
/// Case-insensitive text, declared once in a provider-neutral configuration and translated by each provider context:
/// <c>citext</c> on Postgres, the <c>NOCASE</c> collation on SQLite. Equality and ordering both ignore case.
/// </summary>
public static class CaseInsensitiveText
{
    public const string Annotation = "AppTemplate:CaseInsensitive";

    /// <summary>Marks a text column (a <c>string</c>, or a value object stored as one) as case-insensitive.</summary>
    public static PropertyBuilder<T> IsCaseInsensitive<T>(this PropertyBuilder<T> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.HasAnnotation(Annotation, true);
    }

    internal static void ApplyForPostgres(ModelBuilder modelBuilder)
    {
        var properties = Annotated(modelBuilder);
        if (properties.Count == 0)
        {
            return;
        }

        // citext has no length modifier; the value object enforces the length.
        modelBuilder.HasPostgresExtension("citext");
        foreach (var property in properties)
        {
            property.SetColumnType("citext");
        }
    }

    internal static void ApplyForSqlite(ModelBuilder modelBuilder)
    {
        foreach (var property in Annotated(modelBuilder))
        {
            property.SetCollation("NOCASE");
        }
    }

    private static List<IMutableProperty> Annotated(ModelBuilder modelBuilder)
        => [.. modelBuilder.Model.GetEntityTypes()
            .SelectMany(e => e.GetDeclaredProperties())
            .Where(p => p.FindAnnotation(Annotation)?.Value is true)];
}
