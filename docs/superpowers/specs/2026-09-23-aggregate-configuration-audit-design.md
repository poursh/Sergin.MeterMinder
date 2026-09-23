# Aggregate configuration and audit stamps

## Motivation

Some platform behaviours apply to an aggregate as a whole rather than to one feature slice:
optimistic concurrency, tenant scoping, and an audit trail. Today none of them exists. `RowVersion`
sits in `Sergin.SharedKernel.Domain` with no aggregate carrying one, there is no tenant concept
anywhere, and no table records who created or changed a row.

What is missing first is a place to *declare* such behaviours per aggregate, one that reads like the
EF `IEntityTypeConfiguration<T>` classes the team already writes, so that switching a behaviour on
for an aggregate is one class plus one migration rather than edits spread across the domain type,
the `DbContext` and every repository. This spec builds that mechanism and the first behaviour on it:
audit stamps.

## Scope

In:

- `IAggregateConfiguration<TEntity>` — a per-aggregate configuration class, discovered by assembly
  scan like validators.
- An `AggregateFeatureRegistry` built from those classes at host start.
- An EF model-finalizing convention that adds audit shadow properties to every configured type.
- `AuditStampInterceptor`, which stamps them on save.
- Opting `Device`, `Manufacturer` and `DeviceModel` (`dm`) in, with one migration.
- Startup guards and integration tests.

Out, recorded so nobody reaches for them by accident:

- **Optimistic concurrency.** No update slice exists yet, so there is nothing to protect. It is the
  next feature expected on this mechanism; the builder gains a method then.
- **Tenant scoping.** Needs its own design: where a tenant comes from, UserAccess ownership, Dapper
  filtering (EF query filters do not reach raw-SQL reads), and tenant-scoped unique indexes.
- **Change-history log** (one row per change with old/new values). Stamps only for now.
- **UserAccess opt-in.** Its changes are a separate repository's PR; nothing in this design
  prevents a later opt-in.
- **Roll-up stamping** of a root when only a child entity changed.

The builder deliberately carries no placeholder `Concurrency()`/`Tenant()` methods. Adding a method
later is cheap; the shape of the builder is what must hold.

## Design

### Declaring features: `IAggregateConfiguration<TEntity>`

In `Sergin.SharedKernel.Application`, namespace `Sergin.SharedKernel.Application.Aggregates`:

```csharp
public interface IAggregateConfiguration<TEntity>
    where TEntity : class, IEntity
{
    void Configure(AggregateFeatureBuilder<TEntity> builder);
}

public sealed class AggregateFeatureBuilder<TEntity>
    where TEntity : class, IEntity
{
    public AggregateFeatureBuilder<TEntity> Audited();   // idempotent, chainable
}

public sealed record AggregateFeatures(bool Audited)
{
    public static AggregateFeatures None { get; } = new(false);
}

public sealed class AggregateFeatureRegistry
{
    public AggregateFeatures For(Type entityType);   // None for an unconfigured type
    public IReadOnlyCollection<Type> ConfiguredTypes { get; }
}
```

The constraint is `IEntity`, not `IAggregateRoot`, so a child entity such as `DeviceModel` can be
configured too. The name still says *aggregate* because the configuration expresses an aggregate
boundary decision even when it targets a child.

A module declares one class per configured type, in its `.Application` project, in the
aggregate's root folder:

```csharp
// Sergin.MeterMinder.DeviceManagement.Application/Devices/DeviceAggregateConfiguration.cs
internal sealed class DeviceAggregateConfiguration : IAggregateConfiguration<Device>
{
    public void Configure(AggregateFeatureBuilder<Device> builder) => builder.Audited();
}
```

The naming is `<Type>AggregateConfiguration`, so it cannot collide with the EF
`<Type>Configuration` in `.Infrastructure.Data`. Child-entity configurations follow the nested
folder rule (`Manufacturers/DeviceModels/DeviceModelAggregateConfiguration.cs`).

`.Application` was chosen over `.Domain` (the domain would take on an application-feature concept),
`.Infrastructure.Data` (the choice would become an infrastructure detail and need a new scan target)
and the composition root (a new assembly member on `ISerginModule`). `ApplicationAssembly` is
already scanned for validators and translators.

### Discovery

`AggregateFeatureRegistry.FromAssemblies(assemblies)` finds concrete, non-generic types
implementing a closed `IAggregateConfiguration<T>`, creates each with `Activator.CreateInstance`,
runs its `Configure` against a fresh builder, and returns the frozen result.
`FromConfigurationTypes(types)` does the same for an explicit list (tests use it).

The registry is built in two places, for two readers:

- **The EF model.** Each module `DbContext` names its own registry through
  `protected virtual AggregateFeatureRegistry AggregateFeatures` on `SerginDbContext` (default
  `AggregateFeatureRegistry.Empty`). DeviceManagement overrides it with
  `AggregateFeatureRegistry.FromAssemblies([DeviceManagementApplicationAssemblyReference.Assembly])`.
  This is deliberate: an `IDesignTimeDbContextFactory` builds the context with no DI container, so a
  registry resolved from the application service provider would be empty at design time, and the next
  `dotnet ef migrations add` would scaffold `DropColumn` for every audit column. A per-context
  override behaves the same at runtime and at design time, keeps EF's per-context-type model cache
  correct, and needs no change in a module that does not opt in (UserAccess).
- **The startup guards.** `AddSerginCore` registers a singleton built from every local module's
  `ApplicationAssembly`, which is what guard 3 below checks against the models.

Configurations are pure declarations: no DI, no constructor arguments. Remote modules are skipped —
they ship no `.Application` — exactly as for validators.

`ISerginModule` gains no member.

### Adding the columns: `AggregateFeatureConvention`

`SerginDbContext` overrides `ConfigureConventions` once and adds an
`AggregateFeatureConvention : IModelFinalizingConvention` over the context's own
`AggregateFeatures`. A model-finalizing convention runs after `OnModelCreating`, after
every `IEntityTypeConfiguration`, and after the other conventions, on the complete, still-mutable
model. For every entity type the registry marks `Audited` it adds four shadow properties and the
annotation `sergin:audited`:

| Property | Column | Type |
|---|---|---|
| `CreatedAtUtc` | `created_at_utc` | `timestamptz NOT NULL` |
| `CreatedBy` | `created_by` | `uuid NOT NULL` |
| `ModifiedAtUtc` | `modified_at_utc` | `timestamptz NULL` |
| `ModifiedBy` | `modified_by` | `uuid NULL` |

Property and column names are constants on a SharedKernel `AuditColumns` class, so the convention,
the interceptor and any future raw-SQL read use one spelling.

Shadow properties keep the domain types untouched. No module context overrides
`ConfigureConventions` today (checked 2026-09-23); a future override must call `base`.

**Modified is nullable on purpose.** A row that was only inserted was not modified, and `NULL` says
so directly. A "last touched" read uses `COALESCE(modified_at_utc, created_at_utc)`.

**Column names are set explicitly.** The convention calls `HasColumnName` from `AuditColumns` rather
than relying on `UseSnakeCaseNamingConvention` reacting to properties added during finalizing, which
was unverified. It produces the same names either way and removes the uncertainty;
`AuditColumnsTests` pins the result.

The alternative — an `IConventionSetPlugin` registered through a custom `IDbContextOptionsExtension`
— survives a subclass forgetting `base.ConfigureConventions`, but is EF-internal plumbing for a risk
that does not exist today.

### Stamping: `AuditStampInterceptor`

`AuditStampInterceptor(IUserContext user, IDateTimeProvider clock) : SaveChangesInterceptor`,
registered scoped like `EventDispatcherInterceptor` and added in `AddModuleDbContext` **after** it,
so entities a domain-event handler adds or changes on the same save are stamped too.

In `SavingChangesAsync` and `SavingChanges`, with one `now` read per save, for every entry whose
entity type carries `sergin:audited`:

- **Added** — set `CreatedAtUtc` and `CreatedBy`; leave `Modified*` `NULL`.
- **Modified** — set `ModifiedAtUtc` and `ModifiedBy`; mark both `Created*` properties
  `IsModified = false` so nothing can overwrite them.
- **Deleted / Unchanged / Detached** — skip.

The synchronous path stamps the same way; stamping needs nothing asynchronous, so unlike event
dispatch it is not refused.

The actor is whatever `IUserContext` resolves to in the saving scope: the signed-in user through
`ScopedSerginDispatcher`, the relay identity during outbox delivery, the anonymous identity during
an unauthenticated OIDC callback.

Semantics that follow, documented rather than guarded:

- A root is **not** stamped when only a child entity changed; a child is stamped only if it has its
  own configuration.
- Raw-SQL or Dapper writes bypass stamping. None exist today.

### Startup guards

Each fails host start with a message naming the offending type(s):

1. **Two configurations for the same `T`** — named together; no silent last-wins.
2. **A configuration with no parameterless constructor** — the `Activator` failure is rethrown with
   the type name.
3. **A configured type that no module's `DbContext` maps, or whose context does not apply it** — a
   configuration stranded in the wrong module, or a module context that forgot to override
   `AggregateFeatures`. `AddModuleDbContext` registers a `ModuleDbContextRegistration(Type)` per
   context so the guard can enumerate them. Checked in `UseSerginWebUiAsync` (and `UseSerginWebApiAsync`) in every
   environment, before the Development-only migrate step, comparing `registry.ConfiguredTypes` against every module context's `Model`. The convention
   cannot do this itself because it sees one context at a time.

## Rollout

Two pull requests, because SharedKernel is a submodule.

**Sergin.SharedKernel**

- `Application/Aggregates/`: `IAggregateConfiguration<T>`, `AggregateFeatureBuilder<T>`,
  `AggregateFeatures`, `AggregateFeatureRegistry`.
- The scan and guards 1–2 in `AddSerginCore`; guard 3 in both `Use…Async` bootstraps.
- `AuditColumns`, `AggregateFeatureConvention`, the `ConfigureConventions` override on
  `SerginDbContext`.
- `AuditStampInterceptor`, registered in `AddSerginCore` and added in `AddModuleDbContext`.
- SharedKernel `CLAUDE.md`.

**Sergin.MeterMinder**

- Submodule bump.
- `DeviceAggregateConfiguration`, `ManufacturerAggregateConfiguration`,
  `DeviceModelAggregateConfiguration`, and the `AggregateFeatures` override on
  `DeviceManagementDbContext`.
- Migration `AddAuditColumns` in `dm`, converted to a file-scoped namespace. It adds the columns
  nullable, backfills `created_at_utc = now()` and `created_by` with the relay identity's fixed id
  (`01920000-0000-7000-8000-00000000000f`, the platform's system actor), then sets `created_*`
  `NOT NULL`. A comment in the migration states that the backfill is a stand-in for rows that
  predate auditing, not their real history.
- `CLAUDE.md` and DeviceManagement's `CLAUDE.md`.

## Testing

Integration tests in `tests/Sergin.MeterMinder.IntegrationTests.All/Audit/`, sharing the existing
collection fixture:

- **`AuditColumnsTests`** — the finalized `dm` model carries the four snake_case columns on
  `device`, `manufacturer` and `device_model`, `created_*` required and `modified_*` optional; an
  unconfigured type carries none.
- **`AuditStampTests`**
  - Creating a device through `ISerginDispatcher` stamps `created_*` with the dispatcher's user and
    leaves `modified_*` `NULL`, read back through raw SQL.
  - Modifying a test-only audited aggregate stamps `modified_*` and leaves `created_*` unchanged
    (DeviceManagement has no update slice yet; the test aggregate follows the outbox tests'
    `OutboxTestHost` precedent). Its `IAggregateConfiguration` must live in an assembly the test
    host passes as a local module's `ApplicationAssembly`, or the scan never finds it.
  - An entity added by a domain-event handler on the same save is stamped.
  - A context seeded through `UserContextAccessor` is the stamped actor — the mechanism the outbox
    relay and the Blazor dispatcher both use to hand their identity into a scope, so this covers the
    relay without running it.
- **`AggregateConfigurationGuardTests`** — the duplicate-configuration and unmapped-type guards
  throw, naming the types.
