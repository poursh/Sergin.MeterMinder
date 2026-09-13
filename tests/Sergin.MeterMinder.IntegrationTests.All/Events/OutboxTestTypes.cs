using Sergin.SharedKernel.Application.Events.Integration;

namespace Sergin.MeterMinder.IntegrationTests.All.Events;

/// <summary>
/// The test-only integration event <see cref="TestAggregateCreated"/> translates to, and the translator
/// itself — the one per-event decision <c>OutboxWriter</c> looks up by the domain event's runtime type.
/// Registered by hand in <see cref="OutboxRelayTests"/>, the same way the module's own translators would be
/// registered by <c>AddSerginCore</c>'s assembly scan in a real host.
/// </summary>
[IntegrationEventName("test_events.aggregate.created.v1")]
internal sealed record TestAggregateCreatedIntegrationEvent(Guid Id, DateTime OccurredOnUtc, Guid AggregateId, string Name) : IIntegrationEvent;

internal sealed class TestAggregateCreatedTranslator : IIntegrationEventTranslator<TestAggregateCreated>
{
    public IIntegrationEvent Translate(TestAggregateCreated domainEvent)
        => new TestAggregateCreatedIntegrationEvent(domainEvent.Id, domainEvent.OccurredOnUtc, domainEvent.AggregateId, domainEvent.Name);
}
