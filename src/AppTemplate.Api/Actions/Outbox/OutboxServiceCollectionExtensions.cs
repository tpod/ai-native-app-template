using AppTemplate.Api.Actions.Events;
using AppTemplate.Api.Data;
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
        return services;
    }

    /// <summary>Registers an external publisher as an outbox subscriber. The class needs <see cref="SubscriberAttribute"/>.</summary>
    public static IServiceCollection AddEventPublisher<TPublisher>(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TPublisher : class, IEventPublisher
    {
        services.TryAdd(new ServiceDescriptor(typeof(TPublisher), typeof(TPublisher), lifetime));
        services.AddSingleton(new SubscriberDescriptor(SubscriberKind.Publisher, typeof(TPublisher), []));
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
        services.TryAddScoped<OutboxStore>();
        services.TryAddScoped<OutboxDeliveryContext>();
        services.TryAddSingleton<OutboxProcessor>();
        services.AddHostedService<OutboxWorker>();
        return services;
    }
}
