using AppTemplate.Api.Actions.Events;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppTemplate.Api.Actions.Outbox;

public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers an event handler as an outbox subscriber for every <see cref="IDomainEventHandler{TEvent}"/> it
    /// implements. The class needs <see cref="SubscriberAttribute"/>. Delivery is at least once: handlers must be
    /// idempotent.
    /// </summary>
    public static IServiceCollection AddEventHandler<THandler>(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where THandler : class
    {
        var eventTypes = typeof(THandler).GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToList();
        if (eventTypes.Count == 0)
        {
            throw new InvalidOperationException($"{typeof(THandler).FullName} implements no IDomainEventHandler<TEvent>.");
        }

        services.TryAdd(new ServiceDescriptor(typeof(THandler), typeof(THandler), lifetime));
        services.AddSingleton(new SubscriberDescriptor(SubscriberKind.Handler, typeof(THandler), eventTypes));

        // Transitional: the in-process dispatcher still resolves the handler interfaces until the outbox worker lands.
        foreach (var eventType in eventTypes)
        {
            var handlerInterface = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            services.Add(new ServiceDescriptor(handlerInterface, sp => sp.GetRequiredService<THandler>(), lifetime));
        }

        return services;
    }

    /// <summary>The subscriber registry, built from every <c>AddEventHandler</c> and <c>AddEventPublisher</c> call.</summary>
    public static IServiceCollection AddOutboxSubscribers(this IServiceCollection services)
    {
        services.TryAddSingleton(sp => new OutboxSubscriberRegistry(sp.GetServices<SubscriberDescriptor>()));
        return services;
    }

    internal static IServiceCollection AddOutbox(this IServiceCollection services)
    {
        services.AddOptions<OutboxOptions>().BindConfiguration(OutboxOptions.Section);
        services.AddOutboxSubscribers();
        return services;
    }
}
