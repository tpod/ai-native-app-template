using System.Text.Json;
using System.Text.Json.Serialization;
using AppTemplate.Api.Actions.Events;
using AppTemplate.Api.Actions.Preview;
using AppTemplate.Api.Actions.Telemetry;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Actions;

public static class ActionServiceCollectionExtensions
{
    /// <summary>
    /// Registers the action registry, pipeline, preview tokens, event dispatcher, Api validators and the app's JSON options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="ActionRegistry"/> is a singleton built lazily on first resolve (normally by <c>MapActions</c>), so
    /// <see cref="ActionRegistryOptions"/> can still be configured after this call, e.g. by test fixtures. A rule
    /// violation then throws <see cref="ActionRegistrationException"/> during startup.
    /// </para>
    /// <para>
    /// <b>Validator probe:</b> <c>HasValidator</c> is decided by resolving <c>IValidator&lt;TInput&gt;</c> from a scope of
    /// the root provider, so it sees exactly what the pipeline will resolve per call, however the validator was registered.
    /// </para>
    /// <para>
    /// <b>Handlers:</b> every <c>[Action]</c>/<c>[Query]</c> type visible here (the configured assemblies and types) is
    /// registered as scoped. Types added to the options later are still served: the pipeline resolves a handler with
    /// <see cref="ActivatorUtilities.GetServiceOrCreateInstance(IServiceProvider, Type)"/>, constructing it from the
    /// phase scope when it is not registered.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddActions(this IServiceCollection services, Action<ActionRegistryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<ActionRegistryOptions>();
        var visible = new ActionRegistryOptions();
        if (configure is not null)
        {
            options.Configure(configure);
            configure(visible);
        }

        foreach (var handler in Candidates(visible).Where(IsHandler))
        {
            services.TryAddScoped(handler);
        }

        services.TryAddSingleton(sp =>
        {
            var registryOptions = sp.GetRequiredService<IOptions<ActionRegistryOptions>>().Value;
            using var scope = sp.CreateScope();
            return ActionRegistry.Build(
                Candidates(registryOptions),
                input => scope.ServiceProvider.GetService(typeof(IValidator<>).MakeGenericType(input)) is not null);
        });

        services.AddPreviewTokens();
        services.AddValidatorsFromAssembly(typeof(Program).Assembly);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ActionMetrics>();
        services.TryAddSingleton<ActionPipeline>();
        services.TryAddSingleton<ActionRunner>();
        services.TryAddScoped<DomainEventDispatcher>();

        // One JSON configuration for request bodies, responses and the input hash.
        // A missing constructor parameter without a default, or null for a non-nullable one, fails deserialization
        // (400 request.malformed) instead of binding null or Guid.Empty. Optional input fields need a default value.
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
            o.SerializerOptions.RespectRequiredConstructorParameters = true;
            o.SerializerOptions.RespectNullableAnnotations = true;
        });

        return services;
    }

    private static IEnumerable<Type> Candidates(ActionRegistryOptions options)
        => options.Assemblies.Distinct().SelectMany(a => a.GetTypes()).Concat(options.Types).Distinct();

    private static bool IsHandler(Type type)
        => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
            && (type.IsDefined(typeof(ActionAttribute), inherit: false) || type.IsDefined(typeof(QueryAttribute), inherit: false));
}
