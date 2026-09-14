using ErrorOr;
using MediatR;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Events.Integration;
using Sergin.SharedKernel.Application.Securities.Authorization;

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

/// <summary>
/// Singleton, unlike <see cref="RecordedEvents"/>: the relay delivers each message in a scope of its own, so
/// a scoped recorder would never be the instance the test holds. Records what consumer handlers saw, plus the
/// causation and correlation the seeded <see cref="IntegrationEventContextAccessor"/> carried at the time.
/// </summary>
internal sealed class RecordedIntegrationEvents
{
    private readonly object gate = new();
    private readonly List<(Guid? CausationMessageId, string? CorrelationId, TestAggregateCreatedIntegrationEvent Event)> events = [];

    public IReadOnlyList<(Guid? CausationMessageId, string? CorrelationId, TestAggregateCreatedIntegrationEvent Event)> Events
    {
        get
        {
            lock (gate)
            {
                return [.. events];
            }
        }
    }

    public void Add(Guid? causationMessageId, string? correlationId, TestAggregateCreatedIntegrationEvent integrationEvent)
    {
        lock (gate)
        {
            events.Add((causationMessageId, correlationId, integrationEvent));
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            events.Clear();
        }
    }
}

/// <summary>
/// Singleton switch a test flips to make <see cref="ThrowingIntegrationHandler"/> fail on the next delivery.
/// </summary>
internal sealed class FailureSwitch
{
    public bool ShouldThrow { get; set; }
}

/// <summary>
/// The inbox-deduped consumer. Records the accessor's values alongside the event so a test can prove the
/// relay seeded them on the consumer scope before dispatching.
/// </summary>
internal sealed class RecordingIntegrationHandler(
    IInbox<ITestEventsUnitOfWork> inbox,
    ITestEventsUnitOfWork unitOfWork,
    RecordedIntegrationEvents recorded,
    IntegrationEventContextAccessor eventContext)
    : InboxIntegrationEventHandler<TestAggregateCreatedIntegrationEvent, ITestEventsUnitOfWork>(inbox, unitOfWork)
{
    public override Task Handle(TestAggregateCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        recorded.Add(eventContext.CausationMessageId, eventContext.CorrelationId, integrationEvent);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A plain (no inbox) consumer that fails on demand, so a test can watch the relay record the attempt.
/// </summary>
internal sealed class ThrowingIntegrationHandler(FailureSwitch failure) : IIntegrationEventHandler<TestAggregateCreatedIntegrationEvent>
{
    public Task Handle(TestAggregateCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (failure.ShouldThrow)
        {
            throw new InvalidOperationException("Simulated integration event handler failure.");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// A permissioned command no configured user holds the permission for — only the relay identity, which is a
/// system admin, passes <c>PermissionCheckPipelineBehavior</c> for it.
/// </summary>
[RequiredPermissions("permission.test-events.aggregates.write")]
internal sealed record CreateChildAggregateCommand(string Name) : ICommand<Guid>;

internal sealed class CreateChildAggregateCommandHandler(TestEventsDbContext context) : ICommandHandler<CreateChildAggregateCommand, Guid>
{
    public async Task<ErrorOr<Guid>> Handle(CreateChildAggregateCommand command, CancellationToken cancellationToken)
    {
        var aggregate = TestAggregate.Create(command.Name);
        context.Aggregates.Add(aggregate);
        await context.SaveChangesAsync(cancellationToken);
        return aggregate.Id;
    }
}

/// <summary>
/// A consumer that dispatches a permissioned command through the real MediatR pipeline. It only passes
/// because the relay seeded the relay identity into the consumer scope; the resulting child aggregate raises
/// its own domain event, which the translator turns into a second outbox row whose causation must be the
/// message being consumed.
/// </summary>
internal sealed class ChainingIntegrationHandler(ISender sender) : IIntegrationEventHandler<TestAggregateCreatedIntegrationEvent>
{
    public const string ParentName = "chain-parent";
    public const string ChildName = "chain-child";

    public async Task Handle(TestAggregateCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Name != ParentName)
        {
            return;
        }

        ErrorOr<Guid> result = await sender.Send(new CreateChildAggregateCommand(ChildName), cancellationToken);

        if (result.IsError)
        {
            throw new InvalidOperationException($"Chained command failed: {result.FirstError.Code}");
        }
    }
}
