using AppTemplate.Domain.Common;
using Shouldly;

namespace AppTemplate.Api.Tests.Domain;

public sealed class AggregateRootTests
{
    private sealed record ThingHappened : IDomainEvent;

    private sealed class TestAggregate : AggregateRoot<Guid>
    {
        public void DoThing() => Raise(new ThingHappened());
    }

    [Fact]
    public void Raise_ThenClear_EmptiesEvents()
    {
        var a = new TestAggregate();
        a.DoThing();
        a.DomainEvents.Count.ShouldBe(1);
        a.ClearDomainEvents();
        a.DomainEvents.Count.ShouldBe(0);
    }

    [Fact]
    public void IncrementVersion_Twice_VersionIsTwo()
    {
        var a = new TestAggregate();
        ((IAggregateRoot)a).IncrementVersion();
        ((IAggregateRoot)a).IncrementVersion();
        a.Version.ShouldBe(2);
    }
}
