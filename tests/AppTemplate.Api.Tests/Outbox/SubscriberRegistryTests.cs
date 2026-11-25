using AppTemplate.Api.Actions.Events;
using AppTemplate.Api.Actions.Outbox;
using AppTemplate.Api.Tests.Fixtures.Notes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.Tests.Outbox;

/// <summary>Subscriber names are stable keys on delivery rows: present, well formed and unique.</summary>
public sealed class SubscriberRegistryTests
{
    [Fact]
    public void Build_HandlerWithoutSubscriberAttribute_Throws()
    {
        var ex = Should.Throw<InvalidOperationException>(() => Build(s => s.AddEventHandler<UnnamedHandler>()));

        ex.Message.ShouldContain(nameof(UnnamedHandler));
    }

    [Fact]
    public void Build_DuplicateNames_Throws()
    {
        var ex = Should.Throw<InvalidOperationException>(() => Build(s =>
        {
            s.AddEventHandler<RecordingEventHandler>();
            s.AddEventHandler<DuplicateNameHandler>();
        }));

        ex.Message.ShouldContain(nameof(RecordingEventHandler));
        ex.Message.ShouldContain(nameof(DuplicateNameHandler));
    }

    [Fact]
    public void Build_MalformedName_Throws()
    {
        var ex = Should.Throw<InvalidOperationException>(() => Build(s => s.AddEventHandler<MalformedNameHandler>()));

        ex.Message.ShouldContain("Bad_Name");
    }

    [Fact]
    public void KeysFor_HandlerOfTwoEvents_ReturnsSameKeyForBoth()
    {
        var registry = Build(s => s.AddEventHandler<RecordingEventHandler>());

        registry.KeysFor(typeof(NoteCreated), "note.created").ShouldBe(["handler:note-recorder"]);
        registry.KeysFor(typeof(NoteEdited), "note.edited").ShouldBe(["handler:note-recorder"]);
        registry.KeysFor(typeof(NoteLockApplied), "note.locked").ShouldBeEmpty();
    }

    private static OutboxSubscriberRegistry Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        services.AddOutboxSubscribers();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<OutboxSubscriberRegistry>();
    }

    private sealed class UnnamedHandler : IDomainEventHandler<NoteCreated>
    {
        public Task Handle(NoteCreated domainEvent, CancellationToken ct) => Task.CompletedTask;
    }

    [Subscriber("note-recorder")]
    private sealed class DuplicateNameHandler : IDomainEventHandler<NoteCreated>
    {
        public Task Handle(NoteCreated domainEvent, CancellationToken ct) => Task.CompletedTask;
    }

    [Subscriber("Bad_Name")]
    private sealed class MalformedNameHandler : IDomainEventHandler<NoteCreated>
    {
        public Task Handle(NoteCreated domainEvent, CancellationToken ct) => Task.CompletedTask;
    }
}
