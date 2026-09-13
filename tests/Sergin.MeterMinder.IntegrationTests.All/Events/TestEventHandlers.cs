using Sergin.SharedKernel.Application.Events;

namespace Sergin.MeterMinder.IntegrationTests.All.Events;

/// <summary>
/// Scoped scratch state shared between a test and the handlers running inside its scope: what the
/// handlers saw, and whether the next dispatch should blow up.
/// </summary>
internal sealed class RecordedEvents
{
    private readonly List<TestAggregateCreated> events = [];

    public IReadOnlyList<TestAggregateCreated> Events => events;

    public bool FailNext { get; set; }

    public void Add(TestAggregateCreated domainEvent) => events.Add(domainEvent);
}

internal sealed class RecordingHandler(RecordedEvents recorded) : IDomainEventHandler<TestAggregateCreated>
{
    public Task Handle(TestAggregateCreated domainEvent, CancellationToken cancellationToken)
    {
        recorded.Add(domainEvent);
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingHandler(RecordedEvents recorded) : IDomainEventHandler<TestAggregateCreated>
{
    public Task Handle(TestAggregateCreated domainEvent, CancellationToken cancellationToken)
    {
        if (recorded.FailNext)
        {
            throw new InvalidOperationException("Simulated domain event handler failure.");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Adds a second aggregate — which raises its own event — to the same scoped <see cref="TestEventsDbContext"/>
/// the originating save is running on, without calling <c>SaveChangesAsync</c> itself. Proves both that a
/// handler's writes ride on the same save and that events raised during dispatch are dispatched too.
/// </summary>
internal sealed class CascadingHandler(TestEventsDbContext context) : IDomainEventHandler<TestAggregateCreated>
{
    public const string ParentName = "parent";
    public const string ChildName = "child";

    public Task Handle(TestAggregateCreated domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.Name == ParentName)
        {
            context.Aggregates.Add(TestAggregate.Create(ChildName));
        }

        return Task.CompletedTask;
    }
}
