using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AppTemplate.Api.Actions.Preview;

/// <summary>Turns pending EF changes into one <see cref="Change"/> per changed scalar field, using provider values.</summary>
public static class ChangeSetReader
{
    private const string VersionProperty = "Version";

    public static IReadOnlyList<Change> Read(ChangeTracker tracker)
    {
        var changes = new List<Change>();
        foreach (var entry in tracker.Entries())
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
                continue;
            }

            var entity = entry.Metadata.ClrType.Name;
            var id = string.Join('|', entry.Metadata.FindPrimaryKey()!.Properties
                .Select(p => ProviderString(Provider(entry.Property(p.Name).CurrentValue, entry.Property(p.Name)))));
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

                changes.Add(new Change(entity, id, op, FieldName(path), ToNode(before), ToNode(after)));
            }
        }

        return [.. changes
            .OrderBy(c => c.Entity, StringComparer.Ordinal)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Field, StringComparer.Ordinal)];
    }

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
