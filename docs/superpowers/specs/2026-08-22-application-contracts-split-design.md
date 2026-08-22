# Application Contracts Project Split — Design Spec

- **Date**: 2026-08-22
- **Status**: Approved (brainstorming dialogue, all sections signed off)
- **Goal**: Give each module a thin, shared-source-of-truth project for its MediatR command/query request and response record shapes, so presentation layers (`.Presentation.WebApi`, `.Presentation.Blazor`, and — for DeviceManagement — `.Presentation.Grpc`) can depend on request/response shapes without referencing the assembly that also holds handler implementations, repository interfaces, and `IUnitOfWork`. This is **Approach B**: a pure move, no primitivization, no behavior change.

## Problem

Today each module's `.Application` project holds two things in one assembly: the MediatR command/query request and response records, and the handlers/repository interfaces/`IUnitOfWork` that implement the write/read logic against them. Every presentation layer references `.Application` solely to see the request/response record shapes — the handlers are already `internal sealed`, so presentation gets nothing usable from them. In return, presentation gets an unwanted transitive path through the module's `.Domain` and every other type `.Application` happens to expose.

This is most acute for `.Presentation.Grpc`, DeviceManagement's client-side implementation of the dual-mode dispatch mechanism (see `docs/superpowers/specs/2026-08-21-dispatch-contract-design.md`). If `Sergin:Dispatch:Modules` is ever flipped to `Remote` for a module, the process holding `.Presentation.Grpc`'s `IRemoteInvoker<,>` implementations is, in principle, a client talking to a module running in a different process — it has no business needing the handler-bearing assembly at all. Today it gets it anyway, because that's the only place the command/response types live.

## Chosen approach: Approach B — pure move, no primitivization

Split each module's request/response records out of `.Application` into a new `.Application.Contracts` project. Everything else about the types is untouched: same namespace, same domain-typed constructor arguments, same `[RequiredPermissions]` attributes, same `sealed record` shape. Presentation layers swap their `ProjectReference` from `.Application` to `.Application.Contracts`. No command/handler signature changes anywhere, no WebApi DTO deletions, no behavior change at all.

Approach B is a deliberate stepping stone. It isolates the one genuinely risky part of this change — the dispatch route-resolver fix, see below — from any type-shape change, so the risky part can be verified independently before a later pass attempts primitivization.

### Rejected alternatives

**Approach A (rejected for now)** — same project split, but additionally changes command records to take primitive constructor arguments instead of domain value objects (e.g. `CreateDeviceCommand(string DeviceId, Guid ManufacturerId)` instead of `CreateDeviceCommand(DeviceId DeviceId, ManufacturerId ManufacturerId)`), which would remove the Contracts project's dependency on `.Domain` entirely and let WebApi endpoints bind `[FromBody]` straight to the command instead of mapping through a `NewDeviceModel`/`NewUserModel` DTO. Deferred rather than dropped: the user has real plans for a `Remote` gRPC host, just not immediately, so primitivization is future work once that's closer.

**Approach C (rejected)** — no new project at all: make repository interfaces and `IUnitOfWork` `internal` with `InternalsVisibleTo`, and primitivize commands in place inside the existing `.Application` project. Cheaper to land, but it doesn't reduce the domain-type surface reachable from presentation (the assembly boundary itself is what a future remote client needs to avoid), and it doesn't give a future remote gRPC client any way to reference request/response shapes without also referencing handler-adjacent code.

## Non-Goals

- **Primitivizing command constructors.** Deferred to a future Approach-A follow-up; not part of this change.
- **Deleting `NewDeviceModel`/`NewUserModel`/`NewUserFormModel`/`NewDeviceFormModel`.** These Blazor/WebApi form/request models are explicitly out of scope and must **not** move into the new Contracts project — they're presentation-specific, need DataAnnotations plus a mutable/default-constructible shape for `EditForm` binding, and moving them would pressure the command records toward a shape that breaks the `sealed record` convention.
- **Adding FluentValidation validators.** None exist today; this change doesn't add any.
- **Changing MediatR handler registration/scanning behavior.** `AddSerginCore` keeps scanning `ApplicationAssembly` only — the Contracts assembly is never scanned for handlers, because it has none.

## Architecture

| Piece | Home | Content |
|---|---|---|
| `Sergin.MeterMinder.DeviceManagement.Application.Contracts` | **new**, `src/Modules/DeviceManagement/` (this host repo) | DeviceManagement's command/query request + response records, moved verbatim |
| `Sergin.UserAccess.Application.Contracts` | **new**, `src/Modules/UserAccess/` (`Sergin.UserAccess` submodule repo) | UserAccess's command/query request + response records, moved verbatim |
| `ISerginModule.ContractsAssembly` | `Sergin.SharedKernel.Modules` (`Sergin.SharedKernel` submodule repo) | New interface member, implemented by `DeviceManagementModule`/`UserAccessModule` |
| `ModuleDispatchRouteResolver`, `SerginWebUiExtensions` | `Sergin.SharedKernel.Hosts.WebUi` | Assembly→schema lookup extended to map both `ApplicationAssembly` and `ContractsAssembly` to the same module |
| `Sergin.MeterMinder.slnx` | this host repo | Two new project entries |
| `/add-feature`, `/add-module` skills | `.claude/skills/` | Updated to scaffold into `.Application.Contracts`, not `.Application` |

## 1. New projects

`Sergin.MeterMinder.DeviceManagement.Application.Contracts` (this host repo, alongside the existing DeviceManagement module projects under `src\Modules\DeviceManagement\`) and `Sergin.UserAccess.Application.Contracts` (in the `Sergin.UserAccess` submodule repo, alongside its existing module projects under `src\Modules\UserAccess\`).

Each references only:

- `Sergin.SharedKernel.Application` — for `ICommand<T>`/`IQuery<T>`/`RequiredPermissionsAttribute`, etc.
- its own module's `.Domain` project — kept per Approach B; the command constructors stay domain-typed, so this reference is required.

Each carries a public marker/assembly-reference class, analogous to the existing per-module assembly-reference pattern used for `UiAssembly` (e.g. `DeviceManagementBlazorAssemblyReference`). Name them `DeviceManagementApplicationContractsAssemblyReference` and `UserAccessApplicationContractsAssemblyReference`; their sole purpose is exposing `typeof(ThisClass).Assembly` for `ISerginModule.ContractsAssembly`.

## 2. What moves, verbatim, namespace-preserved

**DeviceManagement**, from `Application/Devices/Commands/...` and `Application/Manufacturers/Commands/...`:
- `CreateDeviceCommand.cs`, `CreateDeviceCommandResponse.cs`
- `GetDeviceByIdQueryCommand.cs`, `DeviceQueryResponse.cs`
- `GetDeviceListItem.cs`
- `GetManufacturerByIdQueryCommand.cs` and its response record, if present (CLAUDE.md documents `permission.dm.manufacturers.read` on this query — verify it and its response type when touching the module and move both if they exist)

**UserAccess**, from `Application/Users/Commands/...`:
- `CreateUserCommand.cs`, `CreateUserCommandResponse.cs`
- its GetOne/GetList equivalents (query request + response records)

Any `[RequiredPermissions(...)]` attribute already decorating one of these record types travels with it unchanged — the attribute itself is not touched, only relocated to the new project along with the type it decorates.

**Namespaces do not change.** A type keeps its existing namespace (e.g. `Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create`) even though it now lives in a different assembly/project. This means no `using` statement anywhere in the codebase needs to change — only `.csproj` `<ProjectReference>` elements.

**What does NOT move**: handler classes (`*CommandHandler`/`*QueryCommandHandler`, all already `internal sealed`), query-repository interfaces (`IGetDeviceQueryRepository`, `IGetDeviceListQueryRepository`, and the equivalent UserAccess interfaces), `IUnitOfWork` interfaces (`IDeviceManagementUnitOfWork`, `IUserAccessUnitOfWork`), and anything under `.Infrastructure`/`.Infrastructure.Data`. These all stay in `.Application` (or below it) exactly where they are.

## 3. Dependency graph after the change

```
SharedKernel.Application
  → <Module>.Domain
      → <Module>.Application.Contracts
          → <Module>.Application                 (adds ProjectReference to .Contracts)
              → <Module>                          (composition root; unchanged reference to
                                                     .Application, gains .Contracts transitively —
                                                     needed directly or transitively for the
                                                     ContractsAssembly marker type to compile against)

<Module>.Presentation.WebApi     → <Module>.Application.Contracts   (replaces .Application reference)
<Module>.Presentation.Blazor     → <Module>.Application.Contracts   (replaces .Application reference)
<Module>.Presentation.Grpc       → <Module>.Application.Contracts   (replaces .Application reference,
                                                                       DeviceManagement only)
```

Each presentation project **replaces** its `ProjectReference` to `.Application` with one to `.Application.Contracts` — it does not add the new reference alongside the old one. Per the prior investigation this repository's design review confirmed, none of the three presentation layers use anything from `.Application` beyond the request/response record shapes: handlers are `internal`, and repository/`IUnitOfWork` interfaces are never referenced from presentation. Verify this holds for each project as the reference is swapped, but no exceptions are expected.

## 4. The SharedKernel change — the risky part

This is the one piece of the change that is not a pure, safe move, and it must be called out prominently: the instant a module's command/response types move to a new assembly, `RoutingSerginUiDispatcher`'s per-request module lookup — which resolves a request's module via `requestType.Assembly` — starts throwing `does not belong to any registered module's ApplicationAssembly` for every dispatch of that module's requests. Left unfixed, this breaks every page in the UI host for that module.

`ISerginModule` (in `Sergin.SharedKernel.Modules`) gains a new member:

```csharp
public interface ISerginModule
{
    // existing members: Schema, ApplicationAssembly, AddServices, MigrateAsync, ...
    Assembly ContractsAssembly { get; }
}
```

`DeviceManagementModule` and `UserAccessModule` each implement it, returning their new marker class's assembly (`typeof(DeviceManagementApplicationContractsAssemblyReference).Assembly`, respectively for UserAccess).

`ModuleDispatchRouteResolver` (`Sergin.SharedKernel.Hosts.WebUi`, `Dispatching/ModuleDispatchRouteResolver.cs`) and wherever `SerginWebUiExtensions` builds its assembly→schema dictionary from `modules.ToDictionary(module => module.ApplicationAssembly, ...)` must change so that **both** `ApplicationAssembly` and `ContractsAssembly` map to the same module/schema entry. This is what lets a request type declared in `.Application.Contracts` still resolve to the right module.

**This resolver fix must land in the same commit/PR as the first type move for that module.** There is no safe intermediate state where a module's types have moved to `.Application.Contracts` but the resolver hasn't been updated to recognize `ContractsAssembly` — that state is dispatch-broken for every request of that module, not degraded, broken.

`AddSerginCore`'s MediatR handler scan is unaffected by any of this and must continue to scan `ApplicationAssembly` only — handlers never move, so there is nothing new for the scanner to find in `.Application.Contracts`.

## 5. Build/tooling surface

- **`Sergin.MeterMinder.slnx`** gains 2 new project entries, one per module's new Contracts project.
- **`/add-feature`** (`.claude/skills/add-feature/SKILL.md`) needs updating so a newly scaffolded feature's command/query request and response records are created in the module's `.Application.Contracts` project, and the presentation-layer `ProjectReference`s it wires point at `.Contracts`, not `.Application`. Without this update, the next scaffolded feature regresses back to the pre-split shape.
- **`/add-module`** (`.claude/skills/add-module/SKILL.md`) needs updating so a brand-new module is scaffolded with a `.Application.Contracts` project from the start, including its `ContractsAssembly` marker class and `ISerginModule.ContractsAssembly` implementation.
- **CLAUDE.md files** in all three repos need updates: this repo's root `.claude/CLAUDE.md` (per-module project layering section), and the SharedKernel and UserAccess submodules' own `.claude/CLAUDE.md` files, describing the new project and the `ISerginModule.ContractsAssembly` member.

## 6. Rollout sequencing

This spans three git repos — this host repo, and the `Sergin.SharedKernel` and `Sergin.UserAccess` submodule repos — so sequencing matters:

1. **SharedKernel repo PR**: add `ISerginModule.ContractsAssembly`, fix `ModuleDispatchRouteResolver`/`SerginWebUiExtensions`'s dictionary construction. No module implements the new member yet at this point in the sequence — this is a coordination point, not something to solve unilaterally in the SharedKernel PR. Whether that means landing it as a purely additive interface member that existing modules can adopt in a later commit, or bundling the `DeviceManagementModule`/`UserAccessModule` implementations into the same wave since all three repos get bumped together anyway, is a call to make when this step is actually executed — this spec flags the coordination need rather than prescribing a specific default-interface-member workaround, since one may not be necessary if the submodule bumps land together in practice.
2. **UserAccess repo PR**: add `Sergin.UserAccess.Application.Contracts`, move UserAccess's command/response types into it (§2), implement `ContractsAssembly` on `UserAccessModule`, update `.Application`'s and its presentation projects' references (§3).
3. **This host repo**: add `Sergin.MeterMinder.DeviceManagement.Application.Contracts`, move DeviceManagement's types (§2), implement `ContractsAssembly` on `DeviceManagementModule`, update `Sergin.MeterMinder.slnx`, bump both submodule pointers to the commits from steps 1–2, update presentation project references (§3), update the two scaffolding skills and all three CLAUDE.md files (§5).

## 7. Testing / verification

No new test project is needed — this is a structural move, and the existing integration suite already exercises the same request/response types through their current call paths:

- **`tests/Sergin.MeterMinder.IntegrationTests.All/Shell/ModulePageRenderingTests.cs`** — would catch a route-resolver break, since every module page's dispatch would start throwing the moment its module's types move without the `ContractsAssembly` fix landing alongside.
- **`tests/Sergin.MeterMinder.IntegrationTests.All/Users/CreateAndGetUserTests.cs`** — the one write-path test, dispatches `CreateUserCommand` via `ISerginUiDispatcher` exactly like a Blazor page would; exercises UserAccess's moved command type end to end.
- **`DeviceGrpcRoundTripTests`** — spins up a real loopback Kestrel gRPC server; would catch a `.Presentation.Grpc` reference-swap mistake for DeviceManagement.

Verification order: `git submodule update --init --recursive`, then `dotnet build Sergin.MeterMinder.slnx` as the first gate (catches broken `ProjectReference`s), then `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj` (requires Docker, per this repo's CLAUDE.md).

## Risks

- **The route-resolver break is a hard, all-or-nothing cutover per module.** There is no incremental, type-by-type migration path that stays dispatch-safe — a module's types and its resolver fix move together or the module is broken (§4).
- **Cross-repo coordination.** Three PRs across three repos, with sequencing that matters (§6). A partially-landed state — for example the SharedKernel PR merged but the UserAccess repo not yet updated — could break UserAccess module resolution if `ContractsAssembly` becomes a required member with no safe default.
- **Scaffolding-skill drift.** If `/add-feature`/`/add-module` aren't updated (§5), the next scaffolded feature lands its command/response types back in `.Application`, silently regressing the split for that one feature.
- **The Contracts project is not fully dependency-light.** It still pulls in `SharedKernel.Application`'s transitive dependencies (MediatR, FluentValidation, Localization.Abstractions) plus, in Approach B specifically, the module's `.Domain`. It is not yet safe to hand to a genuinely external/remote client process without also doing Approach A's primitivization later — this spec does not claim otherwise.

## Open follow-ups (explicitly out of scope for this spec)

- Primitivizing command constructors and removing the `.Domain` reference from `.Application.Contracts` (Approach A).
- Deciding the specific mechanism (if any is needed beyond a coordinated multi-repo bump) for landing `ISerginModule.ContractsAssembly` without a transient broken state across the three repos (§6, step 1).
- Confirming the exact set of Manufacturer command/response types that exist under `Application/Manufacturers/Commands/` before executing the DeviceManagement move (§2) — this spec names `GetManufacturerByIdQueryCommand` from CLAUDE.md's existing mention of its permission, but the full list should be confirmed against the actual source tree at execution time.
