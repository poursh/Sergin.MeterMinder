# Domain event pipe fix, and the outbox decision

## Motivation

The repo ships domain-event infrastructure that nothing uses yet: `AggregateRoot.Raise`,
`IDomainEvent`, `IEventDispatcher`, and `EventDispatcherInterceptor` hooked into every
module `DbContext` by `AddModuleDbContext`. No aggregate calls `Raise(...)`, no event type
exists, no handler exists. Investigating whether the platform needs an outbox turned up
two latent bugs in that pipe — both would surface the moment the first aggregate raises
an event:

1. **Wrong MediatR verb.** `DefaultEventDispatcher` calls `ISender.Send(@event)`.
   `IDomainEvent` does not implement `IRequest`, so MediatR 12's `Send(object)` overload
   throws `ArgumentException: "<Type> does not implement IRequest"` at save time. Even
   with `IRequest` bolted on, `Send` means exactly one handler and throws when none is
   registered. Domain events want `IPublisher.Publish` and `INotification` — zero or
   many handlers, an event nobody listens to is fine.
2. **Dispatch after commit, in memory only.** `EventDispatcherInterceptor` overrides
   `SavedChangesAsync`, which EF Core raises after the transaction has committed. So:
   a handler that throws surfaces its exception from `SaveChangesAsync` to the command
   handler *after* the row is durable — caller sees failure, database says success; a
   process crash between commit and dispatch loses the event, since it lived only on the
   aggregate; a handler that writes through another module's `DbContext` runs a second,
   unrelated transaction; a handler that calls an external system (SMTP, a broker, a
   meter gateway) gets no retry and no durability.

Both bugs are independent of whether an outbox exists. The outbox question itself is
answered in its own section below: **not now, but here is the shape, and the fixed pipe
is deliberately the hook an outbox plugs into.**

## Scope

**In scope:**
- Replace the `ISender`-based dispatch with `IPublisher`/`INotification`, without giving
  `Sergin.SharedKernel.Domain` a MediatR reference.
- Move dispatch from after-commit (`SavedChangesAsync`) to before-save
  (`SavingChangesAsync`), inside the originating transaction.
- Fail loudly on the synchronous `SaveChanges` path when events are pending, instead of
  silently dropping them.
- Regression tests in the host test project, with a test-only aggregate and `DbContext`.
- Record the outbox decision (defer) and the shape it will take.
- Update the root `CLAUDE.md` and SharedKernel's `.claude/CLAUDE.md` to describe the
  fixed pipe.

**Explicitly out of scope for this round:**
- No outbox table, poller, serializer, or inbox/idempotency store.
- No message broker, no MassTransit/Wolverine/CAP.
- No first "real" domain event in DeviceManagement or UserAccess — nothing has a
  reaction worth writing yet, and a no-op handler proves nothing.
- No loop-depth cap in the interceptor. A handler that raises forever is a bug either
  way; add a cap only if it bites.

Two repositories are touched. The contracts, dispatcher and interceptor live in the
`Sergin.SharedKernel` submodule (`src/SharedKernel`) and land there via a branch and
pull request; the tests, this spec, the root `CLAUDE.md` and the submodule pointer bump
land in `Sergin.MeterMinder`.

## Current state

```
Sergin.SharedKernel.Domain/
  IDomainEvent.cs                 Guid Id, DateTime OccurredOnUtc — no MediatR
  AggregateRoot.cs                Raise / DomainEvents (copy) / ClearDomainEvents

Sergin.SharedKernel.Application/Events/
  IEventDispatcher.cs             DispatchAsync(IDomainEvent) + default DispatchAllAsync

Sergin.SharedKernel.Infrastructure/Events/
  DefaultEventDispatcher.cs       ISender.Send(@event)              <- bug 1

Sergin.SharedKernel.Infrastructure.Data.EFCore/
  Interceptors/EventDispatcherInterceptor.cs
                                  SavedChangesAsync: collect, dispatch, clear   <- bug 2
  ModuleDbContextExtensions.cs    AddModuleDbContext(...).AddInterceptors(interceptor)

Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs
  AddScoped<IEventDispatcher, DefaultEventDispatcher>()
  AddScoped<EventDispatcherInterceptor>()
```

The interceptor also enumerates a lazy `IEnumerable<IAggregateRoot>` twice (once to
collect events, once to clear them). It happens to work because `DomainEvents` returns a
fresh copy each time, but it is fragile; the rewrite materializes.

## Design

### Contracts — `Sergin.SharedKernel.Application/Events/`

`IDomainEvent` stays exactly as it is. `Sergin.SharedKernel.Domain` is the leaf of the
dependency graph and gets no MediatR reference; the MediatR shape is added one layer up,
in the same project that already wraps `IRequest`/`IRequestHandler` behind
`ICommand`/`ICommandHandler` for commands.

**`DomainEventNotification<TEvent>`** — the MediatR envelope:

```csharp
public sealed record DomainEventNotification<TEvent>(TEvent Event) : INotification
    where TEvent : IDomainEvent;

public static class DomainEventNotification
{
    public static INotification Wrap(IDomainEvent domainEvent) { ... }
}
```

`Wrap` closes `DomainEventNotification<>` over `domainEvent.GetType()` (cached in a
`ConcurrentDictionary<Type, Type>`) and instantiates it. It exists because the
interceptor only ever holds `IDomainEvent` — the static type is gone by then — and the
handler resolution key must be the concrete event type, not the interface.

**`IDomainEventHandler<TEvent>`** — what a module implements:

```csharp
public interface IDomainEventHandler<TEvent> : INotificationHandler<DomainEventNotification<TEvent>>
    where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken);

    Task INotificationHandler<DomainEventNotification<TEvent>>.Handle(
        DomainEventNotification<TEvent> notification, CancellationToken cancellationToken)
        => Handle(notification.Event, cancellationToken);
}
```

The default explicit implementation unwraps the envelope, so a module writes
`internal sealed class DeviceRegisteredHandler : IDomainEventHandler<DeviceRegistered>`
with a `Handle(DeviceRegistered, CancellationToken)` and never names the envelope.
`AddMediatR`'s `RegisterServicesFromAssembly` discovers the class through the inherited
`INotificationHandler<>` interface, the same way it discovers `ICommandHandler<,>`
implementations through `IRequestHandler<,>` today. No `in` variance on `TEvent`: the
envelope record is invariant, and MediatR never needs it.

`IEventDispatcher` is unchanged, including its default `DispatchAllAsync`.

### Dispatcher — `Sergin.SharedKernel.Infrastructure/Events/DefaultEventDispatcher.cs`

Takes `IPublisher` instead of `ISender`; `DispatchAsync` is
`publisher.Publish(DomainEventNotification.Wrap(@event), cancellationToken)`. `IPublisher`
is already registered by the `AddMediatR` call in `AddSerginCore`, and the existing
`AddScoped<IEventDispatcher, DefaultEventDispatcher>()` /
`AddScoped<EventDispatcherInterceptor>()` registrations stay as they are — no host
composition changes.

MediatR's default notification publisher (`ForeachAwaitPublisher`) runs handlers
sequentially and stops at the first exception. That is the behaviour this design wants:
combined with in-transaction dispatch below, a failing handler aborts the save.

### Interceptor — `EventDispatcherInterceptor`

The `SavedChangesAsync` override is replaced by `SavingChangesAsync`, which EF Core
raises before it opens the transaction and generates any SQL. Inside it:

1. Materialize every tracked `IAggregateRoot` whose `DomainEvents.Count > 0`.
2. If there are none, stop.
3. Materialize their events, then `ClearDomainEvents()` on each root **before**
   dispatching — so a handler that raises again on the same root feeds the next
   iteration instead of being re-dispatched.
4. `DispatchAllAsync(events)`.
5. Loop back to 1 — a handler may raise further events or add new aggregates to the
   context.

Then `base.SavingChangesAsync(...)` runs and EF saves everything, including whatever the
handlers added, in the one transaction `SaveChangesAsync` already owns.

This gives the semantics an in-module domain event should have:

- **Same scope, same `DbContext`.** A handler resolving the module's `I<X>Repository`
  gets the same scoped `DbContext` instance the command handler is using, so entities it
  adds ride on this save.
- **Atomic.** A handler exception propagates out of `SaveChangesAsync` before a single
  statement has run. Nothing is persisted; the command fails as a unit.
- **Handlers never call `IUnitOfWork.SaveChangesAsync`.** The originating save persists
  their work. A nested save would open a second transaction and re-enter the
  interceptor; there is nothing for it to do, but it breaks the one-transaction
  guarantee.
- **Pipeline behaviours do not apply.** `PermissionCheckPipelineBehavior` and
  `ValidationPipelineBehavior` are `IPipelineBehavior`s around requests; notifications
  bypass them. A handler runs with whatever authority the command that triggered it
  already passed — by design.

The synchronous `SavingChanges` override is added too, and it **throws
`InvalidOperationException`** naming the aggregate type when any tracked root has pending
events, telling the caller to use `SaveChangesAsync`. The dispatcher is async-only; the
alternative — dropping the events on the floor because the caller used the sync API — is
exactly the silent loss this change exists to remove. Nothing in the repo calls the sync
`SaveChanges` today.

### What does not change

- `IUnitOfWork`, `AddModuleDbContext`, the module `DbContext`s and migrations.
- `AggregateRoot`: `Raise`/`DomainEvents`/`ClearDomainEvents` keep their shapes. The
  `DomainEvents` getter still returns a copy; the interceptor materializes once per
  iteration, so the extra allocation is bounded.
- Nothing in DeviceManagement or UserAccess. No aggregate raises anything yet; the
  guidance for when one does is in the CLAUDE.md updates.

## The outbox decision

**Superseded the same day.** The deferral below was reversed after review; the outbox is
specified in [2026-09-13-outbox-design.md](2026-09-13-outbox-design.md), which keeps the
shape recorded here where it still fits (per-module tables, `FOR UPDATE SKIP LOCKED`
relay, idempotent consumers, hand-rolled) and changes one thing: the outbox row is
written by the interceptor through a per-event translator, not by a domain event
handler. The text below is kept as history.

**Not now.** The outbox pattern makes "state changed" and "event recorded" one atomic
write, and gives at-least-once delivery to whatever consumes the event afterwards. It
earns its keep when a side effect must survive the originating process — a handler in
*another* module (another `DbContext`, eventually another process once a module goes
Remote), or an external system. Today there is no producer, no cross-module reaction,
and no external side effect. Building it now means a table, a poller and a serializer
that nothing exercises, and a contract nobody can integration-test honestly.

**Trigger to build it:** the first cross-module reaction (for example UserAccess's
`UserDeactivated` revoking device assignments in DeviceManagement) or the first external
side effect (email, broker, meter gateway, billing export). The HES domain — reading
ingestion, alarms, commands to meters — is full of these, so the trigger is a matter of
when, not if.

**Why this change is the right preparation.** In-transaction dispatch is precisely the
hook an outbox needs: an `IDomainEventHandler<X>` that maps the domain event to an
integration event and inserts an `outbox_messages` row through the same scoped
`DbContext` is atomic with the aggregate change for free. Nothing about the dispatcher or
interceptor has to change again when the outbox arrives; only a handler and a table are
added.

**Shape when built** (recorded so the decision is not re-derived later):

- Two kinds of event, on purpose. A **domain event** is in-module, in-transaction,
  handled by `IDomainEventHandler<TEvent>` as designed above. An **integration event**
  is what crosses a module boundary or leaves the process; it is written to the outbox by
  a domain-event handler, never published directly.
- One outbox table per module schema — `dm.outbox_messages`, `ua.outbox_messages` —
  owned by that module's `DbContext` and migrations, like every other table:
  `id uuid`, `type text`, `content jsonb`, `occurred_on_utc timestamptz`,
  `processed_on_utc timestamptz null`, `error text null`. `IDomainEvent.Id` and
  `OccurredOnUtc` already exist; they become the row's identity and ordering.
- A `BackgroundService` poller per module, reading unprocessed rows with
  `FOR UPDATE SKIP LOCKED`, publishing each, stamping `processed_on_utc` or `error`.
  It opens its own scope per batch and seeds a system `IUserContext` through
  `UserContextAccessor` — the same trick `ScopedSerginDispatcher` uses — so a consumer
  that dispatches a command still passes `PermissionCheckPipelineBehavior`.
- Consumers are idempotent on the event `Id` (an inbox table, or a unique constraint on
  whatever the consumer writes). At-least-once delivery is the contract.
- Do **not** reach for a shared connection or `TransactionScope` across the `dm` and `ua`
  schemas to "make cross-module writes atomic". It works while both live in one
  database and one process, and breaks the moment a module is extracted — which is the
  scenario the dual-mode dispatch work exists for.
- Library: hand-rolled fits the current constraints (MediatR everywhere, one Postgres,
  no broker). MassTransit (EF outbox and inbox built in) or Wolverine (durable Postgres
  outbox, but it replaces MediatR) only become worth their weight once a real broker
  enters the picture.

## Testing

The regression tests live in `tests/Sergin.MeterMinder.IntegrationTests.All/Events/`.
Nothing in a real module raises an event, so the tests bring their own producer:

- `TestEventsDbContext : SerginDbContext` (schema `test_events`, one
  `DbSet<TestAggregate>`), `TestAggregate : AggregateRoot<Guid>` whose `Create(name)`
  raises `TestAggregateCreated`, and three handlers — one recording into a scoped
  `RecordedEvents`, one throwing on demand, one adding a `"child"` aggregate when it sees
  a `"parent"`.
- The test class derives a factory from the shared `SerginWebApiFactory<Program>` with
  `WithWebHostBuilder(b => b.ConfigureServices(...))`, adding the test `DbContext`
  through the public `AddModuleDbContext<,,>` helper and the handlers through explicit
  `INotificationHandler<DomainEventNotification<TestAggregateCreated>>` registrations
  (the same manual-registration precedent `DeviceGrpcRoundTripTests` uses for
  `RemoteForwardingHandler`). Everything else — `DefaultEventDispatcher`, the
  interceptor, MediatR — is the real `AddSerginCore` wiring. No SharedKernel `internal`
  is exposed to the test assembly.
- Schema setup once per class: `DROP SCHEMA IF EXISTS test_events CASCADE; CREATE SCHEMA
  test_events;` then `IRelationalDatabaseCreator.CreateTablesAsync()`. Not
  `EnsureCreated` — it no-ops when the database already holds the modules' tables.

Cases:

1. `SaveChangesAsync_DispatchesRaisedEvent_AndClearsIt` — the recorder holds one event
   carrying the aggregate's id; `DomainEvents` is empty afterwards.
2. `SaveChangesAsync_WhenHandlerThrows_PersistsNothing` — the exception surfaces from
   `SaveChangesAsync`; a fresh scope finds no row.
3. `SaveChangesAsync_EntitiesAddedByHandler_PersistInSameSave` — saving a `"parent"`
   yields two rows and two recorded events after one `SaveChangesAsync` (proves the
   loop and same-transaction persistence).
4. `SaveChanges_Sync_WithPendingEvents_Throws` — the sync guard.

The rest of the suite must stay green; no module behaviour changes.

## Documentation

- Root `.claude/CLAUDE.md`, "Cross-cutting conventions" → the **Domain events** bullet
  describes the new pipe and the handler rules, and points here for the outbox decision.
- `src/SharedKernel/.claude/CLAUDE.md` — the `Sergin.SharedKernel.Application` and
  `Sergin.SharedKernel.Infrastructure` entries name `DomainEventNotification<TEvent>`,
  `IDomainEventHandler<TEvent>`, the `IPublisher`-based dispatcher and the
  `SavingChangesAsync` interceptor.
