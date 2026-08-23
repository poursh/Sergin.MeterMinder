# Remote Dispatch via MediatR Pipeline — Microservice-Readiness Design

- **Date**: 2026-08-23
- **Status**: Approved (brainstorming dialogue, all sections signed off)
- **Goal**: Replace the runtime Local/Remote branch inside the dispatch sender with a **composition-time** choice — a module a host doesn't run locally gets a lightweight proxy that registers real `IRequestHandler`s forwarding to gRPC, so Local and Remote calls both flow through the same MediatR pipeline (permission check, validation, any future behavior) uniformly. Prepares SharedKernel for real multi-host deployment (a gateway host + per-module hosts) without building that topology yet.
- **Supersedes**: `2026-08-22-sergin-sender-design.md` in full. That spec's placement of the dispatch contract in `Sergin.SharedKernel.Application`/`Infrastructure` and its "every presentation adapter" scope are both reversed here — see Decisions 1 and 5. Everything from `2026-08-21-dispatch-contract-design.md` §1–§4, §6–§7 (transport, per-feature routing, error mapping, list-query fallout) still stands; only §5 (permission double-check) and the two later specs' placement/scope decisions change.

## Problem

`RoutingSerginSender` decides Local vs Remote **per request, at runtime**, by asking `IDispatchRouteResolver.IsRemote(requestType)` (backed by `Sergin:Dispatch:Modules` config). This has two costs that only surface once you take "microservice-ready" seriously:

1. **Remote calls skip the MediatR pipeline.** `RoutingSerginSender`'s Remote branch resolves `IRemoteInvoker<TRequest,TResponse>` and calls it directly — never touches `ISender.Send`. `ValidationPipelineBehavior` (and any future cross-cutting behavior) never runs for a Remote call. Only `PermissionCheckPipelineBehavior`'s job is covered, and only because `RoutingSerginSender` hand-rolls an equivalent check itself before branching — a second, manually-maintained copy of what the pipeline already does for Local.
2. **A single host binary always references every module's real `.Application`/`.Infrastructure`, even for a module it only ever calls Remote.** Today that's moot — there is one host and every module is `Local`. But a real gateway host (call it HostA, fronting `DeviceManagement`+`UserAccess` which actually run in HostB/HostC) should not need `DeviceManagement.Application`'s handlers, repositories, or `DbContext` compiled into it at all. Nothing in the current design gives that host a way to leave those projects out — `Sergin:Dispatch:Modules` only changes behavior at startup, not what's on the reference graph.

## Decisions made during brainstorming

1. **Dispatch contract moves back to `Sergin.SharedKernel.Presentation.Blazor`, renamed `ISerginDispatcher`.** The fresh-scope-per-call problem it solves (Blazor Server's "scoped" is the SignalR circuit's lifetime, not a request's) is Blazor-specific — it has nothing to do with Local/Remote routing, and never did. Once Remote calls go through the ordinary MediatR pipeline (Decision 2), WebApi endpoints have no remaining reason to route through a wrapper at all: their DI scope already matches one request, and the permission pre-check the wrapper used to add on top is now redundant everywhere, not just for Local (see Decision 4). This reverses `2026-08-22-sergin-sender-design.md` Decision 1 (all-presentation scope) and Decision 3 (Application/Infrastructure placement).
2. **Local/Remote becomes a composition-time choice per module, not a runtime branch per request.** A host's `Program.cs` puts each module in exactly one of two collections it hands to `AddSerginCore`: `localModules` (real `ISerginModule` — `AddServices`, `ApplicationAssembly` scanned by MediatR, `DbContext`, `MigrateAsync`) or `remoteModules` (new `ISerginRemoteModule` — `AddRemoteServices` only, no `ApplicationAssembly`, no `DbContext`). `Sergin:Dispatch:Modules`, `IDispatchRouteResolver`, `ModuleDispatchRouteResolver`, `DispatchModeOptions`(+`Validator`) are all deleted — there is no longer a per-request question to answer at runtime.
3. **Remote modules register real `IRequestHandler`s that forward to gRPC — via a shared generic bridge, not per-feature handler classes.** `RemoteForwardingHandler<TRequest,TResponse> : IRequestHandler<TRequest, ErrorOr<TResponse>>` (new, `Sergin.SharedKernel.Infrastructure/Dispatching/`) wraps the existing `IRemoteInvoker<TRequest,TResponse>` — write once, register per remote-enabled feature with one explicit DI line (`services.AddTransient<IRequestHandler<TRequest,ErrorOr<TResponse>>, RemoteForwardingHandler<TRequest,TResponse>>()`), inside each module's own `.Presentation.Grpc` project (already compile-time isolated from `.Application`/`.Infrastructure` — see Decision 5). Explicit per-feature registration, not open-generic auto-registration: an open-generic MediatR registration would silently match *any* request type of matching arity and fail at `Send`-time with a confusing missing-`IRemoteInvoker` constructor-dependency error instead of a clean "no handler for this request" failure.
4. **The manual permission pre-check is deleted, not carried forward.** It existed only because Remote calls used to bypass the pipeline. Once every call — Local or Remote — resolves through `ISender.Send` and therefore through `PermissionCheckPipelineBehavior`, a second hand-rolled check is pure duplication with no gap left to cover. `ISerginDispatcher`'s job shrinks to exactly one thing: open a fresh scope, resolve `ISender`, call `Send`.
5. **No new project for the remote side — reuse each module's existing `.Presentation.Grpc` project.** It already references only `.Application.Contracts` (never `.Application`/`.Infrastructure` — verified against `Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj`), so it already satisfies the compile-time isolation a gateway host needs. It already holds `IRemoteInvoker<,>` implementations and the gRPC client stub. It gains one new file: an `AddDeviceManagementRemoteServices`-shaped extension implementing the new `ISerginRemoteModule.AddRemoteServices`.
6. **`Sergin.MeterMinder.Hosts.All` (dev/test/QA) is unaffected in behavior.** It puts both modules in `localModules` and passes an empty `remoteModules` collection — identical runtime behavior to today, minus one now-unnecessary config section (`Sergin:Dispatch:Modules` no longer required at all, for any host).
7. **No gateway host (HostA) is built by this spec.** This spec builds the SharedKernel-level and per-module machinery (`ISerginRemoteModule`, `RemoteForwardingHandler`, `AddRemoteServices`) and proves it with a test, the same "live-but-unhosted" posture `IRemoteInvoker`/`DeviceGrpcService` already have today (no host maps `DeviceGrpcService` either). Standing up a real second host project is explicit future work — see Non-goals.

## Non-goals

- **Does not stand up HostB/HostC or a real gateway HostA.** Those remain hypothetical, same as before this spec. This spec makes them *possible without further SharedKernel work*, not *present*.
- **Does not change transport, per-feature routing shape, error mapping, or list-query handling** — `IRemoteInvoker<TRequest,TResponse>`, the generated proto types, `ErrorReplyExtensions`, and `SendListAsync` are untouched.
- **Does not touch `DeviceGrpcService`.** Still the server-side target, still ends in `ISender.Send`, still out of scope for the same reason as before.
- **Does not add identity/permission metadata propagation over gRPC** — still an open follow-up from the original dispatch-contract spec, untouched here.
- **Does not add authentication.**
- **Does not change `PermissionCheckPipelineBehavior` or `ValidationPipelineBehavior` themselves** — the change is only in what now reaches them (Remote calls, for the first time).

## Architecture

| Piece | Home | Change |
|---|---|---|
| `ISerginDispatcher` | `Sergin.SharedKernel.Presentation.Blazor/Dispatching/` | Renamed from `ISerginSender`, **moved back** from `Sergin.SharedKernel.Application` |
| `ScopedSerginDispatcher` | `Sergin.SharedKernel.Presentation.Blazor/Dispatching/` | Renamed from `RoutingSerginSender`, **moved back** from `Sergin.SharedKernel.Infrastructure`. Drops the Local/Remote branch and the manual permission check — just fresh scope + `ISender.Send` |
| `IDispatchRouteResolver`, `ModuleDispatchRouteResolver`, `DispatchModeOptions`, `DispatchModeOptionsValidator` | — | **Deleted** |
| `SerginDispatcherExtensions` (the `SendListAsync<TItem>` helper) | `Sergin.SharedKernel.Presentation.Blazor/Dispatching/`, renamed from `SerginSenderExtensions` | Now extends `ISerginDispatcher` instead of `ISerginSender` |
| `RemoteForwardingHandler<TRequest,TResponse>` | **new**, `Sergin.SharedKernel.Infrastructure/Dispatching/` | Generic `IRequestHandler<TRequest, ErrorOr<TResponse>>` wrapping `IRemoteInvoker<TRequest,TResponse>` |
| `ISerginRemoteModule` | **new**, `Sergin.SharedKernel.Modules/` | `Schema`, `ContractsAssembly`, `AddRemoteServices(IServiceCollection, IConfigurationSection)` — no `ApplicationAssembly`, no `MigrateAsync` |
| `<Module>.Presentation.Grpc` (per module) | unchanged project, two new files, one new `ProjectReference` | Gains `Add<Module>RemoteServices` (registers the gRPC channel/client, the existing `IRemoteInvoker` impls, and one `RemoteForwardingHandler` registration line per remote-enabled feature) and `<Module>RemoteModule : ISerginRemoteModule` (the class a gateway host actually references — kept out of the composition root on purpose, see §5). New `ProjectReference` to `Sergin.SharedKernel.Infrastructure` for `RemoteForwardingHandler<,>` |
| `AddSerginCore` | `Sergin.SharedKernel.Hosts` | Signature gains `remoteModules` parameter; loses all `DispatchModeOptions`/`IDispatchRouteResolver`/`ISerginSender` registration; duplicate-schema guard now spans both collections |
| `AddSerginBlazorKit` | `Sergin.SharedKernel.Presentation.Blazor` | Regains `ISerginDispatcher`/`ScopedSerginDispatcher` registration (this is where it lived before `2026-08-22-sergin-sender-design.md` moved it out) |
| WebApi endpoints (10, both modules) | `.Presentation.WebApi` per module | Revert `ISerginSender sender` → `ISender sender`, `.SendAsync(` → `.Send(` |
| `Sergin.SharedKernel.Presentation.WebApi` | — | No longer needs to expose the dispatch contract to WebApi at all (it keeps its existing `Application` reference regardless — `ListQueryRequestModel.cs` needs `ListQuery<T>`/`IListQuery<T>` from there independently of dispatch) |

## 1. `ISerginDispatcher` (`Sergin.SharedKernel.Presentation.Blazor/Dispatching/ISerginDispatcher.cs`)

```csharp
public interface ISerginDispatcher
{
    Task<ErrorOr<TResponse>> SendAsync<TResponse>(
        IRequest<ErrorOr<TResponse>> request, CancellationToken cancellationToken = default);
}
```

Same signature `ISerginSender` had — this is a rename plus a move back, not a redesign of the contract shape.

## 2. `ScopedSerginDispatcher` (`Sergin.SharedKernel.Presentation.Blazor/Dispatching/ScopedSerginDispatcher.cs`)

```csharp
internal sealed class ScopedSerginDispatcher(IServiceScopeFactory scopeFactory) : ISerginDispatcher
{
    public async Task<ErrorOr<TResponse>> SendAsync<TResponse>(
        IRequest<ErrorOr<TResponse>> request, CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        return await sender.Send(request, cancellationToken);
    }
}
```

Everything `RoutingSerginSender` used to do beyond this — the permission pre-check, the `IDispatchRouteResolver`/`IRemoteInvoker` branch, the `ConcurrentDictionary` invoker-type cache — is gone. `PermissionCheckPipelineBehavior` inside `Send` is now the *only* place permission is checked, for both Local and Remote alike (see §3).

Registered back in `AddSerginBlazorKit()`:
```csharp
services.AddSingleton<ISerginDispatcher, ScopedSerginDispatcher>();
```
`Sergin.SharedKernel.Presentation.Blazor.csproj` needs its `PackageReference Include="MediatR"` restored (removed when `ISerginSender` left this project) and `GlobalUsings.cs` needs `global using MediatR;` restored — both undo the `2026-08-22` fix-wave commit that dropped them.

## 3. `RemoteForwardingHandler<TRequest,TResponse>` (`Sergin.SharedKernel.Infrastructure/Dispatching/RemoteForwardingHandler.cs`)

```csharp
internal sealed class RemoteForwardingHandler<TRequest, TResponse>(IRemoteInvoker<TRequest, TResponse> invoker)
    : IRequestHandler<TRequest, ErrorOr<TResponse>>
    where TRequest : IRequest<ErrorOr<TResponse>>
{
    public Task<ErrorOr<TResponse>> Handle(TRequest request, CancellationToken cancellationToken)
        => invoker.InvokeAsync(request, cancellationToken);
}
```

Pure forwarding — no logic of its own, so it's the one place to add shared remote-call behavior later (retry, tracing) without touching every feature. `Sergin.SharedKernel.Infrastructure`'s existing `ProjectReference`s to `Sergin.SharedKernel.Application` (needed independently, by `DefaultDateTimeProvider`/`DefaultLocalizer`/`DefaultEventDispatcher`) and `Sergin.SharedKernel.Presentation.Grpc` (needed for `IRemoteInvoker<,>`, added by the prior spec) both already cover what this file needs — no `.csproj` change required in this project.

## 4. `ISerginRemoteModule` (`Sergin.SharedKernel.Modules/ISerginRemoteModule.cs`)

```csharp
public interface ISerginRemoteModule
{
    string Schema { get; }

    Assembly ContractsAssembly { get; }

    void AddRemoteServices(IServiceCollection services, IConfigurationSection configuration);
}
```

Deliberately **not** `: ISerginModule` — a remote module has no `ApplicationAssembly` (no real handlers to scan), no `MigrateAsync` (no `DbContext` to migrate). It shares only the two things a remote-participating module actually needs: a schema identity and the assembly holding the request/response record types a caller sends.

## 5. Per-module `.Presentation.Grpc` — `AddRemoteServices`

Example, `Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/DeviceManagementRemoteServicesExtensions.cs` (new file):

```csharp
public static class DeviceManagementRemoteServicesExtensions
{
    public static IServiceCollection AddDeviceManagementRemoteServices(
        this IServiceCollection services, IConfigurationSection configuration)
    {
        string address = configuration["GrpcAddress"]
            ?? throw new InvalidOperationException("Missing gRPC address for the DeviceManagement remote module.");

        services.AddSingleton(_ => GrpcChannel.ForAddress(address));
        services.AddSingleton(p => new DeviceService.DeviceServiceClient(p.GetRequiredService<GrpcChannel>()));

        services.AddTransient<IRemoteInvoker<GetDeviceByIdQueryCommand, DeviceQueryResponse>, GetDeviceByIdGrpcInvoker>();
        services.AddTransient<
            IRequestHandler<GetDeviceByIdQueryCommand, ErrorOr<DeviceQueryResponse>>,
            RemoteForwardingHandler<GetDeviceByIdQueryCommand, DeviceQueryResponse>>();

        return services;
    }
}
```

**`ISerginRemoteModule` is implemented by a new, separate class living inside `.Presentation.Grpc` itself — `DeviceManagementRemoteModule` — not by `DeviceManagementModule`.** `DeviceManagementModule` (the composition root, `Sergin.MeterMinder.DeviceManagement`) references `.Infrastructure`, `.Presentation.WebApi`, and `.Presentation.Blazor` — all of which transitively pull in `.Application`. If `DeviceManagementModule` itself implemented `ISerginRemoteModule`, a gateway host would have to reference the whole composition root just to reach that one capability, defeating Decision 5's isolation claim before it's ever used. `DeviceManagementRemoteModule` instead lives where `.Presentation.Grpc` already is — isolated by construction:

```csharp
// Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/DeviceManagementRemoteModule.cs
public sealed class DeviceManagementRemoteModule : ISerginRemoteModule
{
    // Must match DeviceManagementDbContext.Schema ("dm"). Duplicated, not shared, because
    // .Presentation.Grpc must not reference .Infrastructure.Data — that's the whole point.
    public string Schema => "dm";

    public Assembly ContractsAssembly => DeviceManagementApplicationContractsAssemblyReference.Assembly;

    public void AddRemoteServices(IServiceCollection services, IConfigurationSection configuration)
        => services.AddDeviceManagementRemoteServices(configuration);
}
```

One registration pair (`IRemoteInvoker` + `RemoteForwardingHandler`) per remote-enabled feature; today that's the single `GetDeviceById` slice, matching what already exists. `.Presentation.Grpc`'s `.csproj` gains one new `ProjectReference` to `Sergin.SharedKernel.Infrastructure` (for `RemoteForwardingHandler<,>`) — safe: `SharedKernel.Infrastructure` has no upward reference to any module, so this can't create a cycle, and it doesn't pull in `DeviceManagement.Application`/`.Infrastructure`, so isolation holds.

## 6. `AddSerginCore` (`Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs`)

```csharp
public static IConfigurationSection AddSerginCore<TBuilder>(
    this TBuilder builder,
    IReadOnlyCollection<ISerginModule> localModules,
    IReadOnlyCollection<ISerginRemoteModule>? remoteModules = null)
    where TBuilder : IHostApplicationBuilder
{
    remoteModules ??= [];
    IConfigurationSection serginSection = builder.Configuration.GetRequiredSection(SectionName);

    string[] duplicateSchemas =
    [
        .. localModules.Select(m => m.Schema)
            .Concat(remoteModules.Select(m => m.Schema))
            .GroupBy(schema => schema, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
    ];
    // ... same "duplicate schema" guard as today, now spanning both collections — a schema
    // registered both Local and Remote in one process is exactly as invalid as registered twice.

    builder.Services.AddMediatR(options =>
    {
        foreach (ISerginModule module in localModules)
        {
            options.RegisterServicesFromAssembly(module.ApplicationAssembly);
        }

        options.AddOpenBehavior(typeof(PermissionCheckPipelineBehavior<,>));
        options.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
    });

    // ... existing event dispatcher, IDbConnectionFactory, localizer, IUserContext registrations (unchanged) ...

    foreach (ISerginModule module in localModules)
    {
        module.AddServices(builder.Services, serginSection);
    }

    foreach (ISerginRemoteModule remoteModule in remoteModules)
    {
        remoteModule.AddRemoteServices(builder.Services, serginSection);
    }

    return serginSection;
}
```

No assembly scan for `remoteModules` — a remote module's handlers are explicitly registered by its own `AddRemoteServices`, not discovered by convention (MediatR's scan finds concrete handler types in a scanned assembly; the bridge handler lives in `Sergin.SharedKernel.Infrastructure`, a different assembly than any `ContractsAssembly`, and is generic besides). The open-behavior registrations apply globally to every `Send` regardless of how its handler got registered, so `PermissionCheckPipelineBehavior`/`ValidationPipelineBehavior` wrap `RemoteForwardingHandler` calls exactly as they wrap real ones — this is the mechanism that makes Decision 4 true, not an assumption.

`AddSerginBlazorApp`/`AddSerginWebApi` both still call `AddSerginCore(modules)` — for now, with a single positional argument (their existing `modules` param renamed `localModules`) and no `remoteModules` argument, since neither host has one yet. `Sergin.MeterMinder.Hosts.All`'s `Program.cs` needs no change at all.

## 7. Consumer call sites

**Blazor pages** (6 across both modules): `[Inject] private ISerginSender Sender` → `[Inject] private ISerginDispatcher Dispatcher`. Pure rename, reverting `2026-08-22`'s rename in the other direction; call sites `Sender.SendAsync(...)`/`Sender.SendListAsync(...)` → `Dispatcher.SendAsync(...)`/`Dispatcher.SendListAsync(...)`.

**WebApi endpoints** (10 across both modules): `ISerginSender sender` → `ISender sender`, `sender.SendAsync(...)` → `sender.Send(...)`. Reverts `2026-08-22`'s endpoint migration. These endpoints are still live-but-unhosted (no WebApi host maps them) — this is a compile-time-only change today, same as the rest of that layer.

**`DeviceGrpcService`**: unchanged, per Non-goals.

**`add-feature` skill** (both copies — host repo and UserAccess repo): revert the WebApi endpoint template's injected type back to `ISender`; the Blazor page template's injected type back to `ISerginDispatcher`.

## 8. Testing

- **`CreateAndGetUserTests`**: resolves `ISerginDispatcher` (renamed back from `ISerginSender`) from `factory.Services` — `Sergin.MeterMinder.Hosts.All` is the Blazor host, references `Presentation.Blazor`, so this still resolves. No behavioral change: the fresh-scope-per-send property this test depends on (to prove the read round-trips through Postgres rather than the writing `DbContext`'s change tracker) is exactly what `ScopedSerginDispatcher` still provides.
- **`DeviceGrpcRoundTripTests`** needs real rework, not a rename:
  - Its bespoke MediatR setup in `InitializeAsync` currently can't register `PermissionCheckPipelineBehavior`/`ValidationPipelineBehavior` (both `internal` to `Sergin.SharedKernel.Application`, and this outer-repo test project has no `InternalsVisibleTo` grant into that assembly — only `Sergin.SharedKernel.Infrastructure` grants one today). Under the old design this didn't matter: `RoutingSerginSender`'s own client-side permission check was what the forbidden-path test actually exercised, never the pipeline. Under this design that client-side check is deleted (Decision 4) — so `RemoteDispatch_WithoutRequiredPermission_ReturnsForbidden` has nothing left to assert against unless the real pipeline behavior is reachable. **Add `[assembly: InternalsVisibleTo("Sergin.MeterMinder.IntegrationTests.All")]` to `Sergin.SharedKernel.Application/Properties/AssemblyInfo.cs`** (same pattern already used for `Sergin.SharedKernel.Infrastructure`) so the test's own `AddMediatR(...)` call can add `AddOpenBehavior(typeof(PermissionCheckPipelineBehavior<,>))` for real.
  - `BuildSender`/`FixedRouteResolver`/the `ISerginSender`/`IDispatchRouteResolver` registrations are deleted. The "remote" side of the comparison instead becomes: register `IRemoteInvoker<GetDeviceByIdQueryCommand,DeviceQueryResponse>` → `GetDeviceByIdGrpcInvoker` and `IRequestHandler<GetDeviceByIdQueryCommand, ErrorOr<DeviceQueryResponse>>` → `RemoteForwardingHandler<GetDeviceByIdQueryCommand,DeviceQueryResponse>` in a plain `ServiceCollection` alongside the real `PermissionCheckPipelineBehavior`, then resolve `ISender` and call `Send(command)` directly — no dispatcher of any kind needed on either side of the comparison now, since neither Local nor Remote goes through one anymore.
  - The three existing test cases (`ReturnsSameResultAsLocalHandler`, `ReturnsNotFound`, `ReturnsForbidden`) still make sense and should still exist, just re-pointed at plain `ISender.Send` on both sides with the permission behavior now real instead of simulated.
- **`ModulePageRenderingTests`**: unaffected.
- No test proves the composition-time isolation claim (a host that lists a module only in `remoteModules` truly never touches its `.Application`/`.Infrastructure`) beyond what already holds for `.Presentation.Grpc`'s existing `.csproj` reference list — there is no host yet that would need to prove it at runtime. Acceptable per Non-goals; a real gateway host's own test suite is where that would be proven.

## Migration checklist (for the implementation plan)

1. Move `ISerginSender` → `ISerginDispatcher` from `Sergin.SharedKernel.Application/Dispatching/` into `Sergin.SharedKernel.Presentation.Blazor/Dispatching/`; delete `IDispatchRouteResolver`.
2. Move `RoutingSerginSender` → `ScopedSerginDispatcher` from `Sergin.SharedKernel.Infrastructure/Dispatching/` into `Sergin.SharedKernel.Presentation.Blazor/Dispatching/`, stripped to fresh-scope + `Send` (no permission check, no routing); delete `ModuleDispatchRouteResolver`, `DispatchModeOptions`, `DispatchModeOptionsValidator`.
3. Restore `PackageReference MediatR` and `global using MediatR;` to `Sergin.SharedKernel.Presentation.Blazor` (undoing the `2026-08-22` fix-wave removal).
4. Rename `SerginSenderExtensions` → `SerginDispatcherExtensions`, retarget `SendListAsync` to `ISerginDispatcher`.
5. Add `RemoteForwardingHandler<TRequest,TResponse>` to `Sergin.SharedKernel.Infrastructure/Dispatching/`.
6. Add `ISerginRemoteModule` to `Sergin.SharedKernel.Modules/`.
7. Update `AddSerginCore`: new `remoteModules` parameter, duplicate-schema guard spans both collections, delete all `DispatchModeOptions`/`IDispatchRouteResolver`/`ISerginSender` registration, add the `remoteModules` loop calling `AddRemoteServices`.
8. Update `AddSerginBlazorKit()` to register `ISerginDispatcher`/`ScopedSerginDispatcher`.
9. Add `[assembly: InternalsVisibleTo("Sergin.MeterMinder.IntegrationTests.All")]` to `Sergin.SharedKernel.Application/Properties/AssemblyInfo.cs`.
10. Add `Add<Module>RemoteServices` and `DeviceManagementRemoteModule : ISerginRemoteModule` to `DeviceManagement.Presentation.Grpc` (the one module with a real remote-enabled feature today: `GetDeviceById`) — **not** to `DeviceManagementModule`, per §5. Add the project's new `ProjectReference` to `Sergin.SharedKernel.Infrastructure`.
11. Revert all 10 WebApi endpoint classes (`ISerginSender`→`ISender`, `.SendAsync(`→`.Send(`).
12. Revert all 6 Blazor page code-behinds (`ISerginSender Sender`→`ISerginDispatcher Dispatcher`) and their `_Imports.razor`/`GlobalUsings.cs` entries.
13. Update `CreateAndGetUserTests` to resolve `ISerginDispatcher`.
14. Rework `DeviceGrpcRoundTripTests` per §8 above.
15. Revert `add-feature` skill scaffolding (both copies) — WebApi template back to `ISender`, Blazor template back to `ISerginDispatcher`.
16. Update root `CLAUDE.md` and `SharedKernel` `CLAUDE.md` — remove `Sergin:Dispatch:Modules`/`IDispatchRouteResolver` documentation, document `ISerginRemoteModule`/`RemoteForwardingHandler`/composition-time Local-Remote split, correct the "presentation code injects `ISerginSender`, never `ISender`" convention bullet back to a Blazor-only statement with WebApi endpoints using `ISender` directly.
17. `dotnet build Sergin.MeterMinder.slnx` clean (and standalone `dotnet build Sergin.SharedKernel.slnx`), then `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/...`.
