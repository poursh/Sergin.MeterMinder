# Optimistic concurrency as an aggregate feature

**Date:** 2026-09-29
**Status:** Implemented on feat/optimistic-concurrency (host) and feat/optimistic-concurrency (Sergin.SharedKernel)
**Builds on:** [2026-09-23-aggregate-configuration-audit-design.md](2026-09-23-aggregate-configuration-audit-design.md),
[2026-09-28-soft-delete-design.md](2026-09-28-soft-delete-design.md)

## Problem

Two users can open the same manufacturer, and the second one to act wins without knowing the first acted.
Today that is a lost update: one user adds a model while another deletes the manufacturer from a page
loaded before the model existed, and nothing refuses the stale delete. `RowVersion` has sat in
`Sergin.SharedKernel.Domain` since the start with no aggregate carrying it, and the aggregate-configuration
design said the builder "grows one method each" when concurrency is built. This is that method.

The goal is **optimistic lost-update protection**: a write made against a version of an aggregate the caller
has not seen is refused, and nothing is persisted.

## Decisions

1. **Version travels out of band, both ways.** Command records and query response records do not carry a
   version. The expected version reaches the pipeline through an ambient, scoped request context, seeded by
   the front end (Blazor dispatcher, WebApi `If-Match`, gRPC metadata). The current version leaves the same
   way (dispatcher result, `ETag`, gRPC response header). Contracts stay version-free.
2. **Explicit `row_version uuid` column, not Postgres `xmin`.** One column per aggregate root, a shadow
   property marked `IsConcurrencyToken`, rewritten with `RowVersion.Create()` (Guid v7) on every save that
   touches the aggregate. `xmin` changes only when its own row changes, is a transaction id that does not
   survive dump and restore, and would leak a Postgres `uint` into the `ETag` format.
3. **One version per aggregate.** Children carry no column. A change to a child alone (for example
   `Manufacturer.AddModel`, which inserts a `device_model` row only) still bumps the root's `row_version`,
   so the root's version covers the whole aggregate.
4. **Required per command, by attribute.** `[RequiresExpectedVersion]` on a command record makes a missing
   version an error (428). An unmarked command still has its version checked if the caller sent one:
   present means checked, the same as HTTP `If-Match`. Internal callers (outbox consumers, OIDC
   provisioning) keep working on unmarked commands.
5. **One version, one root.** A request carries at most one expected version, and it applies to the single
   versioned root the handler touched. More than one is a programming error, not a user error. Keyed
   versions wait for a command that needs them.
6. **Scope: DeviceManagement only.** `Device` and `Manufacturer` are versioned; `DeviceModel` inherits its
   root's. UserAccess is untouched: no UserAccess PR.
7. **gRPC propagates the version.** Remote mode is unwired today, but without propagation every guarded
   command sent Remote would answer 428. Metadata carries it both ways.

## Design

### 1. Carrier, attribute, pipeline behavior

All in `Sergin.SharedKernel.Application`, a new `Concurrency/` folder.

**`ConcurrencyContext`** — `public sealed class`, registered scoped by `AddSerginCore` next to
`UserContextAccessor`, with two slots:

- `RowVersion? Expected { get; set; }` — set by the front-end adapter before the send.
- `RowVersion? Current { get; set; }` — set during the send: by a GetOne handler from the row it read, or by
  the bump interceptor to the version it wrote.

One instance per scope. The Blazor dispatcher and the gRPC server each open their own scope per call, so a
value never crosses requests. It is a plain class with no interface, like `UserContextAccessor`: there is
nothing to substitute.

**`[RequiresExpectedVersion]`** — `AttributeUsage(AttributeTargets.Class)`, no properties, placed on the
command record beside `[RequiredPermissions]`. The command's constructor does not change.

**`ExpectedVersionPipelineBehavior<TRequest, TResponse>`** — open behavior, `where TRequest : IBaseCommand`.
Registered between the existing two, so the order is `PermissionCheck`, `ExpectedVersion`, `Validation`: a
request refused for a missing precondition is not worth validating.

- Attribute present and `Expected` null: returns `VersionErrors.Required`.
- Wraps `next()` in `try`/`catch (ConcurrencyConflictException)` and returns `VersionErrors.Stale`. The
  exception is thrown by `SaveChangesAsync` inside the handler (section 2). Catching it here keeps
  `.Application` free of EF types.

**Errors** — `VersionErrors`, a static class:

| Error | Type | Code | HTTP |
|---|---|---|---|
| `Required` | `Error.Custom(428, …)` | `General.VersionRequired` | 428 Precondition Required |
| `Stale` | `Error.Custom(412, …)` | `General.VersionStale` | 412 Precondition Failed |
| `NotPublished` | `Error.Unexpected(…)` | `General.VersionNotPublished` | 500 (a caller bug; see Blazor below) |

Custom types rather than `Conflict`/`Validation`, so the HTTP status is right with no WebApi special case and
a Blazor page recognises a stale version by type, not by comparing a code string. `SerginProblemFactory`
gains a case for each in `GetStatusCode`, `GetTitle` and `GetDetail` (localised on `error.Code` like the
other non-validation types); there are no resource files: `DefaultLocalizer` answers the key.

**Targeted cleanup** — `PermissionCheckPipelineBehavior` builds an `ErrorOr<T>` from an `Error` with inline
reflection. The new behavior needs the same code, so it moves to an internal
`ErrorOrResponse.TryFrom<TResponse>(Error, out TResponse)` helper used by both.

### 2. Model shape, interceptors, exception translation

**Builder** — `AggregateFeatureBuilder<TRoot>.Versioned()`, chainable
(`builder.Audited().SoftDeletable().Versioned()`), calling it twice harmless. `AggregateFeatures` gains a
`Versioned` flag. `AggregateFeatureRegistry.ForChild` always answers `Versioned = false`.

**Convention** — `AggregateFeatureConvention` adds to each versioned root (never a child, never an owned
type):

- shadow property `RowVersion` (`Guid`), column `row_version`, required, `IsConcurrencyToken()`;
- annotation `Sergin:Versioned`.

The walk already maps each child to its root. It stamps every child entity type reached from a versioned
root with an annotation naming that root's type, so the interceptors can find a child's root without a
navigation back (`DeviceModel` has none to `Manufacturer`). Names live in a new `RowVersionColumns` static
class, the counterpart of `AuditColumns` and `SoftDeleteColumns`.

The column is a shadow property, not a domain member: aggregates stay free of persistence concerns, as with
audit. `RowVersion` remains the value type the context carries.

**Two interceptors**, because a domain-event handler can change a second root in the same save and that root
must be bumped but not checked:

1. **`ExpectedVersionInterceptor`**, registered **first**, before `EventDispatcherInterceptor`, so it sees
   only the handler's own changes. It collects the versioned roots touched: a root entry `Modified` or
   `Deleted`, or a child entry `Added`/`Modified`/`Deleted` whose root entry is tracked. When `Expected` is
   set, exactly one such root is required (otherwise `InvalidOperationException` naming the roots), and its
   `row_version` gets `OriginalValue = Expected`. `Added` roots are ignored: there is nothing to compare.
   It records the chosen root for the second interceptor.
2. **`RowVersionBumpInterceptor`**, registered **last**, after `SoftDeleteInterceptor`, so it sees what event
   handlers and the soft-delete cascade changed. Every versioned root touched gets
   `CurrentValue = RowVersion.Create()`; an `Added` root gets its first version. A root left `Unchanged`
   while a child changed has only its `row_version` property marked modified — the `AddModel` case. A
   changed child whose root is not tracked throws `InvalidOperationException`: every write goes through the
   root's behaviour, so the root is always loaded. In `SavedChangesAsync` it publishes to
   `ConcurrencyContext.Current`: the checked root's new version when one was checked; otherwise, when a
   version was sent but nothing was checked (the command changed nothing, or only a domain-event handler
   changed another root), the version that was sent — publishing another root's would hand the caller a
   version that belongs to a different aggregate; otherwise, when no version was sent (a create), the single
   root touched.

The final registration order is `ExpectedVersion`, `EventDispatcher`, `AuditStamp`, `SoftDelete`,
`RowVersionBump`. Postgres does the check atomically:
`UPDATE … SET row_version = @new WHERE id = @id AND row_version = @expected`. Zero rows affected is a
`DbUpdateConcurrencyException`. A soft delete is an UPDATE, so it is checked the same way.

**Translation** — `SerginDbContext` overrides `SaveChangesAsync` and `SaveChanges`, catches
`DbUpdateConcurrencyException`, and rethrows `ConcurrencyConflictException` (in `.Application`, inner
exception kept). Section 1's behavior turns it into `VersionErrors.Stale`. The transaction rolls back;
nothing is persisted.

**Edge case** — `Expected` set but the handler changed nothing: no row is written, so nothing is checked,
and the send succeeds. There is nothing to lose.

**`row_version` is a real EF concurrency token**, so the check above is narrower than "checked only when
`Expected` is set" makes it sound: every update to a tracked versioned root is checked against the version
it was loaded at in that scope, `Expected` or not — EF puts the tracked entity's `OriginalValue` into the
WHERE clause regardless. An unguarded command that loads and saves the same aggregate, a domain-event
handler's change, and an outbox consumer's save can all still hit a conflict this way: 412 through the
pipeline for a command, a retried row for the relay. `Expected`/`ExpectedVersionInterceptor` adds one more
thing on top — a caller-supplied check across scopes, for when the load and the save are different
requests, which the tracked `OriginalValue` alone cannot provide.

### 3. Read side and front-end adapters

**Read side** — a GetOne query selects `row_version` as well. Dapper binds a record through its
constructor, so the extra column cannot land in `DeviceQueryResponse`; the repository reads it with a split
mapping and returns `Versioned<T>(T Value, RowVersion Version)`, a new SharedKernel record. The handler derives
from `VersionedQueryHandler<TQuery, TResponse>` and returns the `Versioned<T>` from `HandleVersioned`; the base
sets `concurrency.Current` and returns the bare value. Response records do not change. Two
handlers change: `GetDeviceById` and `GetManufacturerById`. List queries carry no version: nothing on a list
page writes.

**Blazor** — `ISerginDispatcher` gains:

```csharp
Task<ErrorOr<Versioned<TResponse>>> SendVersionedAsync<TResponse>(
    IRequest<ErrorOr<TResponse>> request,
    RowVersion? expected = null,
    CancellationToken cancellationToken = default);
```

The result is the read side's own shape, so a version exists only on success. A send that succeeds but
publishes no `Current` (it touched no versioned aggregate, or a remote reply carried no version) returns
`VersionErrors.NotPublished`, an `Unexpected` error: asking such a request for its version is a caller bug.
`ScopedSerginDispatcher` seeds
`ConcurrencyContext.Expected` in the child scope, where it already seeds `UserContextAccessor`, and reads
`Current` back before the scope is disposed. `SendAsync` does not change, so no other page does.

Pages:

- `DeviceDetailPage` and `ManufacturerDetailPage` load through `SendVersionedAsync` and keep a
  `private RowVersion? version`. Their delete action sends it.
- `AddDeviceModelPage` already loads the manufacturer for its breadcrumb name. It keeps the version from the
  same load and sends it with `AddDeviceModelCommand`.
- A stale answer (412) raises a snackbar through `ErrorPresenter.Notify`, then the page reloads its record,
  so the user sees the current state, holds the new version, and can retry.

**WebApi** — `ExpectedVersionEndpointFilter`, added once per module group in `UseSerginWebApiAsync`
(`MapGroup(module.Schema).AddEndpointFilter<…>()`). No endpoint changes.

- `If-Match: "<guid>"` (a strong ETag) sets `Expected`.
- A malformed value answers 400. `*` or a weak tag is treated as absent, so a guarded command answers 428.
- After the endpoint runs, a set `Current` becomes the response's `ETag: "<guid>"`.

The filter compiles and is tested but stays unhosted, like the rest of the WebApi layer.

**gRPC** — the client side is `ConcurrencyClientInterceptor`, put on the invoker's channel (not
`RemoteForwardingHandler<TRequest, TResponse>` itself, because `IRemoteInvoker<,>` cannot take metadata
without a breaking change): it sends `Expected` as `sergin-expected-version` request metadata and reads
`Current` back from the `sergin-version` response **trailer**. `ConcurrencyServerInterceptor`, the serving
host's half, seeds `Expected` from that metadata into the call's scope before the service runs its pipeline
and writes `Current` to the response trailer afterwards. A malformed `sergin-expected-version` header — not
parseable as a non-empty GUID — is rejected by `ConcurrencyServerInterceptor` with
`RpcException(StatusCode.InvalidArgument)` before the pipeline runs, the gRPC analogue of the WebApi 400.

### 4. DeviceManagement wiring

- `DeviceAggregateFeatureConfiguration` and `ManufacturerAggregateFeatureConfiguration` append
  `.Versioned()`.
- `[RequiresExpectedVersion]` on `DeleteDeviceCommand`, `DeleteManufacturerCommand` and
  `AddDeviceModelCommand`.
- Migration `AddRowVersionColumns`: `row_version uuid` added nullable to `dm.device` and `dm.manufacturer`,
  backfilled with `gen_random_uuid()` (Postgres 17 has no built-in v7, and a backfill value needs only to be
  unique), then made `NOT NULL`. Hand-converted to a file-scoped namespace. `dm.device_model` gets nothing.
- `DeviceQueryRepository` and `ManufacturerQueryRepository` GetOne SQL add `row_version`; list SQL is
  untouched.

## Testing

A new `tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/` folder, on the shared Testcontainers
fixture.

- **`RowVersionColumnsTests`** — the `dm` model carries the column and concurrency token on `device` and
  `manufacturer`, none on `device_model`, none on an unconfigured type.
- **`RowVersionInterceptorTests`** — a test-only aggregate with a child, in the style of
  `SoftDeleteInterceptorTests`: an insert sets a version; an update bumps it; a child-only change bumps the
  root; a soft delete bumps it; a stale `Expected` throws `ConcurrencyConflictException` and persists
  nothing; `Expected` with two roots touched throws `InvalidOperationException`; a root changed by a
  domain-event handler is bumped but not checked.
- **`ExpectedVersionPipelineTests`** — a marked command with no version answers 428; a stale version answers
  412; an unmarked command with a version is still checked.
- **`DeviceManagementConcurrencyTests`** — through `ISerginDispatcher`: `GetManufacturerById` returns a
  version; `AddDeviceModel` with it succeeds and returns a new one; replaying the old version answers 412 and
  the second model is not saved; a stale device delete answers 412 and the device stays live.
- **`ExpectedVersionEndpointFilterTests`** — a small in-test `WebApplication` with one endpoint: `If-Match`
  in, `ETag` out, malformed answers 400, `*` answers 428.
- **`DeviceGrpcRoundTripTests`** — gains an assertion that the version comes back on `GetDeviceById`.
- **Rendering** — 412 and 428 render their localised title and detail, beside `ValidationProblemRenderingTests`.

## Delivery

1. Work in a git worktree.
2. **Sergin.SharedKernel PR first**: the carrier, attribute, behavior, errors, builder method, convention,
   both interceptors, exception translation, dispatcher overload, endpoint filter, gRPC propagation, the
   `ErrorOrResponse` extraction, and its `.claude/CLAUDE.md`.
3. **Sergin.MeterMinder**: the submodule bump, DeviceManagement wiring, migration, pages, tests, and
   `CLAUDE.md` — drop "`RowVersion` exists … no aggregate carries one" and "Concurrency … not built on this
   mechanism yet", and add a Concurrency bullet under Cross-cutting conventions.

## Out of scope

- UserAccess (`User.Versioned()`, `DeactivateUserCommand`).
- Update slices and edit forms. The first one (`UpdateManufacturer`) will use this mechanism unchanged.
- Keyed versions for commands that guard more than one root.
- Translating other Postgres `SqlState`s (23503, 23505) into `ErrorOr`: still its own cross-cutting slice.
