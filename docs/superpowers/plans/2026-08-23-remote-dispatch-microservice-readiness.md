# Remote Dispatch via MediatR Pipeline — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `ISerginSender` back to `Sergin.SharedKernel.Presentation.Blazor` as `ISerginDispatcher` (Blazor-only, fresh-scope-per-call, no permission pre-check, no Local/Remote branch); replace the deleted runtime branch with a composition-time split — a host hands `AddSerginCore` a `localModules` collection (today's `ISerginModule`, unchanged) and a `remoteModules` collection (new `ISerginRemoteModule`, registering `RemoteForwardingHandler<TRequest,TResponse>` — a real `IRequestHandler` wrapping the existing `IRemoteInvoker<TRequest,TResponse>` — as a normal MediatR handler). Local and Remote calls both flow through `ISender.Send`, so `PermissionCheckPipelineBehavior`/`ValidationPipelineBehavior` cover both uniformly for the first time.

**Architecture:** Three repos, same three branches already in flight (host `worktree-sergin-sender`, SharedKernel submodule `sergin-sender`, UserAccess submodule `sergin-sender`), continuing forward with new commits — nothing pushed yet, so amending in place is cheap. SharedKernel changes first (the dispatch contract lives there), then UserAccess (a pure consumer revert, no remote-enabled feature of its own), then the host (DeviceManagement consumer revert plus the one real `ISerginRemoteModule` implementation, since `GetDeviceById` is the platform's only existing remote-capable slice).

**Tech Stack:** .NET 10, MediatR, ErrorOr, MudBlazor (Blazor Server), gRPC (`Grpc.AspNetCore`/`Grpc.Net.Client`), xUnit + Testcontainers.PostgreSql.

**Spec:** `docs/superpowers/specs/2026-08-23-remote-dispatch-microservice-readiness-design.md` (supersedes `2026-08-22-sergin-sender-design.md`).

## Global Constraints

- Every `dotnet build`/`dotnet test` invocation needs `-p:NuGetAudit=false` — this sandbox has no network access to nuget.org.
- `Directory.Build.props`: `TreatWarningsAsErrors=true`, `AnalysisMode=All`, SonarAnalyzer.CSharp enabled — any analyzer warning, style violation, or nullable warning fails the build. Write code that passes analysis cleanly the first time.
- Central Package Management is on (`Directory.Packages.props` per repo): `PackageReference` items carry no `Version` attribute; a new package needs a version-less `PackageReference` plus an alphabetically-placed `<PackageVersion>` entry.
- **Do not push any branch to any remote.** All three branches stay local until the user explicitly says otherwise — this reverses part of the just-finished (also unpushed) `sergin-sender` work, and the user wants to review diffs themselves before anything goes to GitHub.
- `InternalsVisibleTo` grants use the exact consumer assembly name string as a plain string literal, matching the existing grants already present in each `AssemblyInfo.cs`.
- Match existing repo comment conventions: `.Presentation.Grpc` types that no production host wires up yet are `public`, not `internal`, with a doc comment explaining why (see `DeviceGrpcService`, `GetDeviceByIdGrpcInvoker`) — `DeviceManagementRemoteModule` follows the same pattern.
- Existing typos (`DeviceIntenralId`, `IManufacturerAllQueryRepositoriy`, `Infrastracture`) are real type/project names — never "fix" them as a side effect of an unrelated edit.

---

## Phase A — SharedKernel (submodule at `src/SharedKernel`, branch `sergin-sender`, continuing from `75bf50b`)

### Task 1: Move dispatch contract + implementation back to Presentation.Blazor

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/ISerginDispatcher.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/ScopedSerginDispatcher.cs`
- Delete: `src/SharedKernel/Sergin.SharedKernel.Application/Dispatching/ISerginSender.cs`
- Delete: `src/SharedKernel/Sergin.SharedKernel.Application/Dispatching/IDispatchRouteResolver.cs`
- Delete: `src/SharedKernel/Sergin.SharedKernel.Infrastructure/Dispatching/RoutingSerginSender.cs`
- Delete: `src/SharedKernel/Sergin.SharedKernel.Infrastructure/Dispatching/ModuleDispatchRouteResolver.cs`
- Delete: `src/SharedKernel/Sergin.SharedKernel.Infrastructure/Dispatching/DispatchModeOptions.cs`
- Delete: `src/SharedKernel/Sergin.SharedKernel.Infrastructure/Dispatching/DispatchModeOptionsValidator.cs`
- Rename+modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/SerginSenderExtensions.cs` → `SerginDispatcherExtensions.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/SerginBlazorKitExtensions.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Sergin.SharedKernel.Presentation.Blazor.csproj`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/GlobalUsings.cs`

**Interfaces:**
- Produces: `ISerginDispatcher.SendAsync<TResponse>(IRequest<ErrorOr<TResponse>>, CancellationToken = default) : Task<ErrorOr<TResponse>>` — same shape `ISerginSender` had, in `Sergin.SharedKernel.Presentation.Blazor.Dispatching`.
- Consumes: `MediatR.ISender`, `Microsoft.Extensions.DependencyInjection.IServiceScopeFactory` (both already available in this project's dependency graph via its existing `Application`/framework references, once the `MediatR` package reference is restored in this step).

- [ ] **Step 1: Create `ISerginDispatcher.cs`**

```csharp
namespace Sergin.SharedKernel.Presentation.Blazor.Dispatching;

public interface ISerginDispatcher
{
    Task<ErrorOr<TResponse>> SendAsync<TResponse>(
        IRequest<ErrorOr<TResponse>> request, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Create `ScopedSerginDispatcher.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Sergin.SharedKernel.Presentation.Blazor.Dispatching;

/// <summary>
/// Opens one fresh DI scope per send. In Blazor Server, "scoped" is the whole SignalR circuit's
/// lifetime (as long as the user's tab stays open), not a single operation's — resolving ISender
/// straight off the circuit's container would share one DbContext across every interaction, producing
/// an unbounded change tracker and "a second operation was started on this context" the moment two
/// components render concurrently. No permission pre-check and no Local/Remote branch here anymore:
/// both are now the MediatR pipeline's job (PermissionCheckPipelineBehavior covers every Send call,
/// Local or Remote, since a Remote request now resolves a real IRequestHandler too — see
/// RemoteForwardingHandler in Sergin.SharedKernel.Infrastructure).
/// </summary>
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

- [ ] **Step 3: Delete the six files listed above** (`ISerginSender.cs`, `IDispatchRouteResolver.cs`, `RoutingSerginSender.cs`, `ModuleDispatchRouteResolver.cs`, `DispatchModeOptions.cs`, `DispatchModeOptionsValidator.cs`).

- [ ] **Step 4: Rename `SerginSenderExtensions.cs` → `SerginDispatcherExtensions.cs`, retarget to `ISerginDispatcher`**

```csharp
namespace Sergin.SharedKernel.Presentation.Blazor.Dispatching;

public static class SerginDispatcherExtensions
{
    /// <summary>
    /// List queries have no dedicated command type — handlers implement IListQueryHandler&lt;TItem&gt;
    /// against the shared generic ListQuery&lt;TItem&gt;. This is the UI-side equivalent of
    /// ListQueryRequestModel.ToListQuery&lt;TItem&gt;(), without the [FromQuery] binding attributes.
    /// pageIndex is 1-based, matching PageIndex.Default; MudBlazor's TableState.Page is 0-based.
    /// </summary>
    public static Task<ErrorOr<ListQueryResponse<TItem>>> SendListAsync<TItem>(
        this ISerginDispatcher dispatcher, int pageSize, int pageIndex, CancellationToken cancellationToken = default)
        where TItem : notnull
        => dispatcher.SendAsync(ListQueryFactory.Create<TItem>(pageSize, pageIndex), cancellationToken);
}
```

(Only the parameter type/name changed — `this ISerginSender sender` → `this ISerginDispatcher dispatcher` — and the internal `sender.SendAsync(...)` call becomes `dispatcher.SendAsync(...)`.)

- [ ] **Step 5: Register the dispatcher in `SerginBlazorKitExtensions.cs`**

Add one line to `AddSerginBlazorKit`:
```csharp
services.AddSingleton<ISerginDispatcher, ScopedSerginDispatcher>();
```
placed after the existing `services.AddScoped<IUiThemeStore, LocalStorageThemeStore>();` line.

- [ ] **Step 6: Restore the `MediatR` package reference**

In `Sergin.SharedKernel.Presentation.Blazor.csproj`, add to the existing `<PackageReference>` `<ItemGroup>` (alongside `MudBlazor`/`MudBlazor.ThemeManager`):
```xml
<PackageReference Include="MediatR" />
```
`MediatR` already has a `<PackageVersion>` entry in `Directory.Packages.props` (used by `Application`/`Infrastructure`/other projects) — no new version entry needed, verify it's present rather than adding a duplicate.

- [ ] **Step 7: Restore `global using MediatR;` in `GlobalUsings.cs`**

Current content is:
```csharp
global using ErrorOr;
global using Sergin.SharedKernel.Application;
global using Sergin.SharedKernel.Application.Commands.Queries;
```
Add `global using MediatR;` as a new line (alphabetical placement: before `Sergin.SharedKernel.Application`).

- [ ] **Step 8: Build to verify**

```bash
dotnet build Sergin.SharedKernel.slnx -p:NuGetAudit=false
```
Expect compile errors in every consumer of the old `ISerginSender`/`RoutingSerginSender`/`IDispatchRouteResolver` types — that's expected; those consumers get fixed in later tasks of this phase. This step just confirms the new/moved files themselves compile with no analyzer warnings in isolation (build the `Sergin.SharedKernel.Presentation.Blazor` project specifically if the full solution has too much red to read past):
```bash
dotnet build src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Sergin.SharedKernel.Presentation.Blazor.csproj -p:NuGetAudit=false
```

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "Move ISerginDispatcher back to Presentation.Blazor, delete Local/Remote routing"
```

---

### Task 2: Add `RemoteForwardingHandler<TRequest,TResponse>`

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure/Dispatching/RemoteForwardingHandler.cs`

**Interfaces:**
- Consumes: `IRemoteInvoker<TRequest,TResponse>` (`Sergin.SharedKernel.Presentation.Grpc.Dispatching`, already referenced by this project).
- Produces: `RemoteForwardingHandler<TRequest,TResponse> : IRequestHandler<TRequest, ErrorOr<TResponse>>` — a real MediatR handler a remote module registers explicitly per feature.

- [ ] **Step 1: Create the file**

```csharp
using MediatR;
using Sergin.SharedKernel.Presentation.Grpc.Dispatching;

namespace Sergin.SharedKernel.Infrastructure.Dispatching;

/// <summary>
/// Wraps an IRemoteInvoker as a real MediatR handler, so a Remote-configured module's requests flow
/// through the same ISender.Send pipeline a Local module's real handler would — PermissionCheckPipelineBehavior
/// and ValidationPipelineBehavior now cover Remote calls too, not just Local ones. Pure forwarding, no
/// logic of its own: the one place to add shared remote-call behavior later (retry, tracing) without
/// touching every feature. Registered explicitly per feature by a module's AddRemoteServices — never
/// discovered by MediatR's assembly scan, since it's generic and lives in a different assembly than any
/// module's ContractsAssembly.
/// </summary>
internal sealed class RemoteForwardingHandler<TRequest, TResponse>(IRemoteInvoker<TRequest, TResponse> invoker)
    : IRequestHandler<TRequest, ErrorOr<TResponse>>
    where TRequest : IRequest<ErrorOr<TResponse>>
{
    public Task<ErrorOr<TResponse>> Handle(TRequest request, CancellationToken cancellationToken)
        => invoker.InvokeAsync(request, cancellationToken);
}
```

No `.csproj` changes — `Sergin.SharedKernel.Infrastructure` already references `Sergin.SharedKernel.Presentation.Grpc` (for `IRemoteInvoker<,>`) and the `MediatR` package (both added by the prior, superseded spec and still needed here for a different reason).

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/SharedKernel/Sergin.SharedKernel.Infrastructure/Sergin.SharedKernel.Infrastructure.csproj -p:NuGetAudit=false
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "Add RemoteForwardingHandler bridging IRemoteInvoker into the MediatR pipeline"
```

---

### Task 3: Add `ISerginRemoteModule`

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Modules/ISerginRemoteModule.cs`

**Interfaces:**
- Produces: `ISerginRemoteModule` — `Schema`, `ContractsAssembly`, `AddRemoteServices(IServiceCollection, IConfigurationSection)`. Consumed by `AddSerginCore` (Task 4) and implemented by `DeviceManagementRemoteModule` (Task 11, host repo).

- [ ] **Step 1: Create the file**

```csharp
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sergin.SharedKernel.Modules;

/// <summary>
/// A module a host calls Remote — no real handlers, no DbContext, nothing to migrate. Deliberately not
/// ISerginModule: that contract's ApplicationAssembly/MigrateAsync assume the module runs locally. The
/// type implementing this must not be the module's composition root (ISerginModule implementer) if that
/// root transitively references the module's .Application/.Infrastructure — doing so would force a
/// gateway host to pull in everything just to reach this capability, defeating the point. See
/// DeviceManagementRemoteModule (host repo) for the reference shape: a small class living inside the
/// module's own .Presentation.Grpc project, which is already isolated from .Application/.Infrastructure.
/// </summary>
public interface ISerginRemoteModule
{
    string Schema { get; }

    Assembly ContractsAssembly { get; }

    void AddRemoteServices(IServiceCollection services, IConfigurationSection configuration);
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/SharedKernel/Sergin.SharedKernel.Modules/Sergin.SharedKernel.Modules.csproj -p:NuGetAudit=false
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "Add ISerginRemoteModule contract"
```

---

### Task 4: Update `AddSerginCore` for composition-time Local/Remote

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs`

**Interfaces:**
- Consumes: `ISerginRemoteModule` (Task 3).
- Produces: `AddSerginCore<TBuilder>(this TBuilder, IReadOnlyCollection<ISerginModule> localModules, IReadOnlyCollection<ISerginRemoteModule>? remoteModules = null) : IConfigurationSection`. Existing call sites (`AddSerginBlazorApp`, `AddSerginWebApi`) call this positionally with one argument today — both keep compiling unchanged, since `remoteModules` defaults to `null`/empty and the renamed first parameter is still positional.

- [ ] **Step 1: Rewrite the method**

Replace the full body of `AddSerginCore` in `SerginCoreExtensions.cs` with:

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

    if (duplicateSchemas.Length > 0)
    {
        throw new InvalidOperationException(
            $"Duplicate module schema(s) registered: {string.Join(", ", duplicateSchemas)}. Each schema must "
            + "appear exactly once across localModules and remoteModules combined — a module cannot be both "
            + "Local and Remote in the same host, and two classes for the same schema runs AddServices twice.");
    }

    builder.Services.AddMediatR(options =>
    {
        foreach (ISerginModule module in localModules)
        {
            options.RegisterServicesFromAssembly(module.ApplicationAssembly);
        }

        options.AddOpenBehavior(typeof(PermissionCheckPipelineBehavior<,>));
        options.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
    });

    builder.Services.AddScoped<IEventDispatcher, DefaultEventDispatcher>();
    builder.Services.AddScoped<EventDispatcherInterceptor>();

    string connectionString = serginSection.GetConnectionString("Database")
        ?? throw new InvalidOperationException("Connection string 'Sergin:ConnectionStrings:Database' is not configured.");

    builder.Services.AddScoped<IDbConnectionFactory>(p => new PostgresDbConnectionFactory(connectionString));

    builder.Services.AddScoped(p => p.GetRequiredService<IUserContextFactory>().CreateUserContext());

    builder.Services.AddSingleton<ILocalizer, DefaultLocalizer>();

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

This removes: the `schemas` list built solely for `DispatchModeOptionsValidator`, the `AddOptions<DispatchModeOptions>()...` block, the `AddSingleton<IValidateOptions<DispatchModeOptions>>` line, the `schemaByAssembly` dictionary, and the `AddSingleton<IDispatchRouteResolver>(...)`/`AddSingleton<ISerginSender, RoutingSerginSender>()` lines. Adds: the `remoteModules` parameter and its default, the widened duplicate-schema check, and the `remoteModules` loop.

- [ ] **Step 2: Remove now-unused usings**

`SerginCoreExtensions.cs` currently imports `Microsoft.Extensions.Options` (for `IValidateOptions<T>`/`IOptions<T>`) and `Sergin.SharedKernel.Infrastructure.Dispatching` (for the deleted `DispatchModeOptions`/`ModuleDispatchRouteResolver`/`RoutingSerginSender`) and `Sergin.SharedKernel.Application.Dispatching` (for the deleted `ISerginSender`/`IDispatchRouteResolver`) — remove all three `using` lines; add `using Sergin.SharedKernel.Modules;` if not already present (needed for the new `ISerginRemoteModule` parameter type — `ISerginModule` already implies this namespace is imported, so likely a no-op check).

- [ ] **Step 3: Build to verify**

```bash
dotnet build src/SharedKernel/Sergin.SharedKernel.Hosts/Sergin.SharedKernel.Hosts.csproj -p:NuGetAudit=false
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "AddSerginCore: composition-time localModules/remoteModules split"
```

---

### Task 5: `InternalsVisibleTo` grant for the outer integration test project

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Properties/AssemblyInfo.cs`

**Interfaces:** none (assembly-level attribute only).

**Why:** `DeviceGrpcRoundTripTests`' forbidden-path test used to exercise `RoutingSerginSender`'s own client-side permission check. That check is deleted (Task 1) — permission enforcement now happens solely inside `PermissionCheckPipelineBehavior`, which is `internal` to this assembly. Without this grant, the outer test project (Task 12, host repo) cannot call `options.AddOpenBehavior(typeof(PermissionCheckPipelineBehavior<,>))` in its own bespoke `AddMediatR(...)` setup — `typeof(PermissionCheckPipelineBehavior<,>)` would be a compile error (CS0122) from outside the assembly.

- [ ] **Step 1: Add the grant**

Current content:
```csharp
[assembly: ComVisible(false)]
```
(no `InternalsVisibleTo` lines exist in this file today). Add, matching the exact string format already used in `Sergin.SharedKernel.Infrastructure`'s own `AssemblyInfo.cs`:
```csharp
[assembly: InternalsVisibleTo("Sergin.MeterMinder.IntegrationTests.All")]
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/SharedKernel/Sergin.SharedKernel.Application/Sergin.SharedKernel.Application.csproj -p:NuGetAudit=false
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "Grant IntegrationTests.All visibility into Application's internal pipeline behaviors"
```

---

### Task 6: Update SharedKernel's own `CLAUDE.md`/`README.md`; standalone build verification

**Files:**
- Modify: `src/SharedKernel/.claude/CLAUDE.md`
- Modify: `src/SharedKernel/README.md`

**Steps:**

- [ ] **Step 1: Update `.claude/CLAUDE.md`**

Find and rewrite every passage describing `ISerginSender`/`RoutingSerginSender`/`IDispatchRouteResolver`/`Sergin:Dispatch:Modules` (there are several — the `Presentation.Blazor` project bullet, the `Presentation.WebApi` project bullet, the `Hosts`/`Hosts.WebUi` bullets, and the "Cross-cutting conventions" bullet on injecting `ISerginSender`). Replace with:
  - `ISerginDispatcher`/`ScopedSerginDispatcher` live in `Sergin.SharedKernel.Presentation.Blazor/Dispatching/` — Blazor-only, not presentation-agnostic. State plainly that this reverses the `2026-08-22` move.
  - `RemoteForwardingHandler<TRequest,TResponse>` lives in `Sergin.SharedKernel.Infrastructure/Dispatching/` — describe it as the bridge that makes a Remote module's calls flow through the same MediatR pipeline as Local.
  - `ISerginRemoteModule` lives in `Sergin.SharedKernel.Modules/` alongside `ISerginModule`/`ISerginWebUiModule`/`ISerginWebApiModule`.
  - `AddSerginCore` now takes `localModules` + optional `remoteModules`; `Sergin:Dispatch:Modules` config, `IDispatchRouteResolver`, `DispatchModeOptions` no longer exist — remove every reference to them.
  - The "Presentation code injects `ISerginSender`, never `ISender`" convention bullet reverts to Blazor-only: Blazor pages inject `ISerginDispatcher`; WebApi endpoints inject `ISender` directly (no wrapper — their DI scope already matches one request, and the pipeline now covers permission checking without a redundant manual copy).
  - `Sergin.SharedKernel.Presentation.WebApi`'s bullet: remove the claim that it exists "so an endpoint can inject `ISerginSender`" — it still references `Sergin.SharedKernel.Application` (needed independently by `ListQueryRequestModel.cs` for `ListQuery<T>`/`IListQuery<T>`), just not for dispatch anymore.

- [ ] **Step 2: Update `README.md`**

Fix the one line describing `AddSerginCore`/dispatch (the line the `2026-08-22` fix wave already touched once) to match the reverted shape — no more "presentation-agnostic `ISerginSender`" framing.

- [ ] **Step 3: Full standalone build**

```bash
dotnet build Sergin.SharedKernel.slnx -p:NuGetAudit=false
```
Must be 0 warnings/0 errors — this is the SharedKernel repo's own solution, standalone-buildable per its own `CLAUDE.md`. Every consumer inside this repo (there are none outside `Presentation.Blazor`/`Infrastructure`/`Hosts`/`Modules` themselves, since UserAccess is a separate submodule not part of this `.slnx`) should now compile clean.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Update SharedKernel CLAUDE.md/README for the reverted dispatch shape"
```

---

## Phase B — UserAccess (submodule at `src/Modules/UserAccess`, branch `sergin-sender`, continuing from `3616c7f`)

Requires Phase A's SharedKernel commits to be visible — this worktree already has both submodules checked out side by side at the paths the build expects, so no pointer bump is needed mid-phase; the host-level pointer bump happens once, in Task 9.

### Task 7: Revert UserAccess Blazor pages + WebApi endpoints

**Files:**
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.Blazor/Users/Pages/UserListPage.razor.cs`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.Blazor/Users/Pages/UserDetailPage.razor.cs`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.Blazor/Users/Pages/CreateUserPage.razor.cs`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.Blazor/_Imports.razor`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.Blazor/GlobalUsings.cs`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.WebApi/Users/Endpoints/Create/CreateUserEndpoint.cs`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.WebApi/Users/Endpoints/GetOne/GetUserEndpoint.cs`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.WebApi/Users/Endpoints/GetList/GetUserListEndpoint.cs`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.WebApi/Users/Endpoints/DeactivateUser/DeactivateUserEndpoint.cs`

**Steps:**

- [ ] **Step 1: Revert the three Blazor page code-behinds**

In each of `UserListPage.razor.cs`, `UserDetailPage.razor.cs`, `CreateUserPage.razor.cs`: `using Sergin.SharedKernel.Application.Dispatching;` → delete (the namespace `Sergin.SharedKernel.Presentation.Blazor.Dispatching`, where `ISerginDispatcher` now lives, is already global via `_Imports.razor`/`GlobalUsings.cs` for the `.razor` markup, and code-behind files need it explicitly — check each file; `CreateUserPage.razor.cs` currently has only the `Application.Dispatching` using and needs it replaced with `Sergin.SharedKernel.Presentation.Blazor.Dispatching`, not simply deleted). Concretely, in `CreateUserPage.razor.cs`:

```csharp
using Sergin.SharedKernel.Application.Dispatching;
```
becomes
```csharp
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
```
and
```csharp
[Inject]
private ISerginSender Sender { get; set; } = default!;
```
becomes
```csharp
[Inject]
private ISerginDispatcher Dispatcher { get; set; } = default!;
```
with the body's `await Sender.SendAsync(...)` → `await Dispatcher.SendAsync(...)`. Apply the same three substitutions (`using` namespace, injected property name+type, call-site variable name) to `UserListPage.razor.cs` (uses `SendListAsync`) and `UserDetailPage.razor.cs` (uses `SendAsync`) — read each file first to match its exact current variable name before editing.

- [ ] **Step 2: Fix `_Imports.razor`**

Remove the line `@using Sergin.SharedKernel.Application.Dispatching` (line 7 today) — `@using Sergin.SharedKernel.Presentation.Blazor.Dispatching` (line 8) already covers the namespace `ISerginDispatcher` now lives in, and is unchanged.

- [ ] **Step 3: Fix `GlobalUsings.cs`**

Current content:
```csharp
global using ErrorOr;
global using MediatR;
global using Sergin.SharedKernel.Application;
global using Sergin.SharedKernel.Application.Dispatching;
```
Replace the last line with:
```csharp
global using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
```

- [ ] **Step 4: Revert the four WebApi endpoints**

In each of `CreateUserEndpoint.cs`, `GetUserEndpoint.cs`, `GetUserListEndpoint.cs`, `DeactivateUserEndpoint.cs`: delete the `using Sergin.SharedKernel.Application.Dispatching;` line (MediatR's `ISender` is already globally imported per this project's own `GlobalUsings.cs`, matching the "`.Presentation.WebApi` projects import `ErrorOr`, `MediatR`, `Sergin.SharedKernel.Presentation*`" convention), change the injected parameter type `ISerginSender sender` → `ISender sender`, and the call `await sender.SendAsync(...)` → `await sender.Send(...)`. Concretely, `CreateUserEndpoint.cs`'s handler delegate:
```csharp
.MapPost("/users", async ([FromBody] NewUserModel user, ISerginSender sender) =>
{
    ErrorOr<CreateUserCommandResponse> res = await sender.SendAsync(
        new CreateUserCommand(
            new UserName(user.UserName)));

    return res.ToApiResult();
})
```
becomes
```csharp
.MapPost("/users", async ([FromBody] NewUserModel user, ISender sender) =>
{
    ErrorOr<CreateUserCommandResponse> res = await sender.Send(
        new CreateUserCommand(
            new UserName(user.UserName)));

    return res.ToApiResult();
})
```
Apply the equivalent `ISerginSender`→`ISender`, `.SendAsync(`→`.Send(` substitution to the other three endpoint files (read each first — parameter names/shapes differ per endpoint).

- [ ] **Step 5: Build to verify**

From the host worktree root (this submodule only compiles mounted inside a host):
```bash
dotnet build Sergin.MeterMinder.slnx -p:NuGetAudit=false
```
Expect remaining errors only in DeviceManagement/host-repo files not yet touched (Phase C) — UserAccess itself should be clean.

- [ ] **Step 6: Commit** (inside `src/Modules/UserAccess`)

```bash
git add -A
git commit -m "Revert UserAccess presentation layers to ISerginDispatcher/ISender"
```

---

### Task 8: Update UserAccess `add-feature` skill copy + `CLAUDE.md`

**Files:**
- Modify: `src/Modules/UserAccess/.claude/skills/add-feature/SKILL.md` (if this copy exists in the UserAccess repo — verify path first; the host repo's own copy is handled in Task 13)
- Modify: `src/Modules/UserAccess/.claude/CLAUDE.md`

**Steps:**

- [ ] **Step 1: Locate and update the skill copy**

```bash
find src/Modules/UserAccess -iname 'SKILL.md' -path '*add-feature*'
```
If found, revert its WebApi endpoint scaffolding template's injected type from `ISerginSender` back to `ISender` (matching Task 7's shape), and its Blazor page scaffolding template's injected type from `ISerginSender` back to `ISerginDispatcher`.

- [ ] **Step 2: Update `src/Modules/UserAccess/.claude/CLAUDE.md`**

The "Blazor UI" section's bullet "Inject `ISerginSender`, never MediatR's `ISender`/`IMediator` directly... this module's WebApi endpoints inject it too" is now wrong on two counts: (a) the type is `ISerginDispatcher`, not `ISerginSender`; (b) WebApi endpoints no longer inject it at all — they use `ISender` directly. Rewrite that bullet to say Blazor pages inject `ISerginDispatcher` (`Sergin.SharedKernel.Presentation.Blazor.Dispatching`) for the fresh-DI-scope-per-send reason (keep that explanation, it's still accurate), and that WebApi endpoints inject `ISender` directly since their request-scoped DI container already gives them the same isolation for free.

- [ ] **Step 3: Commit** (inside `src/Modules/UserAccess`)

```bash
git add -A
git commit -m "Update UserAccess add-feature skill + CLAUDE.md for ISerginDispatcher revert"
```

---

## Phase C — Host repo (worktree root, branch `worktree-sergin-sender`, continuing from `75bf50b`)

### Task 9: Bump submodule pointers to Phase A/B commits

**Files:**
- Modify (pointer only): `src/SharedKernel`
- Modify (pointer only): `src/Modules/UserAccess`

**Steps:**

- [ ] **Step 1: Confirm both submodules' working trees are clean and on their `sergin-sender` branches** at the final commits from Tasks 6 and 8 respectively (`git -C src/SharedKernel status --short` / `git -C src/Modules/UserAccess status --short`, both expected empty).

- [ ] **Step 2: Stage and commit the pointer bump** from the host worktree root:
```bash
git add src/SharedKernel src/Modules/UserAccess
git commit -m "Bump SharedKernel and UserAccess submodule pointers to sergin-dispatcher revert"
```
(Pure submodule pointer drift within this same effort — commit directly, no separate review needed, matching this session's established convention for pointer-only bumps.)

---

### Task 10: Revert DeviceManagement WebApi endpoints + Blazor pages

**Files:**
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Devices/Endpoints/Create/CreateDeviceEndpoint.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Devices/Endpoints/GetOne/GetDeviceEndpoint.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Devices/Endpoints/GetList/GetDeviceListEndpoint.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Manufacturers/Endpoints/Create/CreateManufacturerEndpoint.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Manufacturers/Endpoints/GetOne/GetManufacturerEndpoint.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Manufacturers/Endpoints/GetList/GetManufacturerListEndpoint.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Devices/Pages/DeviceListPage.razor.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Devices/Pages/DeviceDetailPage.razor.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Devices/Pages/CreateDevicePage.razor.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/_Imports.razor`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/GlobalUsings.cs`

**Steps:**

- [ ] **Step 1: Revert the six WebApi endpoints**

Same substitution as Task 7 Step 4: delete `using Sergin.SharedKernel.Application.Dispatching;`, `ISerginSender sender`→`ISender sender`, `.SendAsync(`→`.Send(`. `GetDeviceEndpoint.cs` today:
```csharp
using Sergin.SharedKernel.Application.Dispatching;
...
routeBuilder.MapGet("/devices/{deviceId:guid}", async ([FromRoute] Guid deviceId, ISerginSender sender) =>
{
    ErrorOr<DeviceQueryResponse> res = await sender.SendAsync(new GetDeviceByIdQueryCommand(deviceId));

    return res.ToApiResult();
});
```
becomes
```csharp
routeBuilder.MapGet("/devices/{deviceId:guid}", async ([FromRoute] Guid deviceId, ISender sender) =>
{
    ErrorOr<DeviceQueryResponse> res = await sender.Send(new GetDeviceByIdQueryCommand(deviceId));

    return res.ToApiResult();
});
```
(the `using Sergin.SharedKernel.Application.Dispatching;` line is simply removed — `ISender` comes from the project's global `MediatR` using). Apply the equivalent change to the other five endpoint files, reading each first.

- [ ] **Step 2: Revert the three Blazor page code-behinds**

Same substitution as Task 7 Step 1. `DeviceListPage.razor.cs` today:
```csharp
using Sergin.SharedKernel.Application.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
...
[Inject]
private ISerginSender Sender { get; set; } = default!;
...
await Sender.SendListAsync<GetDeviceListItem>(state.PageSize, state.Page + 1, cancellationToken);
```
becomes
```csharp
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
...
[Inject]
private ISerginDispatcher Dispatcher { get; set; } = default!;
...
await Dispatcher.SendListAsync<GetDeviceListItem>(state.PageSize, state.Page + 1, cancellationToken);
```
(the `Application.Dispatching` using is deleted; `Presentation.Blazor.Dispatching` was already imported for `SendListAsync` and now also covers `ISerginDispatcher`). Apply the equivalent change to `DeviceDetailPage.razor.cs` and `CreateDevicePage.razor.cs`, reading each first to match exact current variable names.

- [ ] **Step 3: Fix `_Imports.razor`**

Remove line 7 (`@using Sergin.SharedKernel.Application.Dispatching`) — line 8 (`@using Sergin.SharedKernel.Presentation.Blazor.Dispatching`) already covers `ISerginDispatcher`'s new-old namespace.

- [ ] **Step 4: Fix `GlobalUsings.cs`**

Current content:
```csharp
global using ErrorOr;
global using MediatR;
global using Sergin.SharedKernel.Application;
global using Sergin.SharedKernel.Application.Dispatching;
```
Replace the last line with:
```csharp
global using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
```

- [ ] **Step 5: Build to verify**

```bash
dotnet build Sergin.MeterMinder.slnx -p:NuGetAudit=false
```
Expect remaining errors only from `DeviceGrpcRoundTripTests`/`CreateAndGetUserTests` (Task 12, not yet done) and anything referencing the not-yet-created `DeviceManagementRemoteModule` (Task 11, do that first if build order matters more than task-review granularity — see note below).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Revert DeviceManagement presentation layers to ISerginDispatcher/ISender"
```

---

### Task 11: Add DeviceManagement's `ISerginRemoteModule` implementation

**Files:**
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/DeviceManagementRemoteServicesExtensions.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/DeviceManagementRemoteModule.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Devices/DeviceGrpcService.cs` (doc-comment fix only)

**Interfaces:**
- Consumes: `RemoteForwardingHandler<TRequest,TResponse>` (`Sergin.SharedKernel.Infrastructure.Dispatching`, Task 2), `IRemoteInvoker<GetDeviceByIdQueryCommand,DeviceQueryResponse>`/`GetDeviceByIdGrpcInvoker` (already exist in this project), `ISerginRemoteModule` (Task 3).
- Produces: `DeviceManagementRemoteModule : ISerginRemoteModule` — the class a future gateway host would reference for the `dm` schema.

**Why this is a separate class from `DeviceManagementModule`, not an added interface on it:** see the spec's §5 — `DeviceManagementModule` (the composition root, `Sergin.MeterMinder.DeviceManagement`) already references `.Infrastructure`, `.Presentation.WebApi`, `.Presentation.Blazor`, all of which transitively pull in `.Application`. Implementing `ISerginRemoteModule` there too would force a gateway host to reference the whole composition root just to reach the Remote capability, defeating the isolation this whole plan exists to establish.

- [ ] **Step 1: Add the `ProjectReference` to `Sergin.SharedKernel.Infrastructure`**

In `Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj`, add to the existing `ProjectReference` `<ItemGroup>`:
```xml
<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Infrastructure\Sergin.SharedKernel.Infrastructure.csproj" />
```
Safe direction: `Sergin.SharedKernel.Infrastructure` has no reference back to any module, so this cannot create a cycle, and it doesn't pull in `DeviceManagement.Application`/`.Infrastructure` — isolation holds.

- [ ] **Step 2: Create `DeviceManagementRemoteServicesExtensions.cs`**

```csharp
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Devices;
using Sergin.SharedKernel.Infrastructure.Dispatching;
using Sergin.SharedKernel.Presentation.Grpc.Dispatching;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Grpc;

public static class DeviceManagementRemoteServicesExtensions
{
    public static IServiceCollection AddDeviceManagementRemoteServices(
        this IServiceCollection services, IConfigurationSection configuration)
    {
        string address = configuration["GrpcAddress"]
            ?? throw new InvalidOperationException(
                "Missing 'GrpcAddress' under the 'dm' section — required when the DeviceManagement module is registered Remote.");

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

- [ ] **Step 3: Create `DeviceManagementRemoteModule.cs`**

```csharp
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts;
using Sergin.SharedKernel.Modules;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Grpc;

/// <remarks>
/// Public, not internal: no production host references this yet — same "live-but-unhosted" posture as
/// DeviceGrpcService/GetDeviceByIdGrpcInvoker in this same project. A future gateway host would reference
/// this class (and this project) instead of the DeviceManagementModule composition root, specifically to
/// avoid pulling in .Application/.Infrastructure it has no need to run locally.
/// </remarks>
public sealed class DeviceManagementRemoteModule : ISerginRemoteModule
{
    // Must match DeviceManagementDbContext.Schema ("dm"). Duplicated, not shared, because this project
    // must not reference .Infrastructure.Data — that's the isolation property this class exists for.
    public string Schema => "dm";

    public Assembly ContractsAssembly => DeviceManagementApplicationContractsAssemblyReference.Assembly;

    public void AddRemoteServices(IServiceCollection services, IConfigurationSection configuration)
        => services.AddDeviceManagementRemoteServices(configuration);
}
```

- [ ] **Step 4: Fix `DeviceGrpcService.cs`'s doc comment**

Its class-level `<summary>` currently reads "...dispatched the same way GetDeviceEndpoint (Presentation.WebApi) dispatches via ISerginSender (which itself still ends in ISender.Send inside its own scope)...". Update to: "...dispatched the same way `GetDeviceEndpoint` (Presentation.WebApi) dispatches — directly via `ISender.Send`, no wrapper on either side anymore..." (matching Task 10's revert — WebApi endpoints no longer go through a sender wrapper at all).

- [ ] **Step 5: Build to verify**

```bash
dotnet build Sergin.MeterMinder.slnx -p:NuGetAudit=false
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add DeviceManagementRemoteModule: ISerginRemoteModule implementation for dm"
```

---

### Task 12: Rework `CreateAndGetUserTests` and `DeviceGrpcRoundTripTests`

**Files:**
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Users/CreateAndGetUserTests.cs`
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Devices/DeviceGrpcRoundTripTests.cs`
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Dispatching/RoutingSerginSenderTests.cs` (delete or rewrite — see Step 3)

**Steps:**

- [ ] **Step 1: `CreateAndGetUserTests`**

Change whatever it currently resolves (`ISerginSender`, per the prior — now superseded — migration) to `ISerginDispatcher` (`Sergin.SharedKernel.Presentation.Blazor.Dispatching`), and the send call from `SendAsync` naming if changed — read the file first; the type name and `using` are the only expected changes, the fresh-scope-per-send behavioral property this test relies on (Postgres round-trip, not the writing `DbContext`'s change tracker) is unchanged since `ScopedSerginDispatcher` still provides it.

- [ ] **Step 2: `DeviceGrpcRoundTripTests`**

Full rework, not a rename. Replace `BuildSender`/`FixedRouteResolver` and the `ISerginSender`/`IDispatchRouteResolver` registrations with a plain `ISender`-based comparison on both sides, and add the real `PermissionCheckPipelineBehavior` (now reachable via Task 5's `InternalsVisibleTo` grant):

```csharp
private ISender BuildRemoteSender(Permission[] permissions)
{
    ServiceCollection services = new();

    services.AddSingleton<IUserContextFactory>(new StubUserContextFactory(permissions));
    services.AddScoped(p => p.GetRequiredService<IUserContextFactory>().CreateUserContext());
    services.AddSingleton(new DeviceService.DeviceServiceClient(channel));
    services.AddScoped<IRemoteInvoker<GetDeviceByIdQueryCommand, DeviceQueryResponse>, GetDeviceByIdGrpcInvoker>();

    services.AddMediatR(o => o.AddOpenBehavior(typeof(PermissionCheckPipelineBehavior<,>)));

    // Not discovered by AddMediatR's assembly scan (generic, lives outside any scanned assembly) —
    // registered explicitly, same as production's AddDeviceManagementRemoteServices (Task 11).
    services.AddTransient<
        IRequestHandler<GetDeviceByIdQueryCommand, ErrorOr<DeviceQueryResponse>>,
        RemoteForwardingHandler<GetDeviceByIdQueryCommand, DeviceQueryResponse>>();

    return services.BuildServiceProvider().GetRequiredService<ISender>();
}
```
(`ValidationPipelineBehavior` is intentionally omitted here, same as the "Local" comparison side already omits it today — no `IValidator<GetDeviceByIdQueryCommand>` exists to register, so adding the open behavior with nothing registered is a harmless no-op either way; add it too if the reviewer prefers full parity with production's `AddMediatR` call in `AddSerginCore`.)

Update the three test methods to call `sender.Send(command)` directly instead of `remoteSender.SendAsync(command)`/`sender.SendAsync(...)`, using `BuildRemoteSender(...)` instead of `BuildSender(remote: true, ...)`. The "Local" comparison side (`server.Services.GetRequiredService<ISender>()`) is unchanged — it never went through a dispatcher of any kind, so nothing there was affected by the redesign. Delete the `FixedRouteResolver` private class entirely (no longer meaningful — there is no runtime routing decision left to fix). Update the class's own doc comment and the forbidden-path test's inline comment (which currently references "the permission short-circuit in RoutingSerginSender") to describe the new mechanism: the real `PermissionCheckPipelineBehavior`, registered for real in this test's own `AddMediatR` call, made possible by Task 5's `InternalsVisibleTo` grant.

- [ ] **Step 3: Handle `RoutingSerginSenderTests.cs`**

This file tests `RoutingSerginSender`, which no longer exists — its whole subject matter (the Local/Remote runtime branch, the permission pre-check) has been deleted, not moved. Delete the file. If any of its assertions covered behavior that still exists elsewhere (e.g., that `ScopedSerginDispatcher` really does open a fresh scope per call) and isn't already covered by `CreateAndGetUserTests`'s reliance on that same property, consider whether a small new test for `ScopedSerginDispatcher` itself is warranted — read the file first before deciding; if its assertions were entirely about the deleted routing/permission logic, a clean delete is correct and no replacement test is needed.

- [ ] **Step 4: Build and test**

```bash
dotnet build Sergin.MeterMinder.slnx -p:NuGetAudit=false
dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj -p:NuGetAudit=false
```
Requires Docker (Testcontainers.PostgreSql). All tests should pass, matching the pre-existing count minus `RoutingSerginSenderTests`' cases if deleted outright.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Rework DeviceGrpcRoundTripTests and CreateAndGetUserTests for the dispatch redesign"
```

---

### Task 13: Update host `add-feature` skill copy + root `CLAUDE.md`

**Files:**
- Modify: `.claude/skills/add-feature/SKILL.md`
- Modify: `.claude/CLAUDE.md`
- Modify: `src/Modules/DeviceManagement/CLAUDE.md`

**Steps:**

- [ ] **Step 1: Revert the `add-feature` skill's scaffolding templates**

WebApi endpoint template: injected type back to `ISender` (no `Sergin.SharedKernel.Application.Dispatching` using). Blazor page template: injected type back to `ISerginDispatcher` from `Sergin.SharedKernel.Presentation.Blazor.Dispatching`.

- [ ] **Step 2: Revert `.claude/CLAUDE.md`**

This file's dispatch-related prose is extensive — the "Blazor UI conventions" section's `ISerginSender`/`RoutingSerginSender` bullet, "The Blazor UI host"'s `Sergin:Dispatch:Modules` bullet, the `AddSerginCore`/`Hosts`/`Hosts.WebApi`/`Hosts.WebUi` paragraphs under "Host / module composition", and the Overview's `.Presentation.Grpc` paragraph. Rewrite to describe:
  - `ISerginDispatcher`/`ScopedSerginDispatcher`, Blazor-only, in `Sergin.SharedKernel.Presentation.Blazor.Dispatching`.
  - WebApi endpoints inject `ISender` directly — no wrapper.
  - `AddSerginCore(localModules, remoteModules)` — composition-time split, no more `Sergin:Dispatch:Modules` config key, no more `IDispatchRouteResolver`.
  - `RemoteForwardingHandler`/`ISerginRemoteModule`/`DeviceManagementRemoteModule` as the new machinery, still "live-but-unhosted" (no gateway host exists) — same posture `.Presentation.Grpc`/`DeviceGrpcService` already had, now joined by this.
  - Remove every reference to `Sergin:Dispatch:Modules` as a required startup config key.

- [ ] **Step 3: Fix `src/Modules/DeviceManagement/CLAUDE.md`**

Its `Devices` aggregate section says "Both still end in `ISender.Send(GetDeviceByIdQueryCommand)` — the WebApi side via `ISerginSender.SendAsync`, the gRPC side directly". Update to: "...the WebApi side directly via `ISender.Send`, the gRPC side via the same call inside `DeviceGrpcService` — no wrapper on either side." Also add a short mention of `DeviceManagementRemoteModule`/`AddDeviceManagementRemoteServices` (Task 11) alongside the existing `GetDeviceByIdGrpcInvoker`/`DeviceGrpcService` description in the `GetOne` feature's paragraph.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Update add-feature skill and CLAUDE.md docs for the dispatch redesign"
```

---

### Task 14: Finalize submodule pointers + full verification

**Files:**
- Modify (pointer only): `src/SharedKernel`
- Modify (pointer only): `src/Modules/UserAccess`

**Steps:**

- [ ] **Step 1: Confirm both submodules are clean** (`git -C src/SharedKernel status --short`, `git -C src/Modules/UserAccess status --short`, both empty — no further submodule-side commits were made after Task 9's pointer bump, since Phase C's work is entirely in the host repo).

- [ ] **Step 2: Full solution build**

```bash
dotnet build Sergin.MeterMinder.slnx -p:NuGetAudit=false
```
0 warnings, 0 errors, across every project.

- [ ] **Step 3: Full integration test run**

```bash
dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj -p:NuGetAudit=false
```
All tests pass (requires Docker).

- [ ] **Step 4: Standalone SharedKernel build** (belt-and-suspenders re-check, since Task 6 already did this before Phase B/C's changes)

```bash
dotnet build Sergin.SharedKernel.slnx -p:NuGetAudit=false
```

- [ ] **Step 5: If Step 1 found nothing to commit, no host-side commit is needed here** — Task 9 already captured the only pointer bump this plan produces. If any submodule work happened after Task 9 that wasn't yet pointer-bumped (shouldn't happen per this plan's task order, but check), commit the bump now with the same message shape as Task 9.

- [ ] **Step 6: Report readiness** — this branch is ready for the user's own review of the diff (per standing instruction: no push, no PR, until the user says so).
