using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using AppTemplate.Domain.Common;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AppTemplate.Api.Actions.Preview;

/// <summary>Turns pending EF changes into one <see cref="Change"/> per changed scalar field, using provider values.</summary>
public static class ChangeSetReader
{
    private const string VersionProperty = "Version";

    public static IReadOnlyList<Change> Read(ChangeTracker tracker)
        => [.. tracker.Entries().SelectMany(ChangesOf)
            .OrderBy(c => c.Entity, StringComparer.Ordinal)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Field, StringComparer.Ordinal)];

    /// <summary>The entries that contribute at least one <see cref="Change"/>: the same rule as <see cref="Read"/>.</summary>
    internal static IReadOnlyList<EntityEntry> ChangedEntries(ChangeTracker tracker)
        => [.. tracker.Entries().Where(e => ChangesOf(e).Any())];

    /// <summary>
    /// The tracked aggregate root that owns <paramref name="entry"/>: the entry itself for a root, otherwise the root
    /// reached by following foreign keys to tracked principals. Null when no tracked root owns it.
    /// </summary>
    internal static EntityEntry? RootOf(EntityEntry entry)
    {
        if (entry.Entity is IAggregateRoot)
        {
            return entry;
        }

        var original = entry.State == EntityState.Deleted;
        foreach (var fk in entry.Metadata.GetForeignKeys())
        {
            var key = KeyString(entry, fk.Properties, original);
            var principal = entry.Context.ChangeTracker.Entries().FirstOrDefault(e =>
                fk.PrincipalEntityType.ClrType.IsAssignableFrom(e.Metadata.ClrType)
                && KeyString(e, fk.PrincipalKey.Properties, e.State == EntityState.Deleted) == key);
            if (principal is not null && RootOf(principal) is { } root)
            {
                return root;
            }
        }

        return null;
    }

    /// <summary>The entry's primary key as provider values, joined with <c>|</c>; the <c>id</c> used in <see cref="Change"/>.</summary>
    internal static string KeyOf(EntityEntry entry) => KeyString(entry, entry.Metadata.FindPrimaryKey()!.Properties, original: false);

    private static IEnumerable<Change> ChangesOf(EntityEntry entry)
    {
        var op = entry.State switch
        {
            EntityState.Added => "added",
            EntityState.Modified => "modified",
            EntityState.Deleted => "deleted",
            _ => null,
        };
        if (op is null)
        {
            yield break;
        }

        var entity = entry.Metadata.ClrType.Name;
        var id = KeyOf(entry);
        foreach (var (path, property) in Flatten(entry))
        {
            if (path == VersionProperty)
            {
                continue;
            }

            var before = op == "added" ? null : Provider(property.OriginalValue, property);
            var after = op == "deleted" ? null : Provider(property.CurrentValue, property);
            if (op == "modified" && (!property.IsModified || Equals(before, after)))
            {
                continue;
            }

            yield return new Change(entity, id, op, FieldName(path), ToNode(before), ToNode(after));
        }
    }

    private static string KeyString(EntityEntry entry, IEnumerable<IReadOnlyProperty> properties, bool original)
        => string.Join('|', properties.Select(p =>
        {
            var property = entry.Property(p.Name);
            return ProviderString(Provider(original ? property.OriginalValue : property.CurrentValue, property));
        }));

    private static IEnumerable<(string Path, PropertyEntry Property)> Flatten(EntityEntry entry)
    {
        foreach (var p in entry.Properties)
        {
            yield return (p.Metadata.Name, p);
        }

        foreach (var complex in entry.ComplexProperties)
        {
            foreach (var item in Flatten(complex, complex.Metadata.Name))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<(string Path, PropertyEntry Property)> Flatten(ComplexPropertyEntry entry, string prefix)
    {
        foreach (var p in entry.Properties)
        {
            yield return ($"{prefix}.{p.Metadata.Name}", p);
        }

        foreach (var nested in entry.ComplexProperties)
        {
            foreach (var item in Flatten(nested, $"{prefix}.{nested.Metadata.Name}"))
            {
                yield return item;
            }
        }
    }

    private static string FieldName(string path)
        => string.Join('.', path.Split('.').Select(s => JsonNamingPolicy.CamelCase.ConvertName(s)));

    private static object? Provider(object? value, PropertyEntry property)
    {
        if (value is null)
        {
            return null;
        }

        var converter = property.Metadata.GetValueConverter() ?? property.Metadata.GetTypeMapping().Converter;
        return converter is null ? value : converter.ConvertToProvider(value);
    }

    private static string ProviderString(object? value) => value switch
    {
        null => string.Empty,
        Guid g => g.ToString("D"),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        byte n => JsonValue.Create(n),
        sbyte n => JsonValue.Create(n),
        short n => JsonValue.Create(n),
        ushort n => JsonValue.Create(n),
        int n => JsonValue.Create(n),
        uint n => JsonValue.Create(n),
        long n => JsonValue.Create(n),
        ulong n => JsonValue.Create(n),
        float n => JsonValue.Create(n),
        double n => JsonValue.Create(n),
        decimal n => JsonValue.Create(n),
        Guid g => JsonValue.Create(g.ToString("D")),
        DateTime d => JsonValue.Create(d.ToString("O", CultureInfo.InvariantCulture)),
        DateTimeOffset d => JsonValue.Create(d.ToString("O", CultureInfo.InvariantCulture)),
        byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
        Enum e => JsonValue.Create(e.ToString()),
        _ => JsonValue.Create(ProviderString(value)),
    };
}
