# `ISerginSender` — Presentation-Agnostic Dispatch Contract

- **Date**: 2026-08-22
- **Status**: Approved (brainstorming dialogue, all sections signed off)
- **Goal**: Generalize the existing Blazor-only `ISerginUiDispatcher`/`RoutingSerginUiDispatcher` pair (introduced by `2026-08-21-dispatch-contract-design.md`) into a presentation-agnostic `ISerginSender`, consumed by **every** presentation adapter — Blazor pages and WebApi endpoints alike — with its contract in `Sergin.SharedKernel.Application` and its implementation in `Sergin.SharedKernel.Infrastructure`. Per-module Local/Remote dispatch mode (`Sergin:Dispatch:Modules`) is unchanged in shape and granularity.
- **Supersedes**: the Blazor-only placement described in `2026-08-21-dispatch-contract-design.md` §2–§4. That spec's decisions 1–8 (transport, per-feature routing, error mapping, list-query fallout) all still hold; this spec only relocates and widens the consumer surface.

## Problem

`ISerginUiDispatcher` today lives in `Sergin.SharedKernel.Presentation.Blazor` and is the only sanctioned way a Blazor page reaches Application. WebApi endpoints (10 of them, across both modules) and the gRPC server adapter (`DeviceGrpcService`) each inject `MediatR.ISender` directly instead — there is no permission pre-check, no Local/Remote branch, and no shared code path. If a WebApi host is ever reintroduced (it is deliberately kept buildable per the root `CLAUDE.md`), its endpoints have no way to participate in per-module Remote dispatch without duplicating `RoutingSerginUiDispatcher`'s logic under a new name.

Separately, the name and home of the type imply "UI-only" when the mechanism it provides — permission-gated, scope-correct, Local/Remote-routed access to Application — is a concern of every presentation adapter, not Blazor specifically.

## Decisions made during brainstorming

1. **Scope: all presentation adapters.** `ISerginSender` replaces `ISerginUiDispatcher` as the sole entry point into Application for both Blazor pages and WebApi endpoints. `DeviceGrpcService` (the gRPC **server-side** target of Remote dispatch) is explicitly excluded — see Non-goals.
2. **Dispatch granularity unchanged: per module, not per host.** `Sergin:Dispatch:Modules` keeps its existing schema-keyed shape; a single host can still run one module Local and another Remote. Nothing about this spec changes that axis.
3. **Layer placement: contract in Application, implementation in Infrastructure.** `ISerginSender`/`IDispatchRouteResolver` are interfaces in `Sergin.SharedKernel.Application`; `RoutingSerginSender`/`ModuleDispatchRouteResolver`/`DispatchModeOptions`/`DispatchModeOptionsValidator` all move into `Sergin.SharedKernel.Infrastructure`. This is a deliberate departure from the prior spec's placement (`Presentation.Blazor`), matching the standard Clean Architecture shape: a port declared where consumers can see it without depending on how it's fulfilled, an adapter declared where the framework/IO concerns it touches (DI scopes, gRPC channels, `IOptions`) already live.
4. **Single shared implementation, no per-host variant.** `RoutingSerginSender` keeps opening its own `IServiceScopeFactory` scope per call, even under WebApi (where the ASP.NET Core request pipeline already provides a correctly-scoped container). This costs one extra, immediately-disposed scope per WebApi call in exchange for one code path instead of two — see Approach C considered and rejected in the brainstorming dialogue.
5. **Registration moves to `AddSerginCore`.** Because both `Hosts.WebApi` and `Hosts.WebUi` call `AddSerginCore`, dispatch registration (`DispatchModeOptions` binding/validation, `IDispatchRouteResolver`, `ISerginSender`) happens exactly once, there. Neither `AddSerginWebApi` nor `AddSerginBlazorApp` carries any dispatch-specific code afterward. `Sergin:Dispatch:Modules` becomes a required config key for **any** Sergin host, not just the Blazor UI host.
6. **Two new `ProjectReference`s, confirmed non-circular:**
   - `Sergin.SharedKernel.Infrastructure` → `Sergin.SharedKernel.Application` (for `IUserContext`) and → `Sergin.SharedKernel.Presentation.Grpc` (for `IRemoteInvoker<,>`). The latter is the one deliberately-flagged oddity: a project named "Presentation.Grpc" becomes a dependency of Infrastructure. It holds only generated proto types and the `IRemoteInvoker<,>` client-side port — zero dependencies of its own — so the direction is safe even though the name reads backwards. Not renamed as part of this spec; flagged as a naming smell for a future pass, not fixed here (YAGNI — renaming ripples into every module's `.Presentation.Grpc` project for a cosmetic win).
   - `Sergin.SharedKernel.Presentation.WebApi` → `Sergin.SharedKernel.Application` (currently absent entirely — WebApi endpoints get `ISender` only via the bare MediatR package today). This is what makes `ISerginSender` visible to every module's `.Presentation.WebApi` project **transitively**, through the `SharedKernel.Presentation.WebApi` reference those projects already carry — no module-level `.csproj` edit needed.

## Non-goals

- **Does not change transport, routing granularity, error mapping, or list-query handling** — all of §1–§7 of the prior dispatch-contract spec stand unmodified.
- **Does not touch `DeviceGrpcService`.** It is the server-side target Remote dispatch calls into; it must stay on raw `ISender` by construction — routing it through `ISerginSender` risks a Remote→Remote loop if ever misconfigured, and it has no meaningful "Local vs Remote" question to ask (it *is* the Local side, from the target process's point of view).
- **Does not add authentication or implement identity-metadata propagation** — both remain open follow-ups from the prior spec, untouched here.
- **Does not rename `Sergin.SharedKernel.Presentation.Grpc`** despite Infrastructure now depending on it — flagged, not fixed (see decision 6).
- **Does not change the `[RequiredPermissions]` double-check behavior** — it still runs unconditionally in the sender (both Local and Remote, both Blazor and WebApi call sites), still redundant with `PermissionCheckPipelineBehavior` for Local, still deliberate.

## Architecture

| Piece | Home | Change |
|---|---|---|
| `ISerginSender` | **new**, `Sergin.SharedKernel.Application/Dispatching/` | Replaces `ISerginUiDispatcher` (same method signature, renamed) |
| `IDispatchRouteResolver` | moved to `Sergin.SharedKernel.Application/Dispatching/` | Moved from `Presentation.Blazor` |
| `RoutingSerginSender` | **new**, `Sergin.SharedKernel.Infrastructure/Dispatching/` | Replaces `RoutingSerginUiDispatcher` (moved from `Presentation.Blazor`, renamed) |
| `ModuleDispatchRouteResolver` | moved to `Sergin.SharedKernel.Infrastructure/Dispatching/` | Moved from `Hosts.WebUi`; closure over modules now built in `AddSerginCore` |
| `DispatchModeOptions`, `DispatchModeOptionsValidator` | moved to `Sergin.SharedKernel.Infrastructure/Dispatching/` | Moved from `Hosts` |
| `SerginSenderExtensions` (the `SendListAsync<TItem>` helper) | stays `Sergin.SharedKernel.Presentation.Blazor/Dispatching/`, renamed from `SerginUiDispatcherExtensions` | Blazor-only convenience — WebApi endpoints already build a closed `ListQuery<T>` themselves and can call `ISerginSender.SendAsync` directly, no helper needed |
| `ISerginUiDispatcher`, `RoutingSerginUiDispatcher` | **deleted** | Superseded by the above |
| `AddSerginCore` | `Sergin.SharedKernel.Hosts` | Gains: `AddOptions<DispatchModeOptions>().Bind(...).ValidateOnStart()`, `IDispatchRouteResolver` registration, `ISerginSender` registration (singleton) |
| `AddSerginBlazorApp` | `Sergin.SharedKernel.Hosts.WebUi` | Loses: all dispatch-specific registration (now inherited from `AddSerginCore`) |
| `AddSerginWebApi` | `Sergin.SharedKernel.Hosts.WebApi` | Gains: `ISerginSender` for free, same inheritance |

## 1. Contracts (`Sergin.SharedKernel.Application/Dispatching/`)

```csharp
public interface ISerginSender
{
    Task<ErrorOr<TResponse>> SendAsync<TResponse>(
        IRequest<ErrorOr<TResponse>> request, CancellationToken cancellationToken = default);
}

public interface IDispatchRouteResolver
{
    bool IsRemote(Type requestType);
}
```

Identical signatures to today's `ISerginUiDispatcher`/`IDispatchRouteResolver` — this is a rename plus a move, not a redesign of the contract itself.

## 2. Implementation (`Sergin.SharedKernel.Infrastructure/Dispatching/`)

`RoutingSerginSender` is `RoutingSerginUiDispatcher` moved verbatim (fresh scope per call, permission pre-check against `IUserContext`, Local via `ISender`/Remote via `IRemoteInvoker<,>`, same `ConcurrentDictionary` invoker-type cache). No logic changes.

`ModuleDispatchRouteResolver` moves verbatim, with one wiring change: it was constructed with a closure over `modules.ToDictionary(module => module.ApplicationAssembly, module => module.Schema)` inside `AddSerginBlazorApp`; that closure now builds inside `AddSerginCore`, since `AddSerginCore` already receives the full `modules` collection for its `module.AddServices(...)` loop.

`DispatchModeOptions`/`DispatchModeOptionsValidator` move verbatim — same shape, same `.Bind(...).ValidateOnStart()` pattern, same "name the missing schema" failure message.

## 3. Registration (`AddSerginCore`, `Sergin.SharedKernel.Hosts`)

```csharp
public static TBuilder AddSerginCore<TBuilder>(
    this TBuilder builder, IReadOnlyCollection<ISerginModule> modules)
    where TBuilder : IHostApplicationBuilder
{
    // ... existing MediatR scan, pipeline behaviors, IDbConnectionFactory, etc. (unchanged) ...

    builder.Services
        .AddOptions<DispatchModeOptions>()
        .Bind(builder.Configuration.GetSection("Dispatch"))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<DispatchModeOptions>, DispatchModeOptionsValidator>();

    IReadOnlyDictionary<Assembly, string> schemaByAssembly =
        modules.ToDictionary(module => module.ApplicationAssembly, module => module.Schema);
    builder.Services.AddSingleton<IDispatchRouteResolver>(
        new ModuleDispatchRouteResolver(schemaByAssembly, /* IOptions<DispatchModeOptions> resolved lazily */));
    builder.Services.AddSingleton<ISerginSender, RoutingSerginSender>();

    // ... existing module.AddServices(...) loop (unchanged) ...
}
```

`AddSerginBlazorApp` and `AddSerginWebApi` both call `AddSerginCore(modules)` already (per the existing "every host must register its own `IUserContextFactory` *before* calling `AddSerginCore`" rule) — no change to call order, just less work left for each to do afterward. `AddSerginBlazorKit()` stops registering `ISerginUiDispatcher`.

**Consequence for any future WebApi host**: `Sergin:Dispatch:Modules` becomes a required startup key, same as it already is for the Blazor UI host. A WebApi host that omits it fails startup the same way, naming the missing schema.

## 4. Consumer call sites

**Blazor pages** (`DeviceListPage.razor.cs`, `DeviceDetailPage.razor.cs`, `CreateDevicePage.razor.cs`, `UserListPage.razor.cs`, `UserDetailPage.razor.cs`, `CreateUserPage.razor.cs`): `[Inject] private ISerginUiDispatcher Dispatcher` → `[Inject] private ISerginSender Sender`. Pure rename; `Dispatcher.SendAsync(...)`/`Dispatcher.SendListAsync(...)` call sites become `Sender.SendAsync(...)`/`Sender.SendListAsync(...)`.

**WebApi endpoints** (10 across both modules — `CreateDeviceEndpoint`, `GetDeviceEndpoint`, `GetDeviceListEndpoint`, `CreateManufacturerEndpoint`, `GetManufacturerEndpoint`, `GetManufacturerListEndpoint`, `CreateUserEndpoint`, `GetUserEndpoint`, `GetUserListEndpoint`, `DeactivateUserEndpoint`): constructor/delegate parameter `ISender sender` → `ISerginSender sender`, call sites `sender.Send(...)` → `sender.SendAsync(...)`.

**`add-feature` skill** (`.claude/skills/add-feature/SKILL.md` and the UserAccess-repo copy): scaffolding templates for new WebApi endpoints must inject `ISerginSender`, not `ISender`, going forward. Flagged here as a required follow-up edit; not performed by this spec (implementation plan will cover it).

**`DeviceGrpcService`**: unchanged — keeps `ISender sender`, per Non-goals.

## 5. Error handling

No change from the prior spec: `ISerginSender.SendAsync` returns `ErrorOr<TResponse>` exactly as `ISerginUiDispatcher` did; the permission pre-check short-circuits to `Error.Forbidden()` before either branch runs; WebApi endpoints continue mapping the result via `.ToApiResult()` exactly as before, just from `ISerginSender` instead of `ISender` directly. Since `RoutingSerginSender`'s Local branch still ends in `ISender.Send(...)` inside its own scope, `PermissionCheckPipelineBehavior`/`ValidationPipelineBehavior` still run identically for WebApi as they did when endpoints called `ISender` directly — WebApi gains the sender's pre-check as new, deliberate, cheap redundancy (decision 4 in the prior spec's §5, now extended to WebApi).

## 6. Testing

- `CreateAndGetUserTests` currently resolves `ISerginUiDispatcher` from `factory.Services` — becomes `ISerginSender`. No behavioral change; the fresh-DI-scope-per-send property (the reason the test can round-trip through Postgres instead of the writing `DbContext`'s change tracker) is unchanged, since `RoutingSerginSender` keeps that exact behavior.
- `ModulePageRenderingTests` — unaffected, doesn't touch dispatch.
- `DeviceGrpcRoundTripTests` — unaffected; `DeviceGrpcService` is explicitly out of scope (Non-goals).
- No new test infrastructure required by this spec — it moves and renames existing, already-tested behavior. If a future WebApi host is reintroduced, its own test suite (a new project per the root `CLAUDE.md`'s "second host needs a separate test project" rule) should include at minimum one test asserting a WebApi endpoint's permission pre-check short-circuits the same way the existing Blazor test path can be checked to.

## Migration checklist (for the implementation plan)

1. Move/rename `ISerginUiDispatcher` → `ISerginSender`, `IDispatchRouteResolver` into `Sergin.SharedKernel.Application/Dispatching/`.
2. Move/rename `RoutingSerginUiDispatcher` → `RoutingSerginSender`, `ModuleDispatchRouteResolver`, `DispatchModeOptions`, `DispatchModeOptionsValidator` into `Sergin.SharedKernel.Infrastructure/Dispatching/`.
3. Update `.csproj`s: `Infrastructure` gains references to `Application` and `Presentation.Grpc` (+ `PackageReference MediatR`); `Presentation.WebApi` gains a reference to `Application`.
4. Update `AddSerginCore` (registration), `AddSerginBlazorApp`/`AddSerginBlazorKit` (remove now-redundant registration), `AddSerginWebApi` (no change needed beyond inheriting `AddSerginCore`).
5. Rename `SerginUiDispatcherExtensions` → `SerginSenderExtensions`, update its `ISerginUiDispatcher` parameter type to `ISerginSender`; stays in `Presentation.Blazor`.
6. Update all 6 Blazor page code-behinds (rename injected type + call sites).
7. Update all 10 WebApi endpoint classes (swap `ISender` → `ISerginSender`, `.Send(` → `.SendAsync(`).
8. Update `CreateAndGetUserTests` to resolve `ISerginSender`.
9. Update `add-feature` skill scaffolding templates (both copies) to inject `ISerginSender` for new endpoints.
10. Update root `CLAUDE.md` and `SharedKernel` `CLAUDE.md` sections describing `ISerginUiDispatcher`/dispatch wiring to reflect the new name/location (both files currently document the old shape in detail).
11. `dotnet build Sergin.MeterMinder.slnx` clean, then `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/...`.
