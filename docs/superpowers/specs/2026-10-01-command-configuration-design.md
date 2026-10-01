# Command configuration

**Date:** 2026-10-01
**Status:** Design approved; not yet implemented
**Builds on:** [2026-09-23-aggregate-configuration-audit-design.md](2026-09-23-aggregate-configuration-audit-design.md),
[2026-09-29-optimistic-concurrency-design.md](2026-09-29-optimistic-concurrency-design.md)

## Problem

A MediatR request declares its cross-cutting policy with marker attributes on the contract record:

- `[RequiredPermissions("permission.dm.devices.delete")]` on 16 records, read by `PermissionCheckPipelineBehavior`.
- `[RequiresExpectedVersion]` on 7 records, read by `ExpectedVersionPipelineBehavior`.

Each behavior reflects over the request type on every send to find its attribute. This has three costs:

1. **No single declaration point.** A command's policy is spread over separate attributes, each read by a
   different behavior. Every new setting is one more attribute and one more reflection call.
2. **Attributes cannot grow well.** Arguments must be compile-time constants, there is no composition, and a
   mistake (a bad permission string, a version requirement on a query) is found at the first send, not at
   startup.
3. **Contract records carry platform policy.** The records in `.Application.Contracts` are request shapes.
   The attributes mix authorization and concurrency policy into them.

The platform already solved the same problem for aggregates: `IAggregateFeatureConfiguration<T>` declares an
aggregate's features in one class, and `AggregateFeatureRegistry` freezes every declaration at startup. This
design applies that shape to requests.

## Decisions

1. **One configuration class per request, beside its record.** An `internal sealed class
   <RecordName>Configuration : ICommandConfiguration<<RecordName>>` lives in the module's
   `.Application.Contracts` project, in the same folder and namespace as the record. One interface covers
   commands and queries: it is constrained to `IBaseCommand`, which both `ICommand<T>` and `IQuery<T>` carry.
2. **Contracts, not `.Application`.** A Remote module ships only its `ContractsAssembly`. A gateway host must
   still see the configuration, so its `PermissionCheckPipelineBehavior` refuses a forbidden call before the
   gRPC hop, exactly as the attribute allows today.
3. **Declarations, frozen at startup.** A configuration is created with `Activator`, never through DI, and
   must have a parameterless constructor. Its `Configure` runs once. The result is a singleton
   `CommandConfigurationRegistry`; behaviors do one dictionary lookup per send.
4. **No attribute fallback.** `RequiredPermissionsAttribute` and `RequiresExpectedVersionAttribute` are
   deleted in the same change. One source of truth from day one.
5. **v1 settings: permission and version only.** The builder has `RequirePermissions(...)` and
   `RequireExpectedVersion()`, with today's semantics. Further settings (any-of permissions, caching,
   timeouts, log policy) each add one builder method and one behavior change when a request needs them.
   No placeholder methods now.
6. **An unconfigured request has no policy.** `For(type)` answers `CommandSettings.None` for a request
   without a configuration: no permission and no version required. That is today's behavior for a record
   without attributes, so internal callers (outbox consumers, OIDC provisioning) are unaffected.

## Design

### Contracts (`Sergin.SharedKernel.Application`, namespace `…Commands.Configuration`)

```csharp
public interface ICommandConfiguration<TCommand>
    where TCommand : IBaseCommand
{
    void Configure(CommandConfigurationBuilder<TCommand> builder);
}

public sealed class CommandConfigurationBuilder<TCommand>
    where TCommand : IBaseCommand
{
    // Appends. The caller needs every listed permission (IUserContext.HasPermission).
    public CommandConfigurationBuilder<TCommand> RequirePermissions(Permission permission, params Permission[] more);

    // A missing expected version answers VersionErrors.Required (428).
    public CommandConfigurationBuilder<TCommand> RequireExpectedVersion();
}

public sealed record CommandSettings(IReadOnlyCollection<Permission> RequiredPermissions, bool RequiresExpectedVersion)
{
    public static CommandSettings None { get; } = new([], false);
}
```

`Permission` converts implicitly from `string` and validates in `Permission.Create`, so a configuration
writes plain strings and a malformed permission throws inside `Configure` — at startup.

Example, replacing `[RequiredPermissions("permission.dm.devices.delete")]` and `[RequiresExpectedVersion]`
on `DeleteDeviceCommand`:

```csharp
internal sealed class DeleteDeviceCommandConfiguration : ICommandConfiguration<DeleteDeviceCommand>
{
    public void Configure(CommandConfigurationBuilder<DeleteDeviceCommand> builder) =>
        builder.RequirePermissions("permission.dm.devices.delete").RequireExpectedVersion();
}
```

### Registry

`CommandConfigurationRegistry` copies `AggregateFeatureRegistry`'s shape: `FromAssemblies(...)` scans for
closed `ICommandConfiguration<>` implementations, runs each through `Activator` with
`BindingFlags.DoNotWrapExceptions`, and answers `For(Type requestType)`.

It refuses to build, naming the offending type(s), when:

- two configurations declare one request type;
- a configuration has no parameterless constructor;
- `RequireExpectedVersion()` is declared for a type that is not an `ICommand<>` (a query never carries an
  expected version);
- `Configure` throws (for example a malformed permission) — rethrown as `InvalidOperationException` naming
  the configuration type, with the original as inner exception.

### Sources and registration (`AddSerginCore`)

`CommandConfigurationAssembly(Assembly Assembly)` is a singleton source record, the same pattern as
`AssemblyIntegrationEventSource`. `AddSerginCore` registers one per local module `ContractsAssembly` and one
per remote module `ContractsAssembly`, and registers the registry as a singleton built from every source:

```csharp
builder.Services.AddSingleton(provider => CommandConfigurationRegistry.FromAssemblies(
    provider.GetServices<CommandConfigurationAssembly>().Select(source => source.Assembly)));
```

Built from sources rather than eagerly, so a test host can register its own assembly for test-only
commands. A small hosted service, `CommandConfigurationGuard`, resolves the registry in `StartAsync`, so a
bad configuration fails host start in every environment, not the first send.

### Behaviors

- `PermissionCheckPipelineBehavior` takes `CommandConfigurationRegistry` and replaces
  `GetCustomAttribute<RequiredPermissionsAttribute>()` with `registry.For(request.GetType())`. An empty
  `RequiredPermissions` passes.
- `ExpectedVersionPipelineBehavior` does the same for `RequiresExpectedVersion`. The stale-version catch is
  unchanged.

Pipeline order is unchanged: permission, expected version, validation.

### Migration

Every attributed record gets a configuration and loses its attributes:

- DeviceManagement (this repo): 13 records — Devices GetOne/GetList/Delete/Update; Manufacturers
  GetOne/GetList/Delete/Update; DeviceModels GetOne/GetList/Add/Rename/Remove.
- UserAccess (submodule): `GetUserByIdQueryCommand`, `GetUserListQueryCommand`,
  `ProvisionExternalUserCommand`.
- Tests: the test-only commands in `Events/OutboxTestTypes.cs` and
  `Concurrency/ExpectedVersionPipelineTests.cs`; their hosts register the test assembly as a
  `CommandConfigurationAssembly`.

Doc comments that name the attributes are updated (`ExpectedVersionEndpointFilter`, `ScopedSerginDispatcher`,
`UserContextAccessor`, `RelayUserContext`, `IExternalIdentityResolver`, `IListQueryHandler`,
`AggregateFeatureBuilder`, `SerginNavItem`), as are the CLAUDE.md files, READMEs and both `add-feature`
skills.

## Testing

- New `Commands/CommandConfigurationRegistryTests.cs`: declared settings are what `For()` answers; an
  unconfigured type answers `None`; two `RequirePermissions` calls append; each of the four refusals names
  the offending type.
- Regression proof that behavior is unchanged: `ExpectedVersionPipelineTests`, `DispatcherUserContextTests`,
  `DeviceManagementConcurrencyTests`, `DeviceManagementSoftDeleteTests`, `DeviceListQueryTests`, and the
  outbox tests that send a permission-guarded command as the relay identity.

## Delivery

Three repos, landing together because the attributes are deleted:

1. **Sergin.SharedKernel** — mechanism, behaviors, `AddSerginCore`, guard, attribute deletion, docs.
2. **Sergin.UserAccess** — three configurations, docs, `add-feature` skill.
3. **Sergin.MeterMinder** — DeviceManagement configurations, tests, docs, `add-feature` skill, both
   submodule bumps.

## Out of scope

- Any-of permissions, query caching, timeouts, log or redaction policy.
- Deriving Blazor nav-item permissions (`SerginNavItem.RequiredPermission`) from the registry.
- Resource-based authorization (a permission that depends on the loaded aggregate).
