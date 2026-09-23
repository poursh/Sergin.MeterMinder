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

- `IAggregateFeatureConfiguration<TAggregateRoot>` — a per-aggregate configuration class, one per
  aggregate root, discovered by assembly scan like validators. Child entities take the root's features
  by default; a feature's builder can except a child.
- An `AggregateFeatureRegistry` built from those classes at host start.
- An EF model-finalizing convention that adds audit shadow properties to every configured type.
- `AuditStampInterceptor`, which stamps them on save.
- Opting `Device` and `Manufacturer` (`dm`) in — `DeviceModel` follows as `Manufacturer`'s child — with
  one migration.
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

### Declaring features: `IAggregateFeatureConfiguration<TAggregateRoot>`

In `Sergin.SharedKernel.Application`, namespace `Sergin.SharedKernel.Application.Aggregates`:

```csharp
public interface IAggregateFeatureConfiguration<TAggregateRoot>
    where TAggregateRoot : class, IAggregateRoot
{
    void Configure(AggregateFeatureBuilder<TAggregateRoot> builder);
}

public sealed class AggregateFeatureBuilder<TAggregateRoot>
    where TAggregateRoot : class, IAggregateRoot
{
    public AggregateFeatureBuilder<TAggregateRoot> Audited();   // idempotent, chainable
    public AggregateFeatureBuilder<TAggregateRoot> Audited(Action<AuditFeatureBuilder<TAggregateRoot>> configure);
}

public sealed class AuditFeatureBuilder<TAggregateRoot>
    where TAggregateRoot : class, IAggregateRoot
{
    public AuditFeatureBuilder<TAggregateRoot> ExceptChild<TChild>() where TChild : class, IEntity;
}

public sealed record AggregateFeatures(bool Audited)
{
    public static AggregateFeatures None { get; } = new(false);
}

public sealed class AggregateFeatureRegistry
{
    public AggregateFeatures For(Type aggregateRoot);                      // None for an unconfigured root
    public AggregateFeatures ForChild(Type aggregateRoot, Type childType); // the root's, less exceptions
    public IReadOnlyCollection<Type> ExceptedChildren(Type aggregateRoot);
    public IReadOnlyCollection<Type> ConfiguredTypes { get; }              // the configured roots
}
```

The constraint is `IAggregateRoot`: a feature is a decision about the whole aggregate, so a child
entity cannot be configured on its own. Every child takes the root's features by default (see
"Children" below), and a feature's own builder is where a child is left out:

```csharp
builder.Audited(audit => audit.ExceptChild<DeviceModel>());
```

`ExceptChild` is per feature, so once more features exist a child can be left out of one and keep the
others. Only the named type is excepted; its own children still take the root's features. Excepting an
aggregate root fails when the registry is built, since another aggregate never takes this one's
features in the first place.

A module declares one class per configured root, in its `.Application` project, in the aggregate's
root folder:

```csharp
// Sergin.MeterMinder.DeviceManagement.Application/Devices/DeviceAggregateFeatureConfiguration.cs
internal sealed class DeviceAggregateFeatureConfiguration : IAggregateFeatureConfiguration<Device>
{
    public void Configure(AggregateFeatureBuilder<Device> builder) => builder.Audited();
}
```

The naming is `<Root>AggregateFeatureConfiguration`, so it cannot collide with the EF
`<Type>Configuration` in `.Infrastructure.Data`.

`.Application` was chosen over `.Domain` (the domain would take on an application-feature concept),
`.Infrastructure.Data` (the choice would become an infrastructure detail and need a new scan target)
and the composition root (a new assembly member on `ISerginModule`). `ApplicationAssembly` is
already scanned for validators and translators.

### Discovery

`AggregateFeatureRegistry.FromAssemblies(assemblies)` finds concrete, non-generic types
implementing a closed `IAggregateFeatureConfiguration<T>`, creates each with `Activator.CreateInstance`,
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
model. For every configured root, and every child it reaches (below), whose features are `Audited`, it
adds four shadow properties and the annotation `Sergin:Audited`:

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

**Children.** Which entities belong to an aggregate is the EF model's knowledge, so the convention
finds them: starting at each configured root, it follows every navigation from a principal to its
dependents (ownership navigations included, and those declared on derived types), stopping at any
type that implements `IAggregateRoot`. Each child reached takes `registry.ForChild(root, child)`.

- Navigations, not foreign keys, decide membership. Another aggregate's child that merely holds a key
  to this root is not pulled in; `Manufacturer.Models` makes `DeviceModel` a child, `Device`'s key to
  `DeviceModel` does not make `Device` one.
- An owned type stored in its owner's table (`OwnsOne`) gets no columns of its own — the owner's row
  carries them — but the walk continues below it. An `OwnsMany` in its own table is stamped.
- An `ExceptChild` type the walk never reaches fails the model build naming it and the root, so a
  typo or a type from another aggregate cannot pass silently.
- An entity reached from two roots whose features for it differ fails the model build naming both
  roots — the same no-silent-last-wins rule as duplicate configurations.

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

- A root is **not** stamped when only a child entity changed; the child's own row is stamped, since
  children take the root's features unless excepted.
- Raw-SQL or Dapper writes bypass stamping. None exist today.

### Startup guards

Each fails host start with a message naming the offending type(s):

1. **Two configurations for the same root** — named together; no silent last-wins.
2. **A configuration with no parameterless constructor** — the `Activator` failure is rethrown with
   the type name. `ExceptChild` of an aggregate root fails at the same point.
3. **A configured type that no module's `DbContext` maps, or whose context does not apply it** — a
   configuration stranded in the wrong module, or a module context that forgot to override
   `AggregateFeatures`. `AddModuleDbContext` registers a `ModuleDbContextRegistration(Type)` per
   context so the guard can enumerate them. Checked in `UseSerginWebUiAsync` (and `UseSerginWebApiAsync`) in every
   environment, before the Development-only migrate step, comparing `registry.ConfiguredTypes` against every module context's `Model`. The convention
   cannot do this itself because it sees one context at a time.

## Rollout

Two pull requests, because SharedKernel is a submodule.

**Sergin.SharedKernel**

- `Application/Aggregates/`: `IAggregateFeatureConfiguration<T>`, `AggregateFeatureBuilder<T>`,
  `AuditFeatureBuilder<T>`, `AggregateFeatures`, `AggregateFeatureRegistry`.
- The scan and guards 1–2 in `AddSerginCore`; guard 3 in both `Use…Async` bootstraps.
- `AuditColumns`, `AggregateFeatureConvention`, the `ConfigureConventions` override on
  `SerginDbContext`.
- `AuditStampInterceptor`, registered in `AddSerginCore` and added in `AddModuleDbContext`.
- SharedKernel `CLAUDE.md`.

**Sergin.MeterMinder**

- Submodule bump.
- `DeviceAggregateFeatureConfiguration`, `ManufacturerAggregateFeatureConfiguration` (which audits
  `DeviceModel` as its child), and the `AggregateFeatures` override on `DeviceManagementDbContext`.
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
    `OutboxTestHost` precedent). Its `IAggregateFeatureConfiguration` must live in an assembly the test
    host passes as a local module's `ApplicationAssembly`, or the scan never finds it.
  - An entity added by a domain-event handler on the same save is stamped.
  - A context seeded through `UserContextAccessor` is the stamped actor — the mechanism the outbox
    relay and the Blazor dispatcher both use to hand their identity into a scope, so this covers the
    relay without running it.
- **`AggregateChildFeatureTests`** — against model-only contexts: children and grandchildren
  (including an `OwnsMany`) take the root's features; another aggregate root reached by a navigation
  does not; an `OwnsOne` in its owner's table gets no columns; `ExceptChild` leaves out only the named
  type; excepting a non-child or reaching one entity from two roots with different features fails the
  model build; excepting an aggregate root fails the registry build.
- **`AggregateFeatureGuardTests`** — the duplicate-configuration and unmapped-type guards
  throw, naming the types.
