# Permission catalogue: permissions defined per aggregate

Status: design, not implemented. Date: 2026-10-20.

Builds on `2026-10-01-command-configuration-design.md`, `2026-09-23-aggregate-configuration-audit-design.md` and `2026-10-09-scoped-rbac-design.md`.

## Problem

Permissions have no single source of truth.

- **DeviceManagement writes raw strings.** Fifteen command configurations and `DeviceManagementNavigation` spell out names such as `"permission.dm.devices.update"`. A typo compiles. It fails at runtime, as a 403 or a nav entry that never appears.
- **UserAccess names its permissions once**, in `UserAccessPermissions` (`.Application.Contracts`). But the scoped ones (`ua.scopes.*`, `ua.roles.*`, `ua.assignments.manage`) are checked in handlers through `IScopeAuthorization`, not through `RequirePermissions`.
- **The role editor's catalogue is incomplete.** `CommandConfigurationRegistry.KnownPermissions` is built only from `RequirePermissions(...)` calls. The handler-checked UserAccess permissions never reach it, so `CreateRolePage` and `RoleDetailPage` cannot offer them. An administrator cannot grant "manage scopes" except through `permission.sys.platform-all`.
- **The catalogue is a flat list of strings.** It has no grouping and no human label.

## Goal

1. Every permission is a **definition object**, with a name and a label.
2. Every place that requires or checks a permission references the definition, **never a string name**.
3. Each aggregate **registers** the permissions it owns in its `<Root>AggregateFeatureConfiguration`, in `.Application.Configurations`.
4. The platform builds a catalogue grouped **Module > Aggregate > permission** from those registrations.
5. Host start **refuses** a permission that is used but not registered.

## Design

### `PermissionDefinition`

A new `sealed` type in `Sergin.SharedKernel.Domain/Securities/`, next to `Permission`.

```csharp
public sealed class PermissionDefinition
{
    public Permission Permission { get; }
    public string Label { get; }

    public static PermissionDefinition Create(string name, string label);

    public static PermissionDefinition AllPlatform { get; }   // permission.sys.platform-all
}
```

- `Create` is the **only** place a permission name is written as a string. It validates the name through `Permission.Create`.
- There is no implicit conversion from `string`. A use site cannot pass a name by accident.
- It lives in `.Domain` because that project is the zero-dependency leaf. `.Application.Contracts`, `.Application`, every presentation project and `Sergin.SharedKernel.Modules` can all reference it.
- Equality is by `Permission`. Two definitions with the same name are a startup error (see "Startup refusals").

### Definitions live in Contracts, per aggregate

One `public static class <Aggregate>Permissions` per aggregate root, in `.Application.Contracts/<Aggregate>/`. The folder mirrors the aggregate's folder in `.Application.Configurations`.

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices;

public static class DevicePermissions
{
    public static PermissionDefinition Read { get; } =
        PermissionDefinition.Create("permission.dm.devices.read", "View devices");

    public static PermissionDefinition Update { get; } =
        PermissionDefinition.Create("permission.dm.devices.update", "Create and edit devices");

    public static PermissionDefinition Delete { get; } =
        PermissionDefinition.Create("permission.dm.devices.delete", "Delete devices");
}
```

Contracts is the right project because every reader can see it: handlers (`.Application`), the nav (`.Presentation.Blazor`), the configurations, and tests. `.Application.Configurations` cannot be that home — its types are `internal` and neither `.Application` nor the presentation projects reference it.

Initial set:

| Module | Class | Members |
|---|---|---|
| dm | `DevicePermissions` | `Read`, `Update`, `Delete` |
| dm | `ManufacturerPermissions` | `Read`, `Update`, `Delete` (device models use these) |
| ua | `UserPermissions` | `Read`, `Manage`, `AssignmentsManage` (assignments are a child of `User`) |
| ua | `ScopePermissions` | `Read`, `Manage` |
| ua | `RolePermissions` | `Read`, `Manage` |
| sys | `PermissionDefinition.AllPlatform` | declared by SharedKernel |

`UserAccessPermissions` is split into the three UserAccess classes and deleted.

### Registration in the aggregate configuration

A new feature on `AggregateFeatureBuilder<TAggregateRoot>`:

```csharp
internal sealed class DeviceAggregateFeatureConfiguration : IAggregateFeatureConfiguration<Device>
{
    public void Configure(AggregateFeatureBuilder<Device> builder) =>
        builder.Audited().SoftDeletable().Versioned().Scoped()
            .Permissions(DevicePermissions.Read, DevicePermissions.Update, DevicePermissions.Delete);
}
```

- `Permissions(PermissionDefinition, params PermissionDefinition[])` appends and is chainable, like the other features. The label is already on the definition, so registration repeats nothing.
- It is **not an EF feature**. `AggregateFeatureConvention` and `AggregateFeatureGuard` ignore it. No migration.
- A child entity's permissions are registered on its root. `DeviceModel` uses `ManufacturerPermissions`.
- An aggregate with no other feature gets a configuration holding only `Permissions(...)`. `User` needs a new `UserAggregateFeatureConfiguration`. Its `AggregateFeatures` stay `None`. `AggregateFeatureGuard` only requires that a configured type is mapped and that each feature it claims is applied, so a `None` configuration for a mapped type passes. Implementation adds a test for it.
- Binding a `User` command configuration to `User` is unchanged: a root without `Scoped()` still resolves to `PermissionScope.Any`.

### Use sites take definitions

| API | Today | After |
|---|---|---|
| `CommandConfigurationBuilder<T>.RequirePermissions` | `(Permission, params Permission[])` | `(PermissionDefinition, params PermissionDefinition[])`; the `Permission` overload is removed |
| `IScopeAuthorization.CurrentUserHasAsync` (both), `CurrentUserHoldsAnywhereAsync`, `CurrentUserReachAsync` | `Permission` | `PermissionDefinition` |
| `SerginNavItem.RequiredPermission` | `string?` | `PermissionDefinition?` |
| `IScopedPermissionChecker` | `Permission` | unchanged |
| Stored grants, `GrantRolePermissionCommand`, `RevokeRolePermissionCommand` | `Permission` | unchanged |

Examples:

```csharp
builder.RequirePermissions(DevicePermissions.Update).RequireExpectedVersion();

if (!await authorization.CurrentUserHasAsync(ScopePermissions.Manage, command.ParentId, cancellationToken))
{
    return Error.Forbidden();
}

public static SerginNavItem Devices { get; } =
    new("Devices", "/dm/devices", Icons.Material.Filled.Memory, RequiredPermission: DevicePermissions.Read);
```

Notes:

- `Sergin.SharedKernel.Modules` gains a `ProjectReference` to `Sergin.SharedKernel.Domain`. Both are leaves, so there is no cycle. `SerginNavMenu` drops its `Permission.TryCreate` parse.
- `IScopedPermissionChecker` keeps `Permission`. It is the module-facing seam over stored grants, and a stored grant is data, not a definition.
- A grant is data entered by a user. The role commands keep `Permission` and resolve it against the catalogue (`PermissionCatalog.TryGet`).

### Category

The category is derived, not typed:

- **Module** — the `<schema>` segment of the name (`permission.dm.…` is `dm`). Its label is the localiser key `Permissions.<schema>`, which renders the schema verbatim until resources exist, like every other key today.
- **Group** — the aggregate root that registered the permission. Its label is the root's type name. `Permissions(...)` may later take an optional group label; not in v1.
- **Permission** — `PermissionDefinition.Label`, localised through `ILocalizer`.

`PermissionDefinition.AllPlatform` sits in a `Platform` group under the `sys` module.

### `PermissionCatalog`

A new singleton in `Sergin.SharedKernel.Application/Securities/Authorization/`.

```csharp
public sealed record PermissionCatalogEntry(
    PermissionDefinition Definition, string Schema, Type AggregateRoot);

public sealed class PermissionCatalog
{
    public IReadOnlyCollection<PermissionCatalogEntry> Entries { get; }
    public bool Contains(PermissionDefinition definition);
    public bool TryGet(Permission permission, [NotNullWhen(true)] out PermissionDefinition? definition);
    public IReadOnlyList<IGrouping<string, IGrouping<Type, PermissionCatalogEntry>>> Grouped();
}
```

- `AddSerginCore` builds it from the same aggregate configuration types that `CommandConfigurationRegistry.FromSources` already collects, for local and remote modules.
- `CommandConfigurationRegistry.KnownPermissions` is removed.
- `CreateRolePage` and `RoleDetailPage` inject `PermissionCatalog` and render a grouped `MudSelect`: one `MudListSubheader` per module and per group, each item showing the label with the name as secondary text.

### Startup refusals

Each refusal fails host start and names the offending type and permission.

1. One permission registered by two aggregates, or two definitions with the same name.
2. A registered permission whose `<schema>` segment is not the registering module's schema. `CommandConfigurationSource` gains a `Schema`; `AddSerginCore` already builds one source per module and knows `module.Schema`. `sys` is reserved for SharedKernel.
3. A `RequirePermissions(...)` definition that no aggregate registered. Checked while `CommandConfigurationRegistry` is built.
4. A `SerginNavItem.RequiredPermission` that no aggregate registered. Checked in `UseSerginWebUiAsync`, next to the route-prefix guard.

With no strings at use sites, a typo is a compile error (a missing property). The refusals catch the mistake that remains: a definition that exists but was never registered.

Handler checks cannot be scanned at startup. `ScopeAuthorization` logs an unregistered definition and denies it at call time, which keeps NFR-5 (fail closed).

### Runtime rule on grants

`CreateRoleCommandValidator` and `GrantRolePermissionCommandValidator` refuse a permission that is not in the catalogue. Today any well-formed string can be granted.

A stored grant of a permission later removed from code stays as a dead row. `Permission.TryCreate` already narrows such rows on read. No migration.

## Consequences

- **Tests.** A test that adds its own configurations through `CommandConfigurationSource.FromTypes(...)` and requires a test permission must also register it, through a test aggregate configuration. Otherwise refusal 3 stops its host. This touches helpers under `Commands/`, `Concurrency/` and `Authorization/`.
- **Rollout: three PRs, in order.**
  1. Sergin.SharedKernel: `PermissionDefinition`, `Permissions(...)`, `PermissionCatalog`, the new signatures, refusals 1–4.
  2. Sergin.UserAccess: the three definition classes, `UserAggregateFeatureConfiguration`, registrations on `Scope` and `Role`, handler call sites, role pages, the two validators.
  3. Sergin.MeterMinder: DeviceManagement definitions and registrations, configurations, nav, tests, both submodule bumps.

  The signature changes break UserAccess and DeviceManagement, so the three PRs build only together. Merge them as one coordinated change and bump both submodules in PR 3.
- **Documentation.** Update the root CLAUDE.md ("Request policy"), SharedKernel's CLAUDE.md ("Scoped permission checks", `KnownPermissions`), UserAccess's CLAUDE.md ("Permissions"), and the `/add-feature` and `/add-module` skills.

## Rejected alternatives

- **`const string` names.** Use sites still hold strings, which this design rules out, and a string carries no label.
- **Generic `PermissionDefinition<TAggregateRoot>`** carrying its own category. Gives categories without registration, but the aggregate configuration should be the one registration point.
- **Registration on each command configuration.** A permission used by five commands gets five registrations and no natural owner.
- **A module-level `IPermissionConfiguration`.** Loses the aggregate grouping. Kept as a future escape hatch for a permission that belongs to no aggregate, such as a report.

## Open points

- Module labels: a resource per schema, or a `DisplayName` on `ISerginModule` (a breaking contract change).
- Whether the role editor shows the raw name next to the label.
