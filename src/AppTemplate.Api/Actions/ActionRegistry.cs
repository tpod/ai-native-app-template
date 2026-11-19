using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using AppTemplate.Domain.Common;
using OneOf;
using OneOf.Types;

namespace AppTemplate.Api.Actions;

/// <summary>
/// The validated set of <c>[Action]</c> and <c>[Query]</c> handlers. Built once at startup; any rule violation fails startup.
/// </summary>
public sealed partial class ActionRegistry
{
    private const string NameRule = "^[a-z][a-z0-9_]*$";

    private static readonly HashSet<Type> OneOfDefinitions =
    [
        typeof(OneOf<>), typeof(OneOf<,>), typeof(OneOf<,,>), typeof(OneOf<,,,>), typeof(OneOf<,,,,>),
        typeof(OneOf<,,,,,>), typeof(OneOf<,,,,,,>), typeof(OneOf<,,,,,,,>), typeof(OneOf<,,,,,,,,>),
    ];

    private static readonly MethodInfo AwaitOneOfMethod =
        typeof(ActionRegistry).GetMethod(nameof(AwaitOneOf), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly Dictionary<string, ActionDescriptor> _byName;

    private ActionRegistry(IReadOnlyList<ActionDescriptor> all)
    {
        All = all;
        _byName = all.ToDictionary(d => d.Name, StringComparer.Ordinal);
    }

    /// <summary>Every registered action and query, sorted by name.</summary>
    public IReadOnlyList<ActionDescriptor> All { get; }

    /// <summary>The descriptor with exactly this name, or null.</summary>
    public ActionDescriptor? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>
    /// Builds descriptors for every candidate carrying <see cref="ActionAttribute"/> or <see cref="QueryAttribute"/>.
    /// Other candidates are ignored.
    /// </summary>
    /// <param name="candidates">Types to inspect, typically every type of the configured assemblies.</param>
    /// <param name="hasValidator">Given an input type, reports whether an <c>IValidator</c> for it is registered.</param>
    /// <exception cref="ActionRegistrationException">One or more rules are broken; lists every violation.</exception>
    public static ActionRegistry Build(IEnumerable<Type> candidates, Func<Type, bool> hasValidator)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(hasValidator);

        var violations = new List<string>();
        var descriptors = new List<ActionDescriptor>();
        var names = new List<(string Name, Type Type)>();

        foreach (var type in candidates.Distinct())
        {
            var action = type.GetCustomAttribute<ActionAttribute>(inherit: false);
            var query = type.GetCustomAttribute<QueryAttribute>(inherit: false);
            if (action is null && query is null)
            {
                continue;
            }

            var before = violations.Count;
            void Fail(string rule) => violations.Add($"{DisplayHandler(type)}: {rule}");

            if (action is not null && query is not null)
            {
                Fail("has both [Action] and [Query]; a handler is either a mutation or a query.");
                continue;
            }

            var name = action?.Name ?? query!.Name;
            var description = action?.Description ?? query!.Description;
            names.Add((name, type));

            if (!NamePattern().IsMatch(name))
            {
                Fail($"name '{name}' must match {NameRule}.");
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                Fail("description must be non-empty.");
            }

            if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters || type.GetConstructors().Length == 0)
            {
                // The contract and convention checks below assume a constructible, closed handler type.
                Fail("handler must be a non-abstract, non-generic class with a public constructor.");
                continue;
            }

            var create = action is not null
                ? ValidateMutation(type, action, hasValidator, Fail)
                : ValidateQuery(type, query!, hasValidator, Fail);

            // Invokers are compiled only for a type with no violations, so a broken type can never throw here
            // and hide the violations already collected.
            if (create is not null && violations.Count == before)
            {
                descriptors.Add(create());
            }
        }

        foreach (var group in names.GroupBy(n => n.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            foreach (var (name, type) in group)
            {
                var others = string.Join(", ", group.Where(o => o.Type != type).Select(o => DisplayHandler(o.Type)));
                violations.Add($"{DisplayHandler(type)}: name '{name}' is a duplicate; names must be unique (also used by {others}).");
            }
        }

        if (violations.Count > 0)
        {
            throw new ActionRegistrationException(violations);
        }

        return new ActionRegistry([.. descriptors.OrderBy(d => d.Name, StringComparer.Ordinal)]);
    }

    /// <summary>Checks the mutation contract; returns a factory that builds the descriptor and compiles its invokers.</summary>
    private static Func<ActionDescriptor>? ValidateMutation(Type type, ActionAttribute attr, Func<Type, bool> hasValidator, Action<string> fail)
    {
        var contract = SingleClosedInterface(type, typeof(IMutation<,>), "[Action]", "IMutation<TInput, TAggregate>", fail);
        if (contract is null)
        {
            return null;
        }

        var (input, aggregate) = (contract.GenericTypeArguments[0], contract.GenericTypeArguments[1]);
        RequireRecord(input, fail);

        var load = ConventionMethod(type, "Load", [input, typeof(ActionContext)], aggregate, fail);
        var apply = ConventionMethod(type, "Apply", [aggregate, input, typeof(ActionContext)], typeof(Success), fail);
        if (load is null || apply is null)
        {
            return null;
        }

        return () => new ActionDescriptor(
            attr.Name,
            attr.Description,
            ActionKind.Mutation,
            type,
            input,
            aggregate,
            OutputType: null,
            [.. load.Value.Errors.Concat(apply.Value.Errors).Distinct()],
            hasValidator(input),
            attr.Destructive,
            TimeSpan.FromSeconds(attr.PreviewLifetimeSeconds))
        {
            LoadAsync = Compile<Func<object, object, ActionContext, Task<IOneOf>>>(load.Value.Method),
            ApplyAsync = Compile<Func<object, object, object, ActionContext, Task<IOneOf>>>(apply.Value.Method),
            Summarize = Compile<Func<object, object, object, string>>(contract.GetMethod(nameof(IMutation<object, IAggregateRoot>.Summarize))!),
            ToResult = Compile<Func<object, object, object>>(contract.GetMethod(nameof(IMutation<object, IAggregateRoot>.ToResult))!),
        };
    }

    /// <summary>Checks the query contract; returns a factory that builds the descriptor and compiles its invoker.</summary>
    private static Func<ActionDescriptor>? ValidateQuery(Type type, QueryAttribute attr, Func<Type, bool> hasValidator, Action<string> fail)
    {
        var contract = SingleClosedInterface(type, typeof(IQuery<,>), "[Query]", "IQuery<TInput, TOutput>", fail);
        if (contract is null)
        {
            return null;
        }

        var (input, output) = (contract.GenericTypeArguments[0], contract.GenericTypeArguments[1]);
        RequireRecord(input, fail);

        var run = ConventionMethod(type, "Run", [input, typeof(ActionContext)], output, fail);
        if (run is null)
        {
            return null;
        }

        return () => new ActionDescriptor(
            attr.Name,
            attr.Description,
            ActionKind.Query,
            type,
            input,
            AggregateType: null,
            output,
            [.. run.Value.Errors.Distinct()],
            hasValidator(input),
            Destructive: false,
            TimeSpan.Zero)
        {
            RunAsync = Compile<Func<object, object, ActionContext, Task<IOneOf>>>(run.Value.Method),
        };
    }

    private static Type? SingleClosedInterface(Type type, Type definition, string attribute, string display, Action<string> fail)
    {
        var matches = type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == definition).ToList();
        switch (matches.Count)
        {
            case 1:
                return matches[0];
            case 0:
                fail($"{attribute} handler must implement {display}.");
                return null;
            default:
                fail($"implements {display} more than once; implement it exactly once.");
                return null;
        }
    }

    /// <summary>A record class or record struct has a compiler-generated <c>bool PrintMembers(StringBuilder)</c>; a record class also has <c>EqualityContract</c>.</summary>
    private static void RequireRecord(Type input, Action<string> fail)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var isRecord = input.GetMethod("PrintMembers", flags, [typeof(StringBuilder)])?.ReturnType == typeof(bool)
            || input.GetProperty("EqualityContract", flags)?.PropertyType == typeof(Type);
        if (!isRecord)
        {
            fail($"input type {Display(input)} must be a record (record class or record struct).");
        }
    }

    /// <summary>
    /// Finds the public instance convention method and checks it returns <c>Task&lt;OneOf&lt;T0, E…&gt;&gt;</c> with
    /// T0 = <paramref name="t0"/> and every E deriving from <see cref="AppError"/> with <see cref="ErrorAttribute"/>.
    /// </summary>
    private static (MethodInfo Method, IReadOnlyList<Type> Errors)? ConventionMethod(
        Type type, string name, Type[] parameters, Type t0, Action<string> fail)
    {
        var expected = $"Task<OneOf<{Display(t0)}, ...>> {name}({string.Join(", ", parameters.Select(Display))})";
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, parameters);
        if (method is null)
        {
            fail($"must declare public method {expected}.");
            return null;
        }

        if (method.IsGenericMethodDefinition)
        {
            fail($"{name} must not be generic; declare {expected}.");
            return null;
        }

        var returnType = method.ReturnType;
        if (!returnType.IsGenericType
            || returnType.GetGenericTypeDefinition() != typeof(Task<>)
            || returnType.GenericTypeArguments[0] is not { IsGenericType: true } oneOf
            || !OneOfDefinitions.Contains(oneOf.GetGenericTypeDefinition()))
        {
            fail($"{name} must return Task<OneOf<{Display(t0)}, ...>>; found {Display(returnType)}.");
            return null;
        }

        var arguments = oneOf.GenericTypeArguments;
        var ok = true;
        if (arguments[0] != t0)
        {
            fail($"{name} must return Task<OneOf<{Display(t0)}, ...>>; T0 is {Display(arguments[0])}.");
            ok = false;
        }

        foreach (var error in arguments.Skip(1))
        {
            if (!typeof(AppError).IsAssignableFrom(error))
            {
                fail($"{name} error type {Display(error)} must derive from AppError.");
                ok = false;
            }
            else if (error.GetCustomAttribute<ErrorAttribute>(inherit: false) is null)
            {
                fail($"{name} error type {Display(error)} must declare [Error(code, kind, title)].");
                ok = false;
            }
        }

        return ok ? (method, arguments[1..]) : null;
    }

    /// <summary>
    /// Compiles <c>(object handler, args…) =&gt; ((DeclaringType)handler).Method((T)arg…)</c>. <see cref="ActionContext"/>
    /// parameters stay typed; every other parameter is passed as object. A <c>Task&lt;OneOf&lt;…&gt;&gt;</c> result is
    /// awaited and boxed to <see cref="IOneOf"/>.
    /// </summary>
    private static TDelegate Compile<TDelegate>(MethodInfo method) where TDelegate : Delegate
    {
        var handler = Expression.Parameter(typeof(object), "handler");
        var methodParameters = method.GetParameters();
        var lambdaParameters = methodParameters
            .Select(p => Expression.Parameter(p.ParameterType == typeof(ActionContext) ? typeof(ActionContext) : typeof(object), p.Name))
            .ToArray();
        var arguments = lambdaParameters.Zip(methodParameters, Expression (e, p) => e.Type == p.ParameterType ? e : Expression.Convert(e, p.ParameterType));

        Expression body = Expression.Call(Expression.Convert(handler, method.DeclaringType!), method, arguments);
        if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            body = Expression.Call(AwaitOneOfMethod.MakeGenericMethod(method.ReturnType.GenericTypeArguments[0]), body);
        }

        return Expression.Lambda<TDelegate>(body, [handler, .. lambdaParameters]).Compile();
    }

    private static async Task<IOneOf> AwaitOneOf<T>(Task<T> task) where T : IOneOf => await task;

    /// <summary>The handler as named in violations: namespace plus nested type chain.</summary>
    private static string DisplayHandler(Type type) => $"{type.Namespace}.{Display(type)}";

    /// <summary>C#-style short name: <c>Outer.Inner</c>, <c>Task&lt;OneOf&lt;A, B&gt;&gt;</c>.</summary>
    private static string Display(Type type)
    {
        var prefix = type.IsNested && !type.IsGenericParameter ? $"{Display(type.DeclaringType!)}." : "";
        if (!type.IsGenericType)
        {
            return prefix + type.Name;
        }

        var tick = type.Name.IndexOf('`', StringComparison.Ordinal);
        var name = tick < 0 ? type.Name : type.Name[..tick];
        // GetGenericArguments, not GenericTypeArguments: an open definition then renders as Handler<T>, not Handler<>.
        return $"{prefix}{name}<{string.Join(", ", type.GetGenericArguments().Select(Display))}>";
    }

    [GeneratedRegex(NameRule)]
    private static partial Regex NamePattern();
}
