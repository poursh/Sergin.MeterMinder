# Outbox: durable integration events across module and process boundaries

## Motivation

The domain event pipe dispatches in-transaction, in-module, and is proven by test-only
aggregates. Nothing crosses a module boundary or leaves the process. The earlier spec
deferred the outbox until the first such side effect; the user reviewed the pattern in
depth and chose to build the infrastructure now, proven by test-only events, so that the
first real integration event is a translator and a handler rather than a subsystem.

The dual-write problem it solves: a handler that must change this module's rows *and*
tell another module/process/system has two systems and no shared transaction; whichever
goes second is not guaranteed. The outbox turns the second write into a row in the first
system, written in the same `SaveChangesAsync`, and a relay delivers it later with
at-least-once semantics; consumers make it effectively-once through an inbox.

## Scope

**In scope:**
- The integration-event contracts (`IIntegrationEvent`, `IntegrationEventNameAttribute`,
  `IntegrationEventNotification<TEvent>`, `IIntegrationEventHandler<TEvent>`,
  `IIntegrationEventTranslator`/`IIntegrationEventTranslator<TDomainEvent>`) in
  `Sergin.SharedKernel.Application/Events/Integration/`.
- Outbox and inbox storage (`OutboxMessage`, `InboxMessage`, `IOutboxDbContext`,
  `ModelBuilder.ApplyOutbox()`) in `Sergin.SharedKernel.Infrastructure.Data.EFCore/Outbox/`.
- The producer path: `EventDispatcherInterceptor` writing an outbox row per translator
  result, in the same `SavingChangesAsync` that already dispatches domain events.
- The relay: `OutboxRelaySource<TContext>` claiming rows with `FOR UPDATE SKIP LOCKED`,
  dispatching them, applying backoff and dead-lettering, and purging old rows.
- The relay's hosting and configuration: `OutboxRelayService : BackgroundService` and
  `OutboxOptions`/`OutboxOptionsValidator` in `Sergin.SharedKernel.Hosts/Outbox/`.
- Consumer dedup: a per-module inbox table and `InboxIntegrationEventHandler<TEvent, TUnitOfWork>`.
- Correlation and causation propagation from the writer through the relay to the consumer.
- Composition wiring in `AddModuleDbContext` and `AddSerginCore`.
- `dm` and `ua` opting in to the outbox tables, one `AddOutbox` migration each.
- Proof by test-only integration events and handlers, the same shape as
  `DomainEventDispatchTests`.

**Explicitly out of scope:**
- No module raises a real integration event yet; nothing in DeviceManagement or
  UserAccess has an outbound reaction worth writing.
- No message broker, no cross-process transport, no `LISTEN/NOTIFY` — delivery is
  in-process only, through `IPublisher`.
- No `IOutbox<T>.Enqueue` escape hatch: the only way to produce an integration event is a
  translator reacting to a domain event.
- No new NuGet packages.
- No automatic or unconditional outbox mapping for a module that has not opted in —
  `ApplyOutbox()` is called by hand, per module.
- No change to `EventDispatcherInterceptor`'s synchronous `SavingChanges` guard or to
  domain-event semantics; the outbox is additive to that pipe, not a replacement of it.

## Current state

The domain event pipe is fixed and proven, per
`2026-09-13-domain-events-and-outbox-design.md`: `IDomainEvent`, `AggregateRoot`,
`DomainEventNotification<TEvent>`, `IDomainEventHandler<TEvent>`, `IEventDispatcher`,
`DefaultEventDispatcher`, and `EventDispatcherInterceptor` dispatch in-transaction,
in-module, proven by `DomainEventDispatchTests` against a test-only aggregate. No
aggregate in DeviceManagement or UserAccess raises an event yet.

Nothing exists for a domain event, or any other change, to reach another module or leave
the process: no integration event contract, no outbox table, no relay, no inbox. A
handler that needed to notify another module or an external system would have no durable
way to do it — exactly the dual-write problem described above.

## Design

### Two kinds of event

Domain event: in-module, in-transaction, domain-typed payload, lives in `.Domain`,
handled by `IDomainEventHandler<TEvent>`, free to change.

Integration event: crosses a module boundary, primitives only, versioned public
contract, lives in the producer module's `.Application.Contracts` (other modules already
reference that project for request/response shapes), written to the outbox by the
interceptor through a translator, delivered at-least-once.
`[IntegrationEventName("dm.device.registered.v1")]` is the stable wire name; the CLR type
name is never stored.

### Contracts — `Sergin.SharedKernel.Application/Events/Integration/`

```csharp
public interface IIntegrationEvent { Guid Id { get; } DateTime OccurredOnUtc { get; } }

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventNameAttribute(string name) : Attribute { public string Name { get; } = name; }

public sealed record IntegrationEventNotification<TEvent>(Guid MessageId, string CorrelationId, TEvent Event) : INotification
    where TEvent : IIntegrationEvent;

public static class IntegrationEventNotification
{
    public static INotification Wrap(Guid messageId, string correlationId, IIntegrationEvent integrationEvent); // closes the generic over integrationEvent.GetType(), cached — same reason as DomainEventNotification.Wrap
}

public interface IIntegrationEventHandler<TEvent> : INotificationHandler<IntegrationEventNotification<TEvent>>
    where TEvent : IIntegrationEvent
{
    Task Handle(TEvent integrationEvent, CancellationToken cancellationToken);
    Task INotificationHandler<IntegrationEventNotification<TEvent>>.Handle(IntegrationEventNotification<TEvent> notification, CancellationToken cancellationToken)
        => Handle(notification.Event, cancellationToken);
}

public interface IIntegrationEventTranslator { IIntegrationEvent Translate(IDomainEvent domainEvent); }

public interface IIntegrationEventTranslator<TDomainEvent> : IIntegrationEventTranslator
    where TDomainEvent : IDomainEvent
{
    IIntegrationEvent Translate(TDomainEvent domainEvent);
    IIntegrationEvent IIntegrationEventTranslator.Translate(IDomainEvent domainEvent) => Translate((TDomainEvent)domainEvent);
}

public interface IIntegrationEventSource { IEnumerable<Type> EventTypes { get; } }
public interface IIntegrationEventTypeRegistry { string NameOf(Type eventType); Type TypeOf(string name); }
public interface IIntegrationEventSerializer { string Serialize(IIntegrationEvent integrationEvent); IIntegrationEvent Deserialize(string typeName, string content); }
public interface IIntegrationEventDispatcher { Task DispatchAsync(Guid messageId, string correlationId, IIntegrationEvent integrationEvent, CancellationToken cancellationToken); }

public sealed class IntegrationEventContextAccessor { public string? CorrelationId { get; set; } public Guid? CausationMessageId { get; set; } } // scoped, seeded by the relay, read by the writer

public interface IOutboxRelayIdentity { IUserContext User { get; } }

public interface IInbox<TUnitOfWork> where TUnitOfWork : IUnitOfWork
{
    Task<bool> TryRecordAsync(Guid messageId, string handler, CancellationToken cancellationToken); // false = already recorded
}

public abstract class InboxIntegrationEventHandler<TEvent, TUnitOfWork>(IInbox<TUnitOfWork> inbox, TUnitOfWork unitOfWork) : IIntegrationEventHandler<TEvent>
    where TEvent : IIntegrationEvent where TUnitOfWork : IUnitOfWork
{
    async Task INotificationHandler<IntegrationEventNotification<TEvent>>.Handle(IntegrationEventNotification<TEvent> notification, CancellationToken cancellationToken)
    {
        if (!await inbox.TryRecordAsync(notification.MessageId, GetType().FullName!, cancellationToken)) return;
        await Handle(notification.Event, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
    public abstract Task Handle(TEvent integrationEvent, CancellationToken cancellationToken);
}
```

`IIntegrationEventDispatcher` exists because `.Infrastructure.Data.EFCore` has no MediatR
reference; `DefaultIntegrationEventDispatcher(IPublisher)` in
`Sergin.SharedKernel.Infrastructure/Events/Integration/` publishes
`IntegrationEventNotification.Wrap(...)`. `IntegrationEventTypeRegistry(IEnumerable<IIntegrationEventSource>)`
(same folder) builds name↔type both ways eagerly in its constructor and throws
`InvalidOperationException` naming the offending type(s) on a missing
`[IntegrationEventName]` or a duplicate name. `AssemblyIntegrationEventSource(Assembly)`
yields every non-abstract, non-generic, non-nested type implementing `IIntegrationEvent`;
`TypesIntegrationEventSource(params Type[])` yields exactly the given types (tests use
it). `JsonIntegrationEventSerializer` uses `System.Text.Json` with web defaults
(camelCase) and resolves the CLR type through the registry on deserialize.

### Storage — `Sergin.SharedKernel.Infrastructure.Data.EFCore/Outbox/`

`IOutboxDbContext { DbSet<OutboxMessage> OutboxMessages { get; } DbSet<InboxMessage> InboxMessages { get; } }`
and `ModelBuilder ApplyOutbox(this ModelBuilder)` mapping both tables into the model's
default schema.

`outbox_messages` columns (snake_case by convention): `id uuid PK` (message id,
`Guid.CreateVersion7()`, distinct from the event's own `Id`), `type text`,
`content jsonb`, `occurred_on_utc timestamptz`, `correlation_id text`,
`causation_id uuid null`, `attempts int default 0`, `next_attempt_at timestamptz null`,
`processed_on_utc timestamptz null`, `error text null` (truncated to 4000 chars). Partial
index on `id` filtered `processed_on_utc IS NULL`. `OutboxMessage` is a sealed class with
a private constructor, `static Create(...)`, `MarkProcessed(DateTime nowUtc)`,
`MarkFailed(DateTime nowUtc, string error, TimeSpan backoff)` (increments `attempts`,
sets `error`, `next_attempt_at = nowUtc + backoff`). It is not an aggregate root and
raises nothing, so the interceptor loop terminates.

`inbox_messages`: `message_id uuid`, `handler text`, `processed_on_utc timestamptz`;
composite PK `(message_id, handler)`. `EfInbox<TContext> : IInbox<TUnitOfWork>` checks
`AnyAsync` then `Add`s; it never saves — the handler base saves, so the inbox row and the
side effect commit together. Two relays racing the same message: the second
`SaveChangesAsync` hits the PK violation, the relay records an attempt, and the next
attempt finds the inbox row and skips.

### Producer path — the interceptor writes the row

`EventDispatcherInterceptor` takes a second dependency, `IOutboxWriter` (internal,
`.Infrastructure.Data.EFCore/Outbox/`). Inside the existing `SavingChangesAsync` loop,
after clearing the roots' events and **before** `DispatchAllAsync`, it calls
`outboxWriter.Write(context, domainEvents)`. For each domain event the writer resolves
`IEnumerable<IIntegrationEventTranslator<T>>` for the event's runtime type (closed type
cached in a `ConcurrentDictionary<Type, Type>`); with no translator it skips the event;
with translators and a context that is not `IOutboxDbContext` it throws
`InvalidOperationException` naming the context type and the event type and telling the
module to call `ApplyOutbox()`; otherwise it adds one `OutboxMessage` per translator
result. `type` comes from the registry, `content` from the serializer,
`occurred_on_utc` from the event, `correlation_id`/`causation_id` from
`IntegrationEventContextAccessor` as described under identity, `now` from
`IDateTimeProvider`. Translators are discovered by `AddSerginCore` scanning every local
module's `ApplicationAssembly` for classes implementing a closed
`IIntegrationEventTranslator<>` and registering them transient. A translator that throws
aborts the save exactly as a handler that throws does.

### Relay — `Sergin.SharedKernel.Infrastructure.Data.EFCore/Outbox/OutboxRelaySource<TContext>`

`IOutboxRelaySource` (public): `string Schema { get; }`, `Task<int> RelayOnceAsync(CancellationToken)`,
`Task PurgeAsync(CancellationToken)`. `OutboxRelaySource<TContext>` (internal, singleton)
is registered by `AddModuleDbContext` when `TContext : IOutboxDbContext`, alongside
`IInbox<TIUnitOfWork>`.

`RelayOnceAsync`: open a relay scope, resolve `TContext`, `BeginTransactionAsync`, claim
with `OutboxMessages.FromSqlRaw(claimSql, now, maxAttempts, batchSize).ToListAsync()`
where `claimSql` is
`SELECT * FROM "<schema>"."outbox_messages" WHERE processed_on_utc IS NULL AND attempts < {1} AND (next_attempt_at IS NULL OR next_attempt_at <= {0}) ORDER BY id LIMIT {2} FOR UPDATE SKIP LOCKED`
built once from EF metadata. Per row: open a fresh consumer scope, seed
`UserContextAccessor.Current = relayIdentity.User` and
`IntegrationEventContextAccessor { CorrelationId = row.CorrelationId, CausationMessageId = row.Id }`,
deserialize, resolve `IIntegrationEventDispatcher` from that scope, dispatch. Success →
`MarkProcessed(now)`. Exception → `MarkFailed(now, exception.ToString(), backoff)` with
backoff `min(2^attempts, 300)` seconds, log a warning, continue with the next row. Then
`SaveChangesAsync` on the relay context and commit; return the claimed count.
`PurgeAsync` bulk-deletes outbox rows with `processed_on_utc < now - Retention` and
inbox rows with the same cutoff.

### Relay host service and options — `Sergin.SharedKernel.Hosts/Outbox/`

`OutboxOptions` (bound from `Sergin:Outbox`): `PollInterval` 00:00:05, `BatchSize` 20,
`MaxAttempts` 10, `Retention` 7.00:00:00, `PurgeInterval` 01:00:00. Validated at startup
by `OutboxOptionsValidator` (positive intervals, `BatchSize >= 1`, `MaxAttempts >= 1`,
messages naming `Sergin:Outbox:<Key>`).

`OutboxRelayService : BackgroundService` (`Sergin.SharedKernel.Hosts/Outbox/`):
`StartAsync` resolves `IIntegrationEventTypeRegistry` first so a bad registry fails host
start, then `ExecuteAsync` runs, per source, one loop task (`RelayOnceAsync`; if claimed
< `BatchSize`, delay `PollInterval`; exceptions caught and logged so a database outage
does not kill the loop) and one purge task (every `PurgeInterval`); all awaited with
`Task.WhenAll`, cancellation on shutdown swallowed. In Development the service starts
after `UseSerginWebUiAsync` has applied migrations.

### Consumer path

Handlers implement `IIntegrationEventHandler<TEvent>` in the consuming module's
`.Application` (found by the MediatR scan) and reference the producer's
`.Application.Contracts` for the event type. Deriving from
`InboxIntegrationEventHandler<TEvent, TUnitOfWork>` gives inbox dedup and a save in one
transaction; a plain implementation owns its idempotency. A consumer may `ISender.Send`
a command; it passes `PermissionCheckPipelineBehavior` because the relay identity is a
system admin.

### Identity, correlation and causation

`OutboxRelayIdentity : IOutboxRelayIdentity` (Hosts) exposes `RelayUserContext : IUserContext`
with `Id = new UserId(Guid.Parse("01920000-0000-7000-8000-00000000000f"))`,
`UserName = "outbox-relay"`, `FirstName = "Outbox"`, `LastName = "Relay"`,
`Email = "outbox-relay@sergin.local"`, `Permissions = [Permission.AllPlatform]` — so
`IsSystemAdmin` is true and `HasPermission` passes for everything. The writer's
correlation id: `IntegrationEventContextAccessor.CorrelationId`, else
`Activity.Current?.TraceId.ToString()`, else `Guid.CreateVersion7().ToString()`;
causation: `IntegrationEventContextAccessor.CausationMessageId`. Consequence:
`message A → consumer → command → domain event → message B` gives B
`causation_id = A.id` and A's correlation id.

### Composition

`AddModuleDbContext<TContext, TIContext, TIUnitOfWork>`: when
`typeof(IOutboxDbContext).IsAssignableFrom(typeof(TContext))`, also
`AddScoped<IInbox<TIUnitOfWork>>` (over `TContext`) and
`AddSingleton<IOutboxRelaySource, OutboxRelaySource<TContext>>()`.

`AddSerginCore` additions: `AddOptions<OutboxOptions>().Bind(serginSection.GetSection("Outbox")).ValidateOnStart()`
with `OutboxOptionsValidator`; `TryAddSingleton<IDateTimeProvider, DefaultDateTimeProvider>()`
(it existed but nothing registered it); one `IIntegrationEventSource` per local and
remote module's `ContractsAssembly`; `IIntegrationEventTypeRegistry`,
`IIntegrationEventSerializer` singletons; `IIntegrationEventDispatcher`,
`IntegrationEventContextAccessor`, `IOutboxWriter` scoped; translator scan over local
modules' `ApplicationAssembly`; `IOutboxRelayIdentity` singleton;
`AddHostedService<OutboxRelayService>()`.

### What does not change

`IDomainEvent`, `AggregateRoot`, `DomainEventNotification`, `IDomainEventHandler`,
`IEventDispatcher`, `DefaultEventDispatcher`; the interceptor's sync `SavingChanges`
guard; the pipeline behaviors; `IUnitOfWork`. No new NuGet packages in either
`Directory.Packages.props`.

## Modules

`DeviceManagementDbContext` and `UserAccessDbContext` implement `IOutboxDbContext`
(`OutboxMessages => Set<OutboxMessage>()`, `InboxMessages => Set<InboxMessage>()`), call
`modelBuilder.ApplyOutbox()` after `ApplyConfigurationsFromAssembly`, and each gains a
migration named `AddOutbox`. Neither module declares a translator, integration event or
handler yet.

## Testing

`tests/Sergin.MeterMinder.IntegrationTests.All/Events/`: `TestEventsDbContext` opts in;
`OutboxTestTypes.cs` holds `TestAggregateCreatedIntegrationEvent`
(`[IntegrationEventName("test_events.aggregate.created.v1")]`,
`(Guid Id, DateTime OccurredOnUtc, Guid AggregateId, string Name)`),
`TestAggregateCreatedTranslator`, singleton `RecordedIntegrationEvents`,
`RecordingIntegrationHandler : InboxIntegrationEventHandler<…, ITestEventsUnitOfWork>`,
`ThrowingIntegrationHandler` gated by singleton `FailureSwitch`, `ChainingIntegrationHandler`
that sends `CreateChildAggregateCommand`
(`[RequiredPermissions("permission.test-events.aggregates.write")]`) through `ISender`.
`OutboxRelayTests` registers the test assembly as an `IIntegrationEventSource`, the
translator and handlers by hand, and sets `Sergin:Outbox:PollInterval` to 500 ms. Cases,
by name:

1. `SaveChangesAsync_WithTranslator_WritesOutboxRow_InSameSave` — raising a domain event
   that has a registered translator writes exactly one `outbox_messages` row in the same
   `SaveChangesAsync` that persists the aggregate.
2. `SaveChangesAsync_WhenDomainHandlerThrows_WritesNoOutboxRow` — a domain event handler
   throwing aborts the save before any statement runs, so neither the aggregate nor the
   outbox row is persisted.
3. `RelayOnce_DeliversToHandler_AndStampsProcessed` — `RelayOnceAsync` claims a pending
   row, dispatches it to `RecordingIntegrationHandler`, and stamps `processed_on_utc` on
   success.
4. `RelayOnce_WhenHandlerThrows_RecordsAttemptAndBackoff` — with `FailureSwitch` tripping
   `ThrowingIntegrationHandler`, the relay catches the exception, calls `MarkFailed`
   (attempts incremented, error recorded, `next_attempt_at` pushed out), and does not
   propagate.
5. `RelayOnce_RedeliveredMessage_IsSkippedByInbox` — redelivering the same message id to
   `RecordingIntegrationHandler` a second time is a no-op: the inbox row already exists,
   so the handler's side effect is not re-applied.
6. `RelayOnce_AtMaxAttempts_IsDeadLettered` — a row with `attempts >= MaxAttempts` is no
   longer claimed by `RelayOnceAsync`.
7. `RelayOnce_ConsumerRunsAsRelayUser_AndChainsCausation` — `ChainingIntegrationHandler`'s
   `ISender.Send(CreateChildAggregateCommand)` passes `PermissionCheckPipelineBehavior`
   because the relay seeded the relay identity, and the outbox row written for the
   resulting domain event carries `causation_id` equal to the consumed message's id and
   the same correlation id.
8. `BackgroundRelay_DeliversWithoutManualTrigger` — with `OutboxRelayService` running at
   its configured `PollInterval` (500 ms), a message is delivered without the test
   calling `RelayOnceAsync` directly.
9. `Purge_RemovesProcessedRowsPastRetention` — `PurgeAsync` removes processed outbox and
   inbox rows older than `Retention` and leaves unprocessed or recent rows alone.
10. `HostStart_WithUnnamedIntegrationEvent_Throws` — an unnamed nested event type
    supplied through `TypesIntegrationEventSource` makes `IntegrationEventTypeRegistry`'s
    constructor throw at host start, before any message is ever published.

The rest of the suite, including `DomainEventDispatchTests`, stays green.

## Documentation

Root `.claude/CLAUDE.md` gains an **Outbox** bullet and the Domain events bullet loses
its "no outbox yet" tail; `src/SharedKernel/.claude/CLAUDE.md` names the new types per
project; both module `CLAUDE.md` files note the opt-in and the `AddOutbox` migration.
Three repositories are touched: SharedKernel changes land via its own branch and PR,
UserAccess via its own, and the tests, spec, migrations for `dm`, CLAUDE.md and
submodule bumps land in `Sergin.MeterMinder`.

## Decisions recorded

- Proof by test-only events (same shape as `DomainEventDispatchTests`); no module raises
  an integration event yet.
- Opt-in tables per module through `ApplyOutbox()`; `dm` and `ua` both opt in now, one
  migration each. Unconditional mapping in `SerginDbContext` was rejected because EF Core
  throws `PendingModelChangesWarning` at startup for any module that has not migrated,
  including test contexts.
- Full production row: retry columns (`attempts`, `next_attempt_at`, `error`) and tracing
  columns (`correlation_id`, `causation_id`) from the start, because adding a column
  later is a migration in every module.
- Hand-rolled over MassTransit (v9 commercially licensed, bus abstraction in every
  handler), Wolverine (replaces MediatR), CAP; per-module tables over one shared table
  (module owns its schema; extraction-safe).
- Interceptor + translator over an explicit `IDomainEventHandler` that enqueues: the
  outbox write becomes infrastructure policy that cannot be forgotten; the translator is
  the one per-event decision about what leaks out of the module. Serializing domain
  events raw was rejected: it leaks domain types into `content`, forces consumers to
  reference the producer's `.Domain`, and ties the public contract to internal renames.
  No `IOutbox<T>.Enqueue` escape hatch this round.
- Consumer dedup through a per-module inbox table keyed `(message_id, handler)`, written
  in the consumer's own transaction by an `InboxIntegrationEventHandler<TEvent, TUnitOfWork>`
  base; plain `IIntegrationEventHandler<TEvent>` remains available for naturally
  idempotent handlers.
- Relay failure policy: a failed row gets exponential backoff and is skipped; later rows
  overtake it (no head-of-line blocking); dead letter = `attempts >= MaxAttempts` and
  unprocessed. One sequential loop per module, fresh consumer DI scope per message.
- In-process delivery only (`IPublisher`); no broker, no cross-process transport, no
  `LISTEN/NOTIFY`.
- Correlation/causation: correlation from the ambient consumer scope, else
  `Activity.Current?.TraceId`, else a new v7 guid; causation = the message being
  consumed when the row was written, else null.
