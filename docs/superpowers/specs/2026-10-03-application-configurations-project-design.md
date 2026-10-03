# Application.Configurations project per module

Date: 2026-10-03
Status: approved design, not implemented

## Problem

A module's platform policy is declared in two places today:

- **Command configurations** (`ICommandConfiguration<T>`, 13 in DeviceManagement, 2 in UserAccess) live in
  `.Application.Contracts`, beside their request records.
- **Aggregate feature configurations** (`IAggregateFeatureConfiguration<T>`, 2 in DeviceManagement) live in
  `.Application`, in each root's folder.

`.Application.Contracts` exists so presentation projects and remote callers can depend on request and
response shapes only. The command-configuration design (`2026-10-01-command-configuration-design.md`) moved
policy off the records — its goal 3, "contract records carry platform policy" — but left the configuration
classes in the same assembly, because a remote gateway reads only `ContractsAssembly` and must refuse a
forbidden call before the gRPC hop. The Contracts assembly therefore still carries policy.

## Goal

Keep `.Application.Contracts` pure request/response shapes (plus integration events), and give every
per-module declaration — command and aggregate — one home, without losing the gateway's pre-hop permission
check.

## Decisions

1. **One new project per module: `Sergin.<Module>.Application.Configurations`.** It holds every
   `<Record>Configuration` and every `<Root>AggregateFeatureConfiguration`. It references
   `.Application.Contracts` only; `.Domain` and `Sergin.SharedKernel.Application` arrive through it.
2. **The module contract names the assembly.** `ISerginModule` and `ISerginRemoteModule` each gain
   `Assembly ConfigurationsAssembly { get; }`. A remote module ships Contracts and Configurations, so the
   gateway still builds its `CommandConfigurationRegistry` and refuses before the hop (decision 2 of the
   2026-10-01 design survives).
3. **No new SharedKernel project.** The configuration types stay in `Sergin.SharedKernel.Application`
   (`Commands/Configuration/`, `Aggregates/`). A separate leaf would buy no isolation today — module
   Configurations already reaches `SharedKernel.Application` through Contracts (`ICommand<T>` lives there) —
   and would force `IBaseCommand` down a layer to break the cycle with the pipeline behaviors that read the
   registry. Revisit only if a real need for the leaf appears.
4. **No behavior change.** Same registries, same behaviors, same EF convention, identical `Configure` bodies.
   No migration, no model snapshot change.
5. **One placement guard for both kinds.** A configuration found in any assembly other than
   `ConfigurationsAssembly` fails host start, naming each type.

Rejected alternatives:

- **Command configurations into `.Application`, no new project.** Contracts becomes pure at zero project
  cost, but a remote gateway can no longer check permissions before the gRPC hop — the remote server still
  refuses, at the price of a wasted round trip and lost defense in depth. Reverses a deliberate decision.
- **Also split `Sergin.SharedKernel.Application.Configurations`.** Symmetric, but costs a project and a moved
  public type (`IBaseCommand`) for no isolation gain today. See decision 3.

## Design

### Module project

`Sergin.MeterMinder.DeviceManagement.Application.Configurations` and
`Sergin.UserAccess.Application.Configurations`:

- `Microsoft.NET.Sdk`, one `ProjectReference` to the module's `.Application.Contracts`.
- Folders mirror where each class lives today:
  - command configurations: `<Aggregate>[/<ChildEntity>]/Commands/<Feature>/<Record>Configuration.cs`
  - aggregate configurations: `<Aggregate>/<Root>AggregateFeatureConfiguration.cs`
- Namespaces follow the project (`…Application.Configurations.Devices.Commands.Delete`). Discovery is by
  reflection, so the namespace carries no meaning to the scan.
- Classes stay `internal sealed` with a parameterless constructor.
- Marker class `<Module>ApplicationConfigurationsAssemblyReference` exposing `typeof(...).Assembly`, in the
  shape of the existing Contracts marker.
- A `GlobalUsings.cs` only if the moved files need it; prefer explicit usings matching today's files.
- UserAccess gets the project even though it has no aggregate configuration; its `DbContext` keeps no
  `AggregateFeatures` override.

### References

| Project | Change |
|---|---|
| `.Application.Configurations` | new; references `.Application.Contracts` |
| `.Infrastructure.Data` | adds `.Application.Configurations` (keeps `.Application` for `IUnitOfWork`) |
| `Sergin.<Module>` (composition root) | adds `.Application.Configurations` |
| `.Application` | unchanged — handlers never read policy |
| `.Presentation.*` | unchanged — they reference Contracts and now see no policy types |

`Sergin.MeterMinder.slnx` lists each new project in the module's `Application` solution folder next to
Contracts.

### DbContext

`DeviceManagementDbContext.AggregateFeatures` becomes
`AggregateFeatureRegistry.FromAssemblies([DeviceManagementApplicationConfigurationsAssemblyReference.Assembly])`.
The design-time factory needs nothing new: the EF copy is still built statically, without DI.

### SharedKernel

`Sergin.SharedKernel.Modules`:

- `ISerginModule.ConfigurationsAssembly` — the assembly holding the module's command and aggregate
  feature configurations.
- `ISerginRemoteModule.ConfigurationsAssembly` — same; the gateway reads command configurations from it.

`Sergin.SharedKernel.Hosts` / `SerginCoreExtensions.AddSerginCore`:

- Command configuration sources: `CommandConfigurationSource.FromAssembly(module.ConfigurationsAssembly)`
  for every local and every remote module (was `ContractsAssembly`).
- Aggregate feature registry: `AggregateFeatureRegistry.FromAssemblies(localModules.Select(m => m.ConfigurationsAssembly))`
  (was `ApplicationAssembly`).
- Unchanged scans: integration events from `ContractsAssembly`; MediatR, validators and translators from
  `ApplicationAssembly`.

`Sergin.SharedKernel.Application`:

- A static helper on the aggregate side that lists the closed `IAggregateFeatureConfiguration<>` types in
  an assembly, matching what `CommandConfigurationSource.FromAssembly(...).ConfigurationTypes` already gives
  for commands. The guard below uses both.

### Placement guard

Replaces today's "command configuration found in ApplicationAssembly" check. Before anything is registered,
`AddSerginCore` scans:

- every local module's `ApplicationAssembly` and `ContractsAssembly`,
- every remote module's `ContractsAssembly`,

for closed `ICommandConfiguration<>` or `IAggregateFeatureConfiguration<>` types. If any are found it throws
`InvalidOperationException` listing each full type name and saying: move each to the module's
`.Application.Configurations` project. A misplaced command configuration would otherwise leave its request
unprotected; a misplaced aggregate configuration would be caught later by `AggregateFeatureGuard`, but with a
less direct message.

A module whose `ConfigurationsAssembly` equals its `ContractsAssembly` or `ApplicationAssembly` would defeat
the guard. The guard skips an assembly that is also the module's `ConfigurationsAssembly`, so such a module
composes, but it is not a supported shape; the docs say so.

## Rollout

Three PRs, merged in order, back to back:

1. **Sergin.SharedKernel** — interface properties, `AddSerginCore` reads, the aggregate-type helper, the
   widened guard; `.claude/CLAUDE.md` and README.
2. **Sergin.UserAccess** — new project, 2 command configurations moved, `UserAccessModule.ConfigurationsAssembly`;
   its `.claude/CLAUDE.md` and `add-feature` skill.
3. **Sergin.MeterMinder** — bump both submodules; DeviceManagement project with 13 command and 2 aggregate
   configurations moved; `DeviceManagementModule.ConfigurationsAssembly`; `DbContext` marker; `.slnx`; tests;
   root and module CLAUDE.md; `add-feature` and `add-module` skills.

PR 1 does not build in a host until PRs 2 and 3 exist: every `ISerginModule` implementer must add the
property. Develop all three together in one host worktree.

## Testing

- `CommandConfigurationPlacementTests`: the test module's `ConfigurationsAssembly` points at a clean
  assembly. Cases: a command configuration in `ApplicationAssembly`; a command configuration in
  `ContractsAssembly`; an aggregate configuration in `ApplicationAssembly`. Each fails composition naming the
  type and `.Application.Configurations`.
- `DeviceGrpcRoundTripTests`: the gateway's registry source becomes the Configurations assembly.
- Unchanged suites prove no regression: `Commands/`, `Aggregates/`, `Audit/`, `SoftDelete/`, `Concurrency/`,
  `Validation/`, `Authentication/`.
- Model check: `dotnet ef migrations add Probe` for both modules scaffolds an empty migration; discard it.
- `dotnet build Sergin.MeterMinder.slnx` and the full integration suite (Docker).

## Out of scope

- New configuration kinds (tenant scoping, caching, timeouts).
- Moving validators out of `.Application`.
- The stray `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc` directory
  beside the gRPC trio — checked separately.
