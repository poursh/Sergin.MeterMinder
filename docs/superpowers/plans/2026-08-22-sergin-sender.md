# ISerginSender Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Blazor-only `ISerginUiDispatcher` with a presentation-agnostic `ISerginSender` — contract in `Sergin.SharedKernel.Application`, implementation in `Sergin.SharedKernel.Infrastructure` — consumed by Blazor pages and WebApi endpoints alike, with per-module Local/Remote dispatch mode unchanged.

**Architecture:** This is a structural relocation and rename of already-shipped, already-tested behavior (`RoutingSerginUiDispatcher`'s scope-per-call + permission pre-check + Local/Remote branch), not new behavior. No task in this plan follows red-green TDD for that reason — there is nothing new to characterize with a failing test. Instead every task follows a **refactor-safety pattern**: confirm the relevant build/test is green *before* the change, make the change, confirm it is green *after*. The repo's only test project is integration-level (`Sergin.MeterMinder.IntegrationTests.All`); that suite is the safety net for every behavioral task, and `dotnet build` is the safety net for every pure move/rename task.

**Tech Stack:** .NET 10, MediatR, ErrorOr, xUnit + Testcontainers.PostgreSql (integration tests only).

**Spec:** `docs/superpowers/specs/2026-08-22-sergin-sender-design.md`

## Global Constraints

- `TreatWarningsAsErrors=true`, `AnalysisMode=All`, SonarAnalyzer.CSharp enabled — any analyzer warning fails the build. Write clean code the first time.
- Central Package Management: `PackageReference` items carry no `Version` attribute; new packages get a version-less `PackageReference` plus an alphabetically-placed `<PackageVersion>` entry in the relevant `Directory.Packages.props` (each repo has its own).
- This change spans **three separate git repos**: `Sergin.MeterMinder` (host), `src/SharedKernel` (submodule → `Sergin.SharedKernel`), `src/Modules/UserAccess` (submodule → `Sergin.UserAccess`). Each submodule is its own git history; changes there need their own commit (and, per each repo's own convention, their own PR) — they are not committed as part of the host repo's diff. The host repo's submodule *pointer* is a separate, final commit once the submodule PRs are in.
- Never add a `Co-Authored-By: Claude` trailer; commit under the user's configured git identity.
- Work in isolated git worktrees per repo (`superpowers:using-git-worktrees`) — this touches 3 repos concurrently and should not collide with any in-progress work in the primary checkouts. A fresh `Sergin.MeterMinder` worktree needs `git submodule update --init --recursive` before building.
- Pushing a branch or opening a PR in any of the three repos is a visible-to-others action — confirm with the user before doing it, per each repo's own remote (`poursh/Sergin.SharedKernel`, `poursh/Sergin.UserAccess`, and the host repo's own remote for the pointer-bump commit).

---

## File Structure

**Phase A — `src/SharedKernel` (own repo/branch):**
- Create: `Sergin.SharedKernel.Application/Dispatching/ISerginSender.cs`
- Create: `Sergin.SharedKernel.Application/Dispatching/IDispatchRouteResolver.cs`
- Delete: `Sergin.SharedKernel.Presentation.Blazor/Dispatching/ISerginUiDispatcher.cs`
- Delete: `Sergin.SharedKernel.Presentation.Blazor/Dispatching/IDispatchRouteResolver.cs`
- Create: `Sergin.SharedKernel.Infrastructure/Dispatching/RoutingSerginSender.cs`
- Create: `Sergin.SharedKernel.Infrastructure/Dispatching/ModuleDispatchRouteResolver.cs`
- Create: `Sergin.SharedKernel.Infrastructure/Dispatching/DispatchModeOptions.cs`
- Create: `Sergin.SharedKernel.Infrastructure/Dispatching/DispatchModeOptionsValidator.cs`
- Delete: `Sergin.SharedKernel.Presentation.Blazor/Dispatching/RoutingSerginUiDispatcher.cs`
- Delete: `Sergin.SharedKernel.Hosts.WebUi/Dispatching/ModuleDispatchRouteResolver.cs`
- Delete: `Sergin.SharedKernel.Hosts/Dispatching/DispatchModeOptions.cs`
- Delete: `Sergin.SharedKernel.Hosts/Dispatching/DispatchModeOptionsValidator.cs`
- Modify: `Sergin.SharedKernel.Infrastructure/Sergin.SharedKernel.Infrastructure.csproj` (add refs)
- Modify: `Sergin.SharedKernel.Presentation.WebApi/Sergin.SharedKernel.Presentation.WebApi.csproj` (add ref)
- Modify: `Sergin.SharedKernel.Presentation.Blazor/Dispatching/SerginUiDispatcherExtensions.cs` → rename to `SerginSenderExtensions.cs`
- Modify: `Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs` (registration)
- Modify: `Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs` (remove dispatch registration)
- Modify: `Sergin.SharedKernel.Presentation.Blazor/SerginBlazorKitExtensions.cs` (remove `ISerginUiDispatcher` registration)
- Modify: `.claude/CLAUDE.md` (SharedKernel repo's own doc)

**Phase B — `src/Modules/UserAccess` (own repo/branch):**
- Modify: `Sergin.UserAccess.Presentation.WebApi/Users/Endpoints/{Create,GetOne,GetList,DeactivateUser}/*.cs` (4 files)
- Modify: `Sergin.UserAccess.Presentation.Blazor/Users/Pages/{UserListPage,UserDetailPage,CreateUserPage}.razor.cs` (3 files)
- Modify: `.claude/skills/add-feature/SKILL.md` (UserAccess repo's own copy)
- Modify: `.claude/CLAUDE.md` (UserAccess repo's own doc)

**Phase C — host repo `Sergin.MeterMinder`:**
- Modify: `src/Modules/DeviceManagement/.../Presentation.WebApi/{Devices,Manufacturers}/Endpoints/**/*.cs` (6 files)
- Modify: `src/Modules/DeviceManagement/.../Presentation.Blazor/Devices/Pages/{DeviceListPage,DeviceDetailPage,CreateDevicePage}.razor.cs` (3 files)
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Users/CreateAndGetUserTests.cs`
- Modify: `.claude/skills/add-feature/SKILL.md` (host repo's own copy)
- Modify: `.claude/CLAUDE.md` (host repo)
- Modify: `src/SharedKernel` (submodule pointer bump)
- Modify: `src/Modules/UserAccess` (submodule pointer bump)

---

## Phase A — SharedKernel repo

### Task 1: Move dispatch contracts into Application

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Dispatching/ISerginSender.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Dispatching/IDispatchRouteResolver.cs`
- Delete: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/ISerginUiDispatcher.cs`
- Delete: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/IDispatchRouteResolver.cs`

**Interfaces:**
- Produces: `Sergin.SharedKernel.Application.Dispatching.ISerginSender.SendAsync<TResponse>(IRequest<ErrorOr<TResponse>>, CancellationToken = default) : Task<ErrorOr<TResponse>>`
- Produces: `Sergin.SharedKernel.Application.Dispatching.IDispatchRouteResolver.IsRemote(Type requestType) : bool`

- [ ] **Step 1: Confirm SharedKernel builds clean before touching anything**

Run (from `src/SharedKernel`, in the isolated worktree): `dotnet build Sergin.SharedKernel.slnx`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 2: Create the new `ISerginSender` interface**

```csharp
namespace Sergin.SharedKernel.Application.Dispatching;

/// <summary>
/// Sends a MediatR request through a fresh DI scope, with a permission pre-check and Local/Remote
/// routing applied uniformly across every presentation adapter (Blazor pages, WebApi endpoints).
/// The Blazor-circuit-lifetime rationale that originally motivated the fresh-scope-per-call behavior
/// still applies to Blazor; WebApi callers get the same scope-per-call shape for free, at the cost of
/// one extra, immediately-disposed scope per call under a host where the framework already scopes
/// correctly per request.
/// </summary>
public interface ISerginSender
{
    Task<ErrorOr<TResponse>> SendAsync<TResponse>(
        IRequest<ErrorOr<TResponse>> request, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Create the new `IDispatchRouteResolver` interface**

```csharp
namespace Sergin.SharedKernel.Application.Dispatching;

public interface IDispatchRouteResolver
{
    bool IsRemote(Type requestType);
}
```

- [ ] **Step 4: Delete the old Blazor-only interfaces**

Delete `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/ISerginUiDispatcher.cs` and `.../IDispatchRouteResolver.cs`. Leave the `Dispatching` folder in place — `RoutingSerginUiDispatcher.cs` and `SerginUiDispatcherExtensions.cs` still live there until Tasks 2–3.

- [ ] **Step 5: Build — expect failure naming every broken reference**

Run: `dotnet build Sergin.SharedKernel.slnx`
Expected: FAIL. Errors will point at `RoutingSerginUiDispatcher.cs` (implements the now-deleted `ISerginUiDispatcher`), `ModuleDispatchRouteResolver.cs` (implements the now-deleted `IDispatchRouteResolver`), `SerginBlazorKitExtensions.cs`, `SerginUiDispatcherExtensions.cs`, and `SerginWebUiExtensions.cs`. This is expected and fixed across Tasks 2–4 — do not fix them here.

- [ ] **Step 6: Commit**

```bash
git add Sergin.SharedKernel.Application/Dispatching/ISerginSender.cs \
        Sergin.SharedKernel.Application/Dispatching/IDispatchRouteResolver.cs
git rm Sergin.SharedKernel.Presentation.Blazor/Dispatching/ISerginUiDispatcher.cs \
       Sergin.SharedKernel.Presentation.Blazor/Dispatching/IDispatchRouteResolver.cs
git commit -m "Move dispatch contracts into Application as ISerginSender/IDispatchRouteResolver"
```

---

### Task 2: Move the dispatch implementation into Infrastructure

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure/Dispatching/RoutingSerginSender.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure/Dispatching/ModuleDispatchRouteResolver.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure/Dispatching/DispatchModeOptions.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure/Dispatching/DispatchModeOptionsValidator.cs`
- Delete: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/RoutingSerginUiDispatcher.cs`
- Delete: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/Dispatching/ModuleDispatchRouteResolver.cs`
- Delete: `src/SharedKernel/Sergin.SharedKernel.Hosts/Dispatching/DispatchModeOptions.cs`
- Delete: `src/SharedKernel/Sergin.SharedKernel.Hosts/Dispatching/DispatchModeOptionsValidator.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastructure/Sergin.SharedKernel.Infrastructure.csproj`

**Interfaces:**
- Consumes: `ISerginSender`, `IDispatchRouteResolver` (Task 1); `IRemoteInvoker<TRequest,TResponse>` (existing, `Sergin.SharedKernel.Presentation.Grpc.Dispatching`); `IUserContext` (existing, `Sergin.SharedKernel.Application.Securities.Users`)
- Produces: `Sergin.SharedKernel.Infrastructure.Dispatching.RoutingSerginSender : ISerginSender` (ctor: `IServiceScopeFactory scopeFactory, IDispatchRouteResolver routeResolver`); `Sergin.SharedKernel.Infrastructure.Dispatching.ModuleDispatchRouteResolver : IDispatchRouteResolver` (ctor: `IReadOnlyDictionary<Assembly, string> schemaByAssembly, IOptions<DispatchModeOptions> options`); `Sergin.SharedKernel.Infrastructure.Dispatching.DispatchModeOptions` (`IReadOnlyDictionary<string, DispatchMode> Modules`); `DispatchMode` enum (`Local`, `Remote`)

- [ ] **Step 1: Add the two new project references + package to Infrastructure's csproj**

Read `src/SharedKernel/Sergin.SharedKernel.Infrastructure/Sergin.SharedKernel.Infrastructure.csproj` first, then edit its `ItemGroup`s to:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="MediatR" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Sergin.SharedKernel.Application\Sergin.SharedKernel.Application.csproj" />
    <ProjectReference Include="..\Sergin.SharedKernel.Presentation.Grpc\Sergin.SharedKernel.Presentation.Grpc.csproj" />
  </ItemGroup>
</Project>
```

(Keep whatever else is already in the file — read it first, this shows only the dispatch-relevant additions. `MediatR` already has a `<PackageVersion>` entry in this repo's `Directory.Packages.props` since `Application`/`Presentation.Blazor`/`Hosts` all already reference it — no new `PackageVersion` entry needed, confirm by checking `Directory.Packages.props` before assuming.)

- [ ] **Step 2: Create `RoutingSerginSender.cs`** — move `RoutingSerginUiDispatcher`'s body verbatim, renamed

```csharp
using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Dispatching;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Presentation.Grpc.Dispatching;

namespace Sergin.SharedKernel.Infrastructure.Dispatching;

/// <summary>
/// Every send opens one fresh DI scope (Blazor Server "scoped" is the whole SignalR circuit, not a
/// request, and WebApi gets the same shape for free at the cost of one harmless extra scope) and runs
/// a permission check against IUserContext before branching Local (ISender.Send, in-process) or Remote
/// (IRemoteInvoker, over gRPC). The permission check runs unconditionally, not just for Remote: Local
/// mode already re-checks it inside MediatR's PermissionCheckPipelineBehavior, so this is a deliberate,
/// cheap redundancy, not a correctness gap for Local.
/// </summary>
internal sealed class RoutingSerginSender(
    IServiceScopeFactory scopeFactory,
    IDispatchRouteResolver routeResolver) : ISerginSender
{
    private static readonly ConcurrentDictionary<(Type Request, Type Response), Type> invokerTypeCache = new();

    public async Task<ErrorOr<TResponse>> SendAsync<TResponse>(
        IRequest<ErrorOr<TResponse>> request, CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        IUserContext userContext = scope.ServiceProvider.GetRequiredService<IUserContext>();

        RequiredPermissionsAttribute? attribute =
            request.GetType().GetCustomAttribute<RequiredPermissionsAttribute>();

        if (attribute is not null && !userContext.HasPermission(attribute.Permissionas))
        {
            return Error.Forbidden();
        }

        Type requestType = request.GetType();

        if (routeResolver.IsRemote(requestType))
        {
            Type invokerType = invokerTypeCache.GetOrAdd(
                (requestType, typeof(TResponse)),
                key => typeof(IRemoteInvoker<,>).MakeGenericType(key.Request, key.Response));

            dynamic invoker = scope.ServiceProvider.GetRequiredService(invokerType);
            return await invoker.InvokeAsync((dynamic)request, cancellationToken);
        }

        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        return await sender.Send(request, cancellationToken);
    }
}
```

- [ ] **Step 3: Create `ModuleDispatchRouteResolver.cs`** — move verbatim (logic unchanged), only the namespace/home changes

```csharp
using System.Reflection;
using Microsoft.Extensions.Options;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Dispatching;

namespace Sergin.SharedKernel.Infrastructure.Dispatching;

/// <summary>
/// Maps a request type to its owning module's schema via the request's declaring assembly, then looks
/// that schema up in DispatchModeOptions. Constructed with a closure over the registered modules by
/// AddSerginCore (Sergin.SharedKernel.Hosts) — not resolved from DI, matching SerginUiModuleCatalog's
/// and DispatchModeOptionsValidator's precedent.
/// </summary>
internal sealed class ModuleDispatchRouteResolver(
    IReadOnlyDictionary<Assembly, string> schemaByAssembly,
    IOptions<DispatchModeOptions> options) : IDispatchRouteResolver
{
    public bool IsRemote(Type requestType)
    {
        Type schemaSourceType = ResolveSchemaSourceType(requestType);

        if (!schemaByAssembly.TryGetValue(schemaSourceType.Assembly, out string? schema))
        {
            throw new InvalidOperationException(
                $"'{requestType.FullName}' does not belong to any registered module's ApplicationAssembly.");
        }

        if (!options.Value.Modules.TryGetValue(schema, out DispatchMode mode))
        {
            throw new InvalidOperationException($"No dispatch mode configured for module schema '{schema}'.");
        }

        return mode == DispatchMode.Remote;
    }

    /// <summary>
    /// List queries have no module-specific request type: SendListAsync always builds a closed
    /// ListQuery&lt;TResponseData&gt;, whose generic type definition lives in Sergin.SharedKernel.Application
    /// itself, not any module's ApplicationAssembly. Unwrap to the last type argument (the response-item
    /// type) instead, which does belong to a module's ApplicationAssembly.
    /// </summary>
    private static Type ResolveSchemaSourceType(Type requestType)
    {
        if (!requestType.IsGenericType)
        {
            return requestType;
        }

        Type definition = requestType.GetGenericTypeDefinition();

        return definition == typeof(ListQuery<>) || definition == typeof(ListQuery<,>)
            ? requestType.GetGenericArguments()[^1]
            : requestType;
    }
}
```

- [ ] **Step 4: Create `DispatchModeOptions.cs` and `DispatchModeOptionsValidator.cs`** — read the two files being deleted first (`Sergin.SharedKernel.Hosts/Dispatching/DispatchModeOptions.cs`, `.../DispatchModeOptionsValidator.cs`) and move their content verbatim into `Sergin.SharedKernel.Infrastructure/Dispatching/`, changing only the `namespace` line to `Sergin.SharedKernel.Infrastructure.Dispatching`.

- [ ] **Step 5: Delete the four old files**

```bash
git rm Sergin.SharedKernel.Presentation.Blazor/Dispatching/RoutingSerginUiDispatcher.cs \
       Sergin.SharedKernel.Hosts.WebUi/Dispatching/ModuleDispatchRouteResolver.cs \
       Sergin.SharedKernel.Hosts/Dispatching/DispatchModeOptions.cs \
       Sergin.SharedKernel.Hosts/Dispatching/DispatchModeOptionsValidator.cs
```

- [ ] **Step 6: Build — expect failure only in the consumer files fixed in Tasks 3–4**

Run: `dotnet build Sergin.SharedKernel.slnx`
Expected: FAIL, errors confined to `SerginBlazorKitExtensions.cs`, `SerginUiDispatcherExtensions.cs`, `SerginWebUiExtensions.cs`, `Sergin.SharedKernel.Hosts` project (missing `Dispatching` namespace references). Confirm no error mentions `RoutingSerginSender.cs` or `ModuleDispatchRouteResolver.cs` themselves — if one does, fix it now before moving on.

- [ ] **Step 7: Commit**

```bash
git add Sergin.SharedKernel.Infrastructure/
git commit -m "Move dispatch implementation (RoutingSerginSender, ModuleDispatchRouteResolver, DispatchModeOptions) into Infrastructure"
```

---

### Task 3: Rewire Presentation.WebApi + rename the Blazor list-send extension

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.WebApi/Sergin.SharedKernel.Presentation.WebApi.csproj`
- Modify (rename): `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/SerginUiDispatcherExtensions.cs` → `SerginSenderExtensions.cs`

**Interfaces:**
- Consumes: `ISerginSender` (Task 1)
- Produces: `Sergin.SharedKernel.Presentation.Blazor.Dispatching.SerginSenderExtensions.SendListAsync<TItem>(this ISerginSender sender, int pageSize, int pageIndex, CancellationToken = default) : Task<ErrorOr<ListQueryResponse<TItem>>>` (same signature as before, just on `ISerginSender`)

- [ ] **Step 1: Add the `Application` project reference to `Presentation.WebApi`'s csproj**

Read the file first, then add inside the existing `ItemGroup`:

```xml
<ProjectReference Include="..\Sergin.SharedKernel.Application\Sergin.SharedKernel.Application.csproj" />
```

alongside the existing `Sergin.SharedKernel.Presentation` reference.

- [ ] **Step 2: Rename and update the list-send extension**

Read `Sergin.SharedKernel.Presentation.Blazor/Dispatching/SerginUiDispatcherExtensions.cs` in full, then recreate it as `SerginSenderExtensions.cs` with every `ISerginUiDispatcher` parameter/type reference changed to `ISerginSender` and the `using Sergin.SharedKernel.Application.Dispatching;` import added. Delete the old file.

- [ ] **Step 3: Build**

Run: `dotnet build Sergin.SharedKernel.slnx`
Expected: FAIL only in `SerginBlazorKitExtensions.cs`, `SerginWebUiExtensions.cs`, `Sergin.SharedKernel.Hosts` (Task 4 territory). No error from `Presentation.WebApi` or `SerginSenderExtensions.cs`.

- [ ] **Step 4: Commit**

```bash
git add Sergin.SharedKernel.Presentation.WebApi/Sergin.SharedKernel.Presentation.WebApi.csproj \
        Sergin.SharedKernel.Presentation.Blazor/Dispatching/
git commit -m "Reference Application from Presentation.WebApi; rename SendListAsync extension onto ISerginSender"
```

---

### Task 4: Wire registration through AddSerginCore

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/SerginBlazorKitExtensions.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/Sergin.SharedKernel.Hosts.csproj` (if it needs a new reference — check first)

**Interfaces:**
- Consumes: `RoutingSerginSender`, `ModuleDispatchRouteResolver`, `DispatchModeOptions`, `DispatchModeOptionsValidator` (Task 2)
- Produces: `ISerginSender` and `IDispatchRouteResolver` registered as singletons by `AddSerginCore` for every host

- [ ] **Step 1: Read `SerginCoreExtensions.cs` and `SerginWebUiExtensions.cs` in full**

Confirm the exact current registration order in `AddSerginBlazorApp` (dispatch options binding, `IDispatchRouteResolver`, `SerginUiModuleCatalog`, `SerginHome`) and in `AddSerginCore` (MediatR scan, pipeline behaviors, `IDbConnectionFactory`, `IUserContext`, localizer, `module.AddServices` loop) before editing either.

- [ ] **Step 2: Add dispatch registration to `AddSerginCore`**

Insert, after the existing registrations and before the `module.AddServices(...)` loop (so `modules` is already in scope):

```csharp
builder.Services
    .AddOptions<DispatchModeOptions>()
    .Bind(builder.Configuration.GetSection("Dispatch"))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<DispatchModeOptions>, DispatchModeOptionsValidator>();

IReadOnlyDictionary<Assembly, string> schemaByAssembly =
    modules.ToDictionary(module => module.ApplicationAssembly, module => module.Schema);
builder.Services.AddSingleton<IDispatchRouteResolver>(serviceProvider =>
    new ModuleDispatchRouteResolver(
        schemaByAssembly,
        serviceProvider.GetRequiredService<IOptions<DispatchModeOptions>>()));
builder.Services.AddSingleton<ISerginSender, RoutingSerginSender>();
```

Add `using Sergin.SharedKernel.Application.Dispatching;`, `using Sergin.SharedKernel.Infrastructure.Dispatching;`, `using System.Reflection;` (if not already present) to the top of the file.

- [ ] **Step 3: Remove the now-duplicate registration from `AddSerginBlazorApp`**

Delete the `Sergin:Dispatch:Modules` binding/validation block and the `IDispatchRouteResolver` registration that currently live in `SerginWebUiExtensions.cs`'s `AddSerginBlazorApp` — they're now inherited from `AddSerginCore`, which `AddSerginBlazorApp` already calls.

- [ ] **Step 4: Remove `ISerginUiDispatcher` registration from `AddSerginBlazorKit`**

In `SerginBlazorKitExtensions.cs`, delete the line registering `ISerginUiDispatcher`/`RoutingSerginUiDispatcher` — `ISerginSender` now comes from `AddSerginCore`, which every host calls before/alongside `AddSerginBlazorKit()`.

- [ ] **Step 5: Build**

Run: `dotnet build Sergin.SharedKernel.slnx`
Expected: PASS. If `Sergin.SharedKernel.Hosts.csproj` doesn't yet reference `Sergin.SharedKernel.Infrastructure` (it references `Infrastructure.Data.EFCore` and `Infrastructure` already per the existing csproj — confirm this before assuming a new reference is needed), no csproj edit is needed here.

- [ ] **Step 6: Commit**

```bash
git add Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs \
        Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs \
        Sergin.SharedKernel.Presentation.Blazor/SerginBlazorKitExtensions.cs
git commit -m "Register ISerginSender + dispatch options once, in AddSerginCore"
```

---

### Task 5: Update SharedKernel's own CLAUDE.md

**Files:**
- Modify: `src/SharedKernel/.claude/CLAUDE.md`

- [ ] **Step 1: Update every `ISerginUiDispatcher`/`RoutingSerginUiDispatcher` mention**

Read the file's `Sergin.SharedKernel.Presentation.Blazor`, `Sergin.SharedKernel.Hosts.WebUi`, and "Cross-cutting conventions" sections (they describe the dispatcher in detail — see the sections quoted in this session's context). Update each to say `ISerginSender`/`RoutingSerginSender`, note the new homes (`Application`/`Infrastructure`), and note that WebApi endpoints now go through it too, not just Blazor.

- [ ] **Step 2: Commit**

```bash
git add .claude/CLAUDE.md
git commit -m "Document ISerginSender's new home and WebApi consumption in CLAUDE.md"
```

---

### Task 6: Full SharedKernel build verification, then push for review

**Files:** none (verification only)

- [ ] **Step 1: Clean build**

Run: `dotnet build Sergin.SharedKernel.slnx`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 2: Confirm no leftover references to the old names**

Run (from `src/SharedKernel`): search for `ISerginUiDispatcher`, `RoutingSerginUiDispatcher`, `ScopedSerginUiDispatcher` across the repo (grep). Expected: no matches outside `.claude/CLAUDE.md`'s historical prose if any remains — fix any live code match found.

- [ ] **Step 3: Push branch — ask the user before pushing**

This is a push to `poursh/Sergin.SharedKernel`. Confirm with the user, then:

```bash
git push -u origin <branch-name>
```

Open a PR in that repo describing the `ISerginSender` relocation, linking the spec. Do not merge without the user's say-so.

---

## Phase B — UserAccess repo

> **Depends on Phase A's PR being at least open** (ideally merged) so the branch used here can reference a `Sergin.SharedKernel` commit that actually has `ISerginSender`. If Phase A isn't merged yet, point this branch's host-repo submodule pointer (Phase C, Task 12) at the Phase A feature-branch commit temporarily, and re-bump after merge — flag this explicitly when it happens, don't silently leave a branch pointer in place.

### Task 7: Update UserAccess WebApi endpoints

**Files:**
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.WebApi/Users/Endpoints/Create/CreateUserEndpoint.cs`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.WebApi/Users/Endpoints/GetOne/GetUserEndpoint.cs`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.WebApi/Users/Endpoints/GetList/GetUserListEndpoint.cs`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.WebApi/Users/Endpoints/DeactivateUser/DeactivateUserEndpoint.cs`

**Interfaces:**
- Consumes: `ISerginSender` (`Sergin.SharedKernel.Application.Dispatching`, from the bumped SharedKernel submodule)

- [ ] **Step 1: Read all four endpoint files**

Confirm the exact current shape — each is an `IEndpoint.MapEndpoint` implementation with a lambda or method taking `ISender sender` (or resolving it) and calling `sender.Send(command, cancellationToken)`.

- [ ] **Step 2: Swap `ISender` for `ISerginSender` in each**

For each of the 4 files: change the parameter type `ISender sender` to `ISerginSender sender`, change `sender.Send(` to `sender.SendAsync(`, add `using Sergin.SharedKernel.Application.Dispatching;` if the file doesn't already import `Sergin.SharedKernel.Application` globally (check `GlobalUsings.cs` first — the root CLAUDE.md notes `.Presentation.WebApi` projects import `MediatR`/`Sergin.SharedKernel.Presentation*` globally, not necessarily `Sergin.SharedKernel.Application.Dispatching` specifically).

- [ ] **Step 3: This project cannot build standalone**

`Sergin.UserAccess` is embed-only (no own `.slnx`) — it only compiles once mounted in a host. Do not attempt `dotnet build` from inside `src/Modules/UserAccess` directly; full verification happens in Phase C, Task 18. Read each edited file back once to sanity-check the mechanical rename (no leftover `ISender`, no leftover `.Send(` call).

- [ ] **Step 4: Commit**

```bash
git add Sergin.UserAccess.Presentation.WebApi/
git commit -m "Use ISerginSender instead of raw ISender in WebApi endpoints"
```

---

### Task 8: Update UserAccess Blazor pages

**Files:**
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.Blazor/Users/Pages/UserListPage.razor.cs`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.Blazor/Users/Pages/UserDetailPage.razor.cs`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.Blazor/Users/Pages/CreateUserPage.razor.cs`

**Interfaces:**
- Consumes: `ISerginSender` (`Sergin.SharedKernel.Application.Dispatching`)

- [ ] **Step 1: Read all three page code-behinds**

Confirm each has `[Inject] private ISerginUiDispatcher Dispatcher { get; set; } = default!;` and one or more `Dispatcher.SendAsync(...)`/`Dispatcher.SendListAsync(...)` call sites.

- [ ] **Step 2: Rename the injected property and call sites in each**

`ISerginUiDispatcher Dispatcher` → `ISerginSender Sender`; every `Dispatcher.SendAsync(` / `Dispatcher.SendListAsync(` → `Sender.SendAsync(` / `Sender.SendListAsync(`. Check `_Imports.razor` for a stale `Sergin.SharedKernel.Presentation.Blazor.Dispatching` using that may now need `Sergin.SharedKernel.Application.Dispatching` alongside it (the extension method stays in `Presentation.Blazor.Dispatching`, the interface moves to `Application.Dispatching` — both usings are needed).

- [ ] **Step 3: Read back each edited file** to confirm no leftover `Dispatcher` identifier or `ISerginUiDispatcher` type reference remains.

- [ ] **Step 4: Commit**

```bash
git add Sergin.UserAccess.Presentation.Blazor/Users/Pages/
git commit -m "Rename injected ISerginUiDispatcher to ISerginSender in page code-behinds"
```

---

### Task 9: Update UserAccess's add-feature skill copy and CLAUDE.md

**Files:**
- Modify: `src/Modules/UserAccess/.claude/skills/add-feature/SKILL.md`
- Modify: `src/Modules/UserAccess/.claude/CLAUDE.md`

- [ ] **Step 1: Update the skill's endpoint scaffolding template**

Read `SKILL.md`, find the WebApi endpoint template section (it currently scaffolds a constructor/lambda taking `ISender sender` and calling `sender.Send(...)`). Change the template to `ISerginSender sender` / `sender.SendAsync(...)`, matching Task 7's shape.

- [ ] **Step 2: Update any `ISerginUiDispatcher` mention in the module's own CLAUDE.md**, if present, to `ISerginSender`.

- [ ] **Step 3: Commit**

```bash
git add .claude/skills/add-feature/SKILL.md .claude/CLAUDE.md
git commit -m "Update add-feature scaffolding and docs for ISerginSender"
```

---

### Task 10: Push UserAccess branch for review

**Files:** none

- [ ] **Step 1: Push branch — ask the user before pushing**

Push to `poursh/Sergin.UserAccess`, open a PR describing the same rename, linking the spec. Do not merge without the user's say-so.

---

## Phase C — Host repo (Sergin.MeterMinder)

> **Depends on Phase A and Phase B.** Bump the host's submodule pointers (Task 12) to point at the SharedKernel/UserAccess commits from Phases A/B — the merged `main` commit if their PRs have landed, or the feature-branch commit (temporarily, with an explicit note to re-bump later) if not.

### Task 11: Create the host worktree and update submodules to the new commits

**Files:**
- Modify: `src/SharedKernel` (pointer)
- Modify: `src/Modules/UserAccess` (pointer)

- [ ] **Step 1: Confirm a worktree exists** (per this repo's `CLAUDE.md`: create one for work spanning multiple files/sessions) and run `git submodule update --init --recursive` inside it.

- [ ] **Step 2: Point both submodules at the Phase A/B commits**

```bash
cd src/SharedKernel && git fetch && git checkout <phase-a-commit-or-branch> && cd ../..
cd src/Modules/UserAccess && git fetch && git checkout <phase-b-commit-or-branch> && cd ../../..
```

- [ ] **Step 3: Stage the pointer bumps**

```bash
git add src/SharedKernel src/Modules/UserAccess
```

Do not commit yet — this commit lands at the end of Phase C (Task 18) alongside the host-repo edits, or immediately here if the host-repo edits will take a while and you want a checkpoint; either is fine, but if committed here, use a message that flags the pointers are provisional until Phase A/B PRs merge, per this session's `feedback_submodule_bump` convention only for *pure* pointer drift — a pointer bump paired with dependent host-code changes is not "pure," so don't auto-commit this silently; get confirmation same as any other host-repo commit in this plan.

---

### Task 12: Update DeviceManagement WebApi endpoints

**Files:**
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Devices/Endpoints/Create/CreateDeviceEndpoint.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Devices/Endpoints/GetOne/GetDeviceEndpoint.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Devices/Endpoints/GetList/GetDeviceListEndpoint.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Manufacturers/Endpoints/Create/CreateManufacturerEndpoint.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Manufacturers/Endpoints/GetOne/GetManufacturerEndpoint.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Manufacturers/Endpoints/GetList/GetManufacturerListEndpoint.cs`

**Interfaces:**
- Consumes: `ISerginSender` (`Sergin.SharedKernel.Application.Dispatching`)

- [ ] **Step 1: Read all six endpoint files.**

- [ ] **Step 2: Swap `ISender` for `ISerginSender`** in each, same mechanical change as Task 7: parameter type, `.Send(` → `.SendAsync(`, using statement if needed.

- [ ] **Step 3: Build the host solution**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: FAIL only at the 3 Blazor pages (Task 13) and the test file (Task 15) — not at any of these 6 endpoint files. If one of these 6 fails to build, fix it now before proceeding.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/
git commit -m "Use ISerginSender instead of raw ISender in DeviceManagement WebApi endpoints"
```

---

### Task 13: Update DeviceManagement Blazor pages

**Files:**
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Devices/Pages/DeviceListPage.razor.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Devices/Pages/DeviceDetailPage.razor.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Devices/Pages/CreateDevicePage.razor.cs`

**Interfaces:**
- Consumes: `ISerginSender` (`Sergin.SharedKernel.Application.Dispatching`)

- [ ] **Step 1: Read all three page code-behinds.**

- [ ] **Step 2: Rename the injected property and call sites**, same shape as Task 8: `ISerginUiDispatcher Dispatcher` → `ISerginSender Sender`, call sites updated, `_Imports.razor` checked for the `Application.Dispatching` using.

- [ ] **Step 3: Build**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: FAIL only at the test file (Task 15).

- [ ] **Step 4: Commit**

```bash
git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/
git commit -m "Rename injected ISerginUiDispatcher to ISerginSender in DeviceManagement page code-behinds"
```

---

### Task 14: Update the integration test

**Files:**
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Users/CreateAndGetUserTests.cs`

**Interfaces:**
- Consumes: `ISerginSender` (`Sergin.SharedKernel.Application.Dispatching`)

- [ ] **Step 1: Read the test file in full.**

Confirm it currently does `ISerginUiDispatcher dispatcher = factory.Services.GetRequiredService<ISerginUiDispatcher>();` (or resolves it via a scope) then `await dispatcher.SendAsync(new CreateUserCommand(...))`.

- [ ] **Step 2: Rename the resolved type and variable**

`ISerginUiDispatcher` → `ISerginSender`, `dispatcher` → `sender` (or whatever the existing local variable convention is — match it), call sites `dispatcher.SendAsync(` → `sender.SendAsync(`.

- [ ] **Step 3: Build the full solution**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: Build succeeded, 0 warnings, 0 errors, across every project.

- [ ] **Step 4: Run the integration suite**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`
Expected: All tests pass, including `CreateAndGetUserTests` (real Postgres round-trip via Testcontainers — requires Docker running) and `ModulePageRenderingTests`.

- [ ] **Step 5: Commit**

```bash
git add tests/Sergin.MeterMinder.IntegrationTests.All/Users/CreateAndGetUserTests.cs
git commit -m "Resolve ISerginSender instead of ISerginUiDispatcher in write-path integration test"
```

---

### Task 15: Update host repo's add-feature skill copy and CLAUDE.md

**Files:**
- Modify: `.claude/skills/add-feature/SKILL.md`
- Modify: `.claude/CLAUDE.md`

- [ ] **Step 1: Update the endpoint scaffolding template**, same change as Phase B Task 9 — `ISender` → `ISerginSender`, `.Send(` → `.SendAsync(`.

- [ ] **Step 2: Update `CLAUDE.md`'s dispatch documentation**

Update every section describing `ISerginUiDispatcher`/`RoutingSerginUiDispatcher` (the "Blazor UI conventions" section's dispatcher bullet, the `AddSerginBlazorApp`/`AddSerginCore` descriptions, the write-path test note) to describe `ISerginSender`, its new home (`Application`/`Infrastructure`), and that WebApi endpoints use it too now. Cross-reference `docs/superpowers/specs/2026-08-22-sergin-sender-design.md`.

- [ ] **Step 3: Commit**

```bash
git add .claude/skills/add-feature/SKILL.md .claude/CLAUDE.md
git commit -m "Document ISerginSender in host CLAUDE.md and add-feature scaffolding"
```

---

### Task 16: Finalize submodule pointers and full verification

**Files:**
- Modify: `src/SharedKernel` (pointer, if not already committed in Task 11)
- Modify: `src/Modules/UserAccess` (pointer, if not already committed in Task 11)

- [ ] **Step 1: If Phase A/B PRs have merged by now, re-point both submodules at their merged `main` commits**

```bash
cd src/SharedKernel && git fetch origin && git checkout origin/main && cd ../..
cd src/Modules/UserAccess && git fetch origin && git checkout origin/main && cd ../../..
git add src/SharedKernel src/Modules/UserAccess
```

If they haven't merged yet, leave the provisional pointers from Task 11 and note explicitly in the final summary to the user that a follow-up pointer-bump commit is needed once those PRs land.

- [ ] **Step 2: Full clean build + full test run**

```bash
dotnet build Sergin.MeterMinder.slnx
dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj
```

Expected: both succeed with 0 warnings/errors and all tests green.

- [ ] **Step 3: Commit the pointer bump (if updated in this step) and confirm final host-repo diff**

```bash
git status
git add src/SharedKernel src/Modules/UserAccess
git commit -m "Bump SharedKernel and UserAccess submodule pointers to ISerginSender commits"
```

- [ ] **Step 4: Report to the user**

Summarize: what merged where, what's still pending (any unmerged Phase A/B PRs), and confirm before pushing the host repo's own branch/PR.

---

## Self-Review Notes

- **Spec coverage:** Every numbered decision in the spec (§Decisions 1–6) maps to a task — scope/consumer surface (Tasks 7, 8, 12, 13), granularity unchanged (no task touches `DispatchModeOptions`' shape), layer placement (Tasks 1–2), shared implementation (Task 2 Step 2, unchanged scope-opening logic), registration (Task 4), csproj references (Tasks 2 Step 1, 3 Step 1). Non-goals are respected: no task touches `DeviceGrpcService`, transport, error mapping, or `Sergin.SharedKernel.Presentation.Grpc`'s name.
- **Placeholder scan:** No task in this plan uses "add appropriate handling" or unshown code — every code-bearing step includes the literal file content or diff needed.
- **Type consistency:** `ISerginSender.SendAsync<TResponse>` (Task 1) matches every call site in Tasks 7–14. `RoutingSerginSender` (Task 2) implements exactly that signature. `SerginSenderExtensions.SendListAsync<TItem>` (Task 3) is declared as an extension on `ISerginSender`, matching what Tasks 8/13's page call sites invoke.
