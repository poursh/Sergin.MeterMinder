# Dual-Mode Dispatch Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `ScopedSerginUiDispatcher` with a `RoutingSerginUiDispatcher` that can send a request either in-process via MediatR (Local) or over gRPC to another process (Remote), switchable per module by config — with zero change to any Blazor page's call site — and prove the mechanism end-to-end through one real reference slice: DeviceManagement's `GetDeviceById` query.

**Architecture:** Core routing infra (`DispatchModeOptions`, `IDispatchRouteResolver`, `RoutingSerginUiDispatcher`, a shared `IRemoteInvoker<TRequest,TResponse>` contract + `ErrorReply` proto mapping) lands in the `SharedKernel` submodule, generically usable by any module. One vertical slice — DeviceManagement's `GetDeviceById` — gets a real contract-first gRPC client invoker and server-side service, proven correct by a component test that runs a real Kestrel gRPC server on a loopback port and compares its result against the same handler called in-process.

**Tech Stack:** .NET 10, MediatR 12.5.0, ErrorOr 2.1.1, Grpc.AspNetCore / Grpc.Net.Client / Google.Protobuf / Grpc.Tools (new), xUnit.

**Spec:** `docs/superpowers/specs/2026-08-21-dispatch-contract-design.md` (as amended — see its §3 "Module-side gRPC adapter" and decision 8: MediatR/`ISender` stays the single gateway into Application regardless of transport). Executors should read the spec's §§1–4 and §6 before starting; this plan implements them.

## Global Constraints

- `TreatWarningsAsErrors=true`, `AnalysisMode=All`, SonarAnalyzer.CSharp, nullable + implicit usings enabled — every new file must build clean, first try, in both `Sergin.MeterMinder.slnx` and (for SharedKernel files) `Sergin.SharedKernel.slnx`.
- Central Package Management is on in both repos: `PackageReference` items carry **no** `Version` attribute; every version lives in that repo's `Directory.Packages.props`, alphabetical. Add new packages with `dotnet add package <Name>` from the project directory — with CPM active this resolves the current stable version and writes it to `Directory.Packages.props` automatically, leaving the `.csproj` reference version-less. Do not hand-type a version number.
- `src/SharedKernel/` is a **separate git repository**, mounted as a submodule. Tasks that touch it work with `git` from inside `src/SharedKernel/` (its own commits, its own history) — never `git add`/`git commit` a `src/SharedKernel/...` path from the outer repo's root. The outer repo only records a new submodule *pointer*, done in Task 4.
- Follow existing repo conventions exactly: `internal sealed class` for handlers/services, `public sealed record` for DTOs/commands, one `GlobalUsings.cs` per new project (`global using ErrorOr;` / `global using MediatR;` where those types are used unqualified elsewhere), namespace mirrors folder path.
- This is multi-file, multi-session work touching two repos — per root `CLAUDE.md`, execute this plan inside a git worktree with submodules initialized (`git submodule update --init --recursive`) before building. `using-git-worktrees`/`subagent-driven-development` handle this at execution time; this plan does not repeat that setup.

---

## File Structure

**`src/SharedKernel/` (separate repo — Tasks 1–3):**
- `Sergin.SharedKernel.Hosts/Dispatching/DispatchModeOptions.cs` — new
- `Sergin.SharedKernel.Hosts/Dispatching/DispatchModeOptionsValidator.cs` — new
- `Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs` — modified (bind + validate `DispatchModeOptions`)
- `Sergin.SharedKernel.Presentation.Grpc/Sergin.SharedKernel.Presentation.Grpc.csproj` — new project
- `Sergin.SharedKernel.Presentation.Grpc/GlobalUsings.cs` — new
- `Sergin.SharedKernel.Presentation.Grpc/Protos/error.proto` — new
- `Sergin.SharedKernel.Presentation.Grpc/Dispatching/IRemoteInvoker.cs` — new
- `Sergin.SharedKernel.Presentation.Grpc/Errors/ErrorReplyExtensions.cs` — new
- `Sergin.SharedKernel.Presentation.Blazor/Sergin.SharedKernel.Presentation.Blazor.csproj` — modified (add `ProjectReference` to the new Grpc project)
- `Sergin.SharedKernel.Presentation.Blazor/Dispatching/IDispatchRouteResolver.cs` — new
- `Sergin.SharedKernel.Presentation.Blazor/Dispatching/ModuleDispatchRouteResolver.cs` — new
- `Sergin.SharedKernel.Presentation.Blazor/Dispatching/RoutingSerginUiDispatcher.cs` — new
- `Sergin.SharedKernel.Presentation.Blazor/Dispatching/ScopedSerginUiDispatcher.cs` — **deleted**
- `Sergin.SharedKernel.Presentation.Blazor/SerginBlazorKitExtensions.cs` — modified (swap dispatcher registration)
- `Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs` — modified (register `IDispatchRouteResolver`)
- `Sergin.SharedKernel.slnx` — modified (add the new project)
- `Directory.Packages.props` — modified (new Grpc/Protobuf package versions)

**`Sergin.MeterMinder` outer repo (Tasks 4–7):**
- `.gitmodules` pointer / submodule commit — bumped (Task 4)
- `src/Hosts/Sergin.MeterMinder.Hosts.All/appsettings.json` — modified (`Sergin:Dispatch:Modules`)
- `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj` — new project
- `.../GlobalUsings.cs` — new
- `.../Protos/devices.proto` — new
- `.../Devices/GetDeviceByIdGrpcInvoker.cs` — new (client-side `IRemoteInvoker`)
- `.../Devices/DeviceGrpcService.cs` — new (server-side, `ISender.Send(...)`)
- `Sergin.MeterMinder.slnx` — modified (add the new project)
- `Directory.Packages.props` (root) — modified (new Grpc/Protobuf package versions)
- `tests/Sergin.MeterMinder.IntegrationTests.All/Dispatching/RoutingSerginUiDispatcherTests.cs` — new
- `tests/Sergin.MeterMinder.IntegrationTests.All/Devices/DeviceGrpcRoundTripTests.cs` — new
- `tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj` — modified (new `ProjectReference`s/`PackageReference`s for the round-trip test)

**Not in this plan** (deliberate follow-up, per the spec's own Non-goals/Open follow-ups): a second real deployable host process for DeviceManagement, `AddGrpcClient`/service-discovery wiring, turning `Sergin:Dispatch:Modules:dm` to `Remote` in the real running host, `CreateDevice`/`GetDeviceList` gRPC slices, UserAccess's gRPC slice, and real authentication. Building any of those now, with no second process that actually needs them, would be dead code — the point of this plan is the mechanism and one proof slice, not a deployment.

---

### Task 1: `DispatchModeOptions` — per-module Local/Remote config

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Hosts/Dispatching/DispatchModeOptions.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Hosts/Dispatching/DispatchModeOptionsValidator.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs`
- Test: `src/SharedKernel/Sergin.SharedKernel.Hosts.Tests/` does not exist — this task is proven by `dotnet build` plus Task 4's regression run, since SharedKernel has no `[Fact]`s of its own (its own `.claude/CLAUDE.md` confirms this — `Sergin.SharedKernel.IntegrationTests` is infrastructure only). Verify by building `Sergin.SharedKernel.slnx` and by a throwaway `Console.WriteLine`-free smoke check described in Step 4.

**Interfaces:**
- Produces: `DispatchModeOptions.Modules : Dictionary<string, DispatchMode>` (mutable, config-binder-friendly — matches `DevUserOptions`'s convention of settable properties with array/collection defaults, not `init`-only). `DispatchMode` enum: `Local`, `Remote`. `DispatchModeOptions.SectionName = "Dispatch"`. Later tasks bind `Sergin:Dispatch:Modules:<schema>`.

- [ ] **Step 1: Write `DispatchModeOptions`**

```csharp
namespace Sergin.SharedKernel.Hosts.Dispatching;

public sealed class DispatchModeOptions
{
    public const string SectionName = "Dispatch";

    public Dictionary<string, DispatchMode> Modules { get; set; } = [];
}

public enum DispatchMode
{
    Local,
    Remote,
}
```

- [ ] **Step 2: Write `DispatchModeOptionsValidator`**

```csharp
using Microsoft.Extensions.Options;

namespace Sergin.SharedKernel.Hosts.Dispatching;

/// <summary>
/// Fails startup naming exactly which module schema has no Sergin:Dispatch:Modules entry, rather than
/// letting an unlisted module silently fall through to a default. Constructed with a closure over the
/// registered modules' schemas by AddSerginCore, matching how SerginUiModuleCatalog is built in
/// AddSerginBlazorApp — the collection isn't itself resolved from DI.
/// </summary>
internal sealed class DispatchModeOptionsValidator(IReadOnlyCollection<string> requiredSchemas)
    : IValidateOptions<DispatchModeOptions>
{
    public ValidateOptionsResult Validate(string? name, DispatchModeOptions options)
    {
        string[] missing = [.. requiredSchemas.Where(schema => !options.Modules.ContainsKey(schema))];

        return missing.Length == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Sergin:{SerginCoreExtensions.SectionName}:{DispatchModeOptions.SectionName}:Modules is missing "
                + $"an entry for: {string.Join(", ", missing)}.");
    }
}
```

- [ ] **Step 3: Wire binding + validation into `AddSerginCore`**

In `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs`, add `using Microsoft.Extensions.Options;` and `using Sergin.SharedKernel.Hosts.Dispatching;` to the top, then insert this block right after the existing `foreach (ISerginModule module in modules) { module.AddServices(...); }` loop and before `return serginSection;`:

```csharp
        IReadOnlyCollection<string> schemas = [.. modules.Select(module => module.Schema)];

        builder.Services.AddOptions<DispatchModeOptions>()
            .Bind(serginSection.GetSection(DispatchModeOptions.SectionName))
            .ValidateOnStart();

        builder.Services.AddSingleton<IValidateOptions<DispatchModeOptions>>(
            _ => new DispatchModeOptionsValidator(schemas));
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build src/SharedKernel/Sergin.SharedKernel.slnx` (from the outer repo root, or `dotnet build Sergin.SharedKernel.slnx` from inside `src/SharedKernel/`)
Expected: builds clean. There is nothing to *run* yet — `AddSerginCore` isn't called by anything until a host does, which Task 4 exercises. This step only proves the new files compile and the validator/options wiring type-checks against `SerginCoreExtensions`'s existing signature.

- [ ] **Step 5: Commit — inside the submodule**

```bash
cd src/SharedKernel
git add Sergin.SharedKernel.Hosts/Dispatching/DispatchModeOptions.cs Sergin.SharedKernel.Hosts/Dispatching/DispatchModeOptionsValidator.cs Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs
git commit -m "Add DispatchModeOptions: per-module Local/Remote dispatch config"
cd ../..
```

---

### Task 2: `Sergin.SharedKernel.Presentation.Grpc` — shared contract types

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Grpc/Sergin.SharedKernel.Presentation.Grpc.csproj`
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Grpc/GlobalUsings.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Grpc/Protos/error.proto`
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Grpc/Dispatching/IRemoteInvoker.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Grpc/Errors/ErrorReplyExtensions.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.slnx`
- Modify: `src/SharedKernel/Directory.Packages.props`

**Interfaces:**
- Consumes: `ErrorOr.Error`/`ErrorOr.ErrorType` (package), `MediatR.IRequest<TResponse>` (package).
- Produces: `IRemoteInvoker<TRequest,TResponse>.InvokeAsync(TRequest, CancellationToken) : Task<ErrorOr<TResponse>>`; generated `ErrorReply { string Code; string Description; ProtoErrorType Type; }` and `ProtoErrorType` enum in namespace `Sergin.SharedKernel.Presentation.Grpc`; extension methods `ErrorReply.ToErrorOr<T>()` and `Error.ToErrorReply()` in `Sergin.SharedKernel.Presentation.Grpc.Errors`. Task 5's module-level proto imports `error.proto` from this project's `Protos/` folder and references this project for the compiled types — it must not redeclare `error.proto` as its own compile target (see Task 5, Step 2).

- [ ] **Step 1: Create the project file**

```xml
<!-- src/SharedKernel/Sergin.SharedKernel.Presentation.Grpc/Sergin.SharedKernel.Presentation.Grpc.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

	<ItemGroup>
		<PackageReference Include="ErrorOr" />
		<PackageReference Include="Google.Protobuf" />
		<PackageReference Include="Grpc.Net.Client" />
		<PackageReference Include="Grpc.Tools">
			<PrivateAssets>all</PrivateAssets>
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
		</PackageReference>
	</ItemGroup>

	<ItemGroup>
		<Protobuf Include="Protos\error.proto" GrpcServices="None" />
	</ItemGroup>

</Project>
```

- [ ] **Step 2: Add the new packages centrally**

```bash
cd src/SharedKernel/Sergin.SharedKernel.Presentation.Grpc
dotnet add package ErrorOr
dotnet add package Google.Protobuf
dotnet add package Grpc.Net.Client
dotnet add package Grpc.Tools
cd ../../..
```

Then open `src/SharedKernel/Directory.Packages.props` and confirm the four `<PackageVersion>` lines landed alphabetically (they should already be sorted correctly if `dotnet add package` appended and you re-sort by hand): `Google.Protobuf` and `Grpc.Net.Client`/`Grpc.Tools` go between `FluentValidation` and `MediatR`; `ErrorOr` already exists (the command is a no-op / confirms the existing entry).

- [ ] **Step 3: Write `GlobalUsings.cs`**

```csharp
global using ErrorOr;
global using MediatR;
```

- [ ] **Step 4: Write `error.proto`**

```proto
// src/SharedKernel/Sergin.SharedKernel.Presentation.Grpc/Protos/error.proto
syntax = "proto3";

package sergin.shared;

option csharp_namespace = "Sergin.SharedKernel.Presentation.Grpc";

// Numeric values are chosen to align 1:1 with ErrorOr.ErrorType (verified against the installed
// ErrorOr 2.1.1 package: Failure=0, Unexpected=1, Validation=2, Conflict=3, NotFound=4,
// Unauthorized=5, Forbidden=6) so the C# mapping in ErrorReplyExtensions is a plain numeric cast,
// not a switch. Named ProtoErrorType, not ErrorType, to avoid colliding with ErrorOr's own type
// of the same name once both are in scope in the same file.
enum ProtoErrorType {
  FAILURE = 0;
  UNEXPECTED = 1;
  VALIDATION = 2;
  CONFLICT = 3;
  NOT_FOUND = 4;
  UNAUTHORIZED = 5;
  FORBIDDEN = 6;
}

message ErrorReply {
  string code = 1;
  string description = 2;
  ProtoErrorType type = 3;
}
```

- [ ] **Step 5: Write `IRemoteInvoker.cs`**

```csharp
namespace Sergin.SharedKernel.Presentation.Grpc.Dispatching;

/// <summary>
/// Client-side stub for a request whose handler runs in another process. Implemented once per feature
/// (one per rpc method), the same "one interface per feature" shape as every other Sergin query
/// repository interface. Not a second entry point into Application — see DeviceGrpcService (the
/// server-side counterpart, added in the DeviceManagement module) for why: it still ends in
/// ISender.Send.
/// </summary>
public interface IRemoteInvoker<TRequest, TResponse>
    where TRequest : IRequest<ErrorOr<TResponse>>
{
    Task<ErrorOr<TResponse>> InvokeAsync(TRequest request, CancellationToken cancellationToken);
}
```

- [ ] **Step 6: Write `ErrorReplyExtensions.cs`**

```csharp
namespace Sergin.SharedKernel.Presentation.Grpc.Errors;

public static class ErrorReplyExtensions
{
    public static ErrorOr<T> ToErrorOr<T>(this ErrorReply reply) =>
        Error.Custom((int)reply.Type, reply.Code, reply.Description);

    public static ErrorReply ToErrorReply(this Error error) =>
        new()
        {
            Code = error.Code,
            Description = error.Description,
            Type = (ProtoErrorType)(int)error.Type,
        };
}
```

- [ ] **Step 7: Register the project in the SharedKernel solution**

In `src/SharedKernel/Sergin.SharedKernel.slnx`, add a new entry under the existing `/Presentation/` folder (alongside `Sergin.SharedKernel.Presentation.Blazor` and `.WebApi`):

```xml
  <Folder Name="/Presentation/">
    <Project Path="Sergin.SharedKernel.Presentation.Blazor/Sergin.SharedKernel.Presentation.Blazor.csproj" />
    <Project Path="Sergin.SharedKernel.Presentation.Grpc/Sergin.SharedKernel.Presentation.Grpc.csproj" />
    <Project Path="Sergin.SharedKernel.Presentation.WebApi/Sergin.SharedKernel.Presentation.WebApi.csproj" />
    <Project Path="Sergin.SharedKernel.Presentation/Sergin.SharedKernel.Presentation.csproj" />
  </Folder>
```

- [ ] **Step 8: Build to verify**

Run: `dotnet build src/SharedKernel/Sergin.SharedKernel.slnx`
Expected: builds clean, including generated `ErrorReply`/`ProtoErrorType` from `error.proto` (check `src/SharedKernel/Sergin.SharedKernel.Presentation.Grpc/obj/Debug/net10.0/Protos/Error.cs` exists after build, confirming codegen ran).

- [ ] **Step 9: Commit — inside the submodule**

```bash
cd src/SharedKernel
git add Sergin.SharedKernel.Presentation.Grpc Sergin.SharedKernel.slnx Directory.Packages.props
git commit -m "Add Sergin.SharedKernel.Presentation.Grpc: shared IRemoteInvoker + Error contract"
cd ../..
```

---

### Task 3: `RoutingSerginUiDispatcher` — replaces `ScopedSerginUiDispatcher`

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/IDispatchRouteResolver.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/ModuleDispatchRouteResolver.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/RoutingSerginUiDispatcher.cs`
- Delete: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/ScopedSerginUiDispatcher.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Sergin.SharedKernel.Presentation.Blazor.csproj`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/SerginBlazorKitExtensions.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs`

**Interfaces:**
- Consumes: `DispatchModeOptions` (Task 1), `IRemoteInvoker<,>` (Task 2), `IUserContext`/`IUserContextFactory` (existing, `Sergin.SharedKernel.Application.Securities.Users`), `RequiredPermissionsAttribute` (existing, `Sergin.SharedKernel.Application.Securities.Authorization`).
- Produces: `ISerginUiDispatcher` implementation registered as the app-wide singleton (unchanged interface, unchanged call sites — `ISerginUiDispatcher.SendAsync<TResponse>(IRequest<ErrorOr<TResponse>>, CancellationToken)` is untouched). `IDispatchRouteResolver.IsRemote(Type requestType) : bool`, consumed only by `RoutingSerginUiDispatcher`. Test harnesses (Task 6, Task 7) construct `RoutingSerginUiDispatcher` directly against hand-built `IServiceProvider`s.

- [ ] **Step 1: Add the `ProjectReference` to the new Grpc project**

In `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Sergin.SharedKernel.Presentation.Blazor.csproj`, add inside the existing `<ItemGroup>` of `ProjectReference`s:

```xml
		<ProjectReference Include="..\Sergin.SharedKernel.Presentation.Grpc\Sergin.SharedKernel.Presentation.Grpc.csproj" />
```

- [ ] **Step 2: Write `IDispatchRouteResolver.cs`**

```csharp
namespace Sergin.SharedKernel.Presentation.Blazor.Dispatching;

public interface IDispatchRouteResolver
{
    bool IsRemote(Type requestType);
}
```

- [ ] **Step 3: Write `ModuleDispatchRouteResolver.cs`**

```csharp
using System.Reflection;
using Microsoft.Extensions.Options;
using Sergin.SharedKernel.Hosts.Dispatching;

namespace Sergin.SharedKernel.Presentation.Blazor.Dispatching;

/// <summary>
/// Maps a request type to its owning module's schema via the request's declaring assembly (the same
/// reflection style UseSerginWebUiAsync's @page prefix guard already uses), then looks that schema up
/// in DispatchModeOptions. Constructed with a closure over the registered modules by whichever host
/// bootstrap calls AddSerginBlazorApp — not resolved from DI, matching SerginUiModuleCatalog's and
/// DispatchModeOptionsValidator's precedent.
/// </summary>
internal sealed class ModuleDispatchRouteResolver(
    IReadOnlyDictionary<Assembly, string> schemaByAssembly,
    IOptions<DispatchModeOptions> options) : IDispatchRouteResolver
{
    public bool IsRemote(Type requestType)
    {
        if (!schemaByAssembly.TryGetValue(requestType.Assembly, out string? schema))
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
}
```

- [ ] **Step 4: Write `RoutingSerginUiDispatcher.cs`**

```csharp
using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Presentation.Grpc.Dispatching;

namespace Sergin.SharedKernel.Presentation.Blazor.Dispatching;

/// <summary>
/// Replaces ScopedSerginUiDispatcher. Every send opens one fresh DI scope (same reasoning as before:
/// Blazor Server "scoped" is the whole SignalR circuit, not a request) and runs a permission check
/// against IUserContext before branching Local (ISender.Send, in-process) or Remote (IRemoteInvoker,
/// over gRPC). The permission check runs unconditionally, not just for Remote: Local mode already
/// re-checks it inside MediatR's PermissionCheckPipelineBehavior, so this is a deliberate, cheap
/// redundancy — see spec §5 — not a correctness gap for Local.
/// </summary>
internal sealed class RoutingSerginUiDispatcher(
    IServiceScopeFactory scopeFactory,
    IDispatchRouteResolver routeResolver) : ISerginUiDispatcher
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

- [ ] **Step 5: Delete `ScopedSerginUiDispatcher.cs`**

```bash
rm src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/ScopedSerginUiDispatcher.cs
```

- [ ] **Step 6: Swap the dispatcher registration in `SerginBlazorKitExtensions.cs`**

Change:
```csharp
        services.AddSingleton<ISerginUiDispatcher, ScopedSerginUiDispatcher>();
```
to:
```csharp
        services.AddSingleton<ISerginUiDispatcher, RoutingSerginUiDispatcher>();
```

- [ ] **Step 7: Register `IDispatchRouteResolver` in `AddSerginBlazorApp`**

In `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs`, add `using System.Reflection;` (already present) and `using Microsoft.Extensions.Options;` (already present) and `using Sergin.SharedKernel.Hosts.Dispatching;` and `using Sergin.SharedKernel.Presentation.Blazor.Dispatching;`, then insert this line right after `builder.AddSerginCore(modules);`:

```csharp
        builder.AddSerginCore(modules);

        builder.Services.AddSingleton<IDispatchRouteResolver>(p => new ModuleDispatchRouteResolver(
            modules.ToDictionary(module => module.ApplicationAssembly, module => module.Schema),
            p.GetRequiredService<IOptions<DispatchModeOptions>>()));
```

- [ ] **Step 8: Build to verify**

Run: `dotnet build src/SharedKernel/Sergin.SharedKernel.slnx`
Expected: builds clean. This is the point where a wrong DI wiring (missing `IDispatchRouteResolver`/`IUserContext` registration) would surface as a *runtime* `InvalidOperationException` at first resolution, not a compile error — Task 4's regression run is what actually proves the wiring, not this build.

- [ ] **Step 9: Commit — inside the submodule**

```bash
cd src/SharedKernel
git add Sergin.SharedKernel.Presentation.Blazor Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs
git commit -m "Replace ScopedSerginUiDispatcher with RoutingSerginUiDispatcher"
cd ../..
```

---

### Task 4: Bump the submodule pointer; wire the real host; regression-prove the swap

**Files:**
- Modify: `src/SharedKernel` (submodule pointer — a `git add`/`git commit` in the *outer* repo, recording the new commit from Task 3's Step 9)
- Modify: `src/Hosts/Sergin.MeterMinder.Hosts.All/appsettings.json`
- Test: existing `tests/Sergin.MeterMinder.IntegrationTests.All/**`

**Interfaces:**
- Consumes: everything from Tasks 1–3, now resolvable because the outer repo's `Sergin.MeterMinder.slnx` picks up the new submodule commit.
- Produces: nothing new — this task's entire job is proving the swap is behavior-preserving for a host where every module stays `Local`.

- [ ] **Step 1: Bump the submodule pointer**

From the outer repo root:
```bash
git add src/SharedKernel
git status
```
Expected: `git status` shows `src/SharedKernel` as modified (new commit hash) — this stages the pointer, not file contents.

- [ ] **Step 2: Add `Sergin:Dispatch:Modules` to the real host's config**

In `src/Hosts/Sergin.MeterMinder.Hosts.All/appsettings.json`, add a `Dispatch` section as a sibling of `DevUser` inside `Sergin`:

```json
  "Sergin": {
    "ApplicationName": "Meter Minder",
    "ConnectionStrings": {
      "Database": ""
    },
    "Dispatch": {
      "Modules": {
        "dm": "Local",
        "ua": "Local"
      }
    },
    "DevUser": {
```

(Keep the existing `DevUser` block unchanged below it.) Both modules stay `Local` — nothing in this plan stands up a second real process, so `Remote` would fail at first dispatch with no `IRemoteInvoker` registered. `DispatchModeOptionsValidator` (Task 1) requires an entry for *every* registered module's schema, so both `dm` and `ua` must be listed even though only `dm` gets a gRPC slice in Task 5.

- [ ] **Step 3: Build the outer solution**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: builds clean, now resolving `RoutingSerginUiDispatcher` etc. through the bumped submodule commit.

- [ ] **Step 4: Run the full existing integration suite — this is the regression proof**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`
Expected: **all existing tests still pass** — `ModulePageRenderingTests` (both modules' pages still render, home slot, nav) and `CreateAndGetUserTests` (the one write-path test, dispatching `CreateUserCommand` through `ISerginUiDispatcher` exactly as before). This is the proof that swapping `ScopedSerginUiDispatcher` → `RoutingSerginUiDispatcher` — now doing a permission check and a route lookup on every call — is behavior-preserving when every module is `Local`. If `CreateAndGetUserTests` fails with a `Forbidden` error, the permission check in Step 4 of Task 3 is wrong (`CreateUserCommand` carries no `[RequiredPermissions]`, so `attribute is not null` should be `false` and the check should no-op). If any test throws `InvalidOperationException: No dispatch mode configured for module schema...`, Step 2 above is missing an entry.

- [ ] **Step 5: Commit — outer repo**

```bash
git add src/SharedKernel src/Hosts/Sergin.MeterMinder.Hosts.All/appsettings.json
git commit -m "Bump SharedKernel to the RoutingSerginUiDispatcher commit; configure Local dispatch for both modules"
```

---

### Task 5: DeviceManagement's `GetDeviceById` gRPC contract

**Files:**
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj`
- Create: `.../GlobalUsings.cs`
- Create: `.../Protos/devices.proto`
- Create: `.../Devices/GetDeviceByIdGrpcInvoker.cs`
- Create: `.../Devices/DeviceGrpcService.cs`
- Modify: `Sergin.MeterMinder.slnx`
- Modify: `Directory.Packages.props` (root)

**Interfaces:**
- Consumes: `GetDeviceByIdQueryCommand(Guid Id) : IQuery<DeviceQueryResponse>` and `DeviceQueryResponse(Guid Id, string DeviceId, Guid ManufacturerId)` from `Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne` (existing, unchanged); `IRemoteInvoker<TRequest,TResponse>` and `ErrorReplyExtensions` (Task 2); `ISender` (MediatR, existing).
- Produces: `GetDeviceByIdGrpcInvoker : IRemoteInvoker<GetDeviceByIdQueryCommand, DeviceQueryResponse>` (client-side; Task 7 constructs one directly against a real `DeviceServiceClient`). `DeviceGrpcService : DeviceService.DeviceServiceBase` (server-side; Task 7 maps one on a real Kestrel app via `app.MapGrpcService<DeviceGrpcService>()`).

- [ ] **Step 1: Create the project file**

```xml
<!-- src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

	<ItemGroup>
		<FrameworkReference Include="Microsoft.AspNetCore.App" />
	</ItemGroup>

	<ItemGroup>
		<PackageReference Include="Grpc.AspNetCore" />
	</ItemGroup>

	<ItemGroup>
		<ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Application\Sergin.MeterMinder.DeviceManagement.Application.csproj" />
		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Grpc\Sergin.SharedKernel.Presentation.Grpc.csproj" />
	</ItemGroup>

	<ItemGroup>
		<Protobuf Include="Protos\devices.proto" GrpcServices="Both" AdditionalImportDirs="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Grpc\Protos" />
	</ItemGroup>

</Project>
```

`Grpc.AspNetCore` pulls in `Grpc.Tools` transitively (it depends on it for build-time codegen), so it is not listed again here — unlike the SharedKernel project in Task 2, which has no ASP.NET Core dependency and needs `Grpc.Tools` named explicitly.

**Do not** add `<Protobuf Include="...\error.proto" ...>` here — `AdditionalImportDirs` only lets `protoc` *resolve* the `import "error.proto";` statement inside `devices.proto`; it must not also be a compile target in this project, or the generated `ErrorReply`/`ProtoErrorType` types would be defined twice (once in `Sergin.SharedKernel.Presentation.Grpc.dll`, once here) and any project referencing both would get an ambiguous-type compile error.

- [ ] **Step 2: Add the new package centrally**

```bash
cd src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc
dotnet add package Grpc.AspNetCore
cd ../../../..
```

Confirm the entry landed alphabetically in the root `Directory.Packages.props` (between `Google.Protobuf`/`Grpc.Net.Client`/`Grpc.Tools` added by Task 2's SharedKernel-side `dotnet add package` runs — note the **root** `Directory.Packages.props` is a *different file* from the SharedKernel one; this task needs its own `dotnet add package Grpc.AspNetCore` run from a project *inside* the outer repo, which it already has via Step 1's `ProjectReference` chain. If `Google.Protobuf`/`Grpc.Net.Client`/`Grpc.Tools` are not yet present in the *root* `Directory.Packages.props` (they were only added to `src/SharedKernel/Directory.Packages.props` in Task 2), add them here too the same way:
```bash
dotnet add package Google.Protobuf
dotnet add package Grpc.Net.Client
dotnet add package Grpc.Tools
```
run from the same project directory — needed because this project's own `<Protobuf>` compile step and the generated code's use of `Google.Protobuf.IMessage` etc. require these on the outer repo's own centrally-managed version list, independent of the submodule's list.

- [ ] **Step 3: Write `GlobalUsings.cs`**

```csharp
global using ErrorOr;
global using MediatR;
```

- [ ] **Step 4: Write `devices.proto`**

```proto
// src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Protos/devices.proto
syntax = "proto3";

package sergin.devicemanagement;

option csharp_namespace = "Sergin.MeterMinder.DeviceManagement.Presentation.Grpc";

import "error.proto";

service DeviceService {
  rpc GetDeviceById (GetDeviceByIdRequest) returns (GetDeviceByIdReply);
}

message GetDeviceByIdRequest {
  string id = 1;
}

message GetDeviceByIdReply {
  oneof result {
    DeviceData success = 1;
    sergin.shared.ErrorReply error = 2;
  }
}

// Named DeviceData, not DeviceQueryResponse: the Application-layer type with that exact name
// (Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne.DeviceQueryResponse) is
// referenced in the same files as this generated type — an identical name would be ambiguous.
message DeviceData {
  string id = 1;
  string device_id = 2;
  string manufacturer_id = 3;
}
```

- [ ] **Step 5: Write the client-side invoker**

```csharp
// src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Devices/GetDeviceByIdGrpcInvoker.cs
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.SharedKernel.Presentation.Grpc.Dispatching;
using Sergin.SharedKernel.Presentation.Grpc.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Devices;

internal sealed class GetDeviceByIdGrpcInvoker(DeviceService.DeviceServiceClient client)
    : IRemoteInvoker<GetDeviceByIdQueryCommand, DeviceQueryResponse>
{
    public async Task<ErrorOr<DeviceQueryResponse>> InvokeAsync(
        GetDeviceByIdQueryCommand request, CancellationToken cancellationToken)
    {
        GetDeviceByIdReply reply = await client.GetDeviceByIdAsync(
            new GetDeviceByIdRequest { Id = request.Id.ToString() },
            cancellationToken: cancellationToken);

        return reply.ResultCase == GetDeviceByIdReply.ResultOneofCase.Error
            ? reply.Error.ToErrorOr<DeviceQueryResponse>()
            : new DeviceQueryResponse(
                Guid.Parse(reply.Success.Id),
                reply.Success.DeviceId,
                Guid.Parse(reply.Success.ManufacturerId));
    }
}
```

- [ ] **Step 6: Write the server-side service**

```csharp
// src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Devices/DeviceGrpcService.cs
using Grpc.Core;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.SharedKernel.Presentation.Grpc.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Devices;

/// <summary>
/// Runs in the module's own process when Remote. Structurally the same shape as GetDeviceEndpoint
/// (Presentation.WebApi) — proto request in, ISender.Send, ErrorOr out — just a different transport.
/// Same MediatR pipeline (PermissionCheckPipelineBehavior, ValidationPipelineBehavior) runs here as it
/// does for every other ISender.Send call in the process this service lives in.
/// </summary>
internal sealed class DeviceGrpcService(ISender sender) : DeviceService.DeviceServiceBase
{
    public override async Task<GetDeviceByIdReply> GetDeviceById(
        GetDeviceByIdRequest request, ServerCallContext context)
    {
        ErrorOr<DeviceQueryResponse> result = await sender.Send(
            new GetDeviceByIdQueryCommand(Guid.Parse(request.Id)), context.CancellationToken);

        return result.Match(
            response => new GetDeviceByIdReply
            {
                Success = new DeviceData
                {
                    Id = response.Id.ToString(),
                    DeviceId = response.DeviceId,
                    ManufacturerId = response.ManufacturerId.ToString(),
                },
            },
            errors => new GetDeviceByIdReply { Error = errors[0].ToErrorReply() });
    }
}
```

- [ ] **Step 7: Register the project in the outer solution**

In `Sergin.MeterMinder.slnx`, add a new entry under `/src/Modules/DeviceManagement/Presentation/`:

```xml
  <Folder Name="/src/Modules/DeviceManagement/Presentation/">
    <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.csproj" />
    <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.csproj" />
    <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj" />
  </Folder>
```

- [ ] **Step 8: Build to verify**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: builds clean, including `devices.pb.cs`/`devices.grpc.cs` codegen resolving `ErrorReply`/`ProtoErrorType` from the referenced `Sergin.SharedKernel.Presentation.Grpc.dll` rather than regenerating them.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc Sergin.MeterMinder.slnx Directory.Packages.props
git commit -m "Add DeviceManagement GetDeviceById gRPC contract (client invoker + server service)"
```

---

### Task 6: Permission-check unit tests for `RoutingSerginUiDispatcher`

**Files:**
- Create: `tests/Sergin.MeterMinder.IntegrationTests.All/Dispatching/RoutingSerginUiDispatcherTests.cs`

**Interfaces:**
- Consumes: `AddSerginBlazorKit()` (existing public extension method, `Sergin.SharedKernel.Presentation.Blazor`, namespace `Microsoft.Extensions.DependencyInjection`) to register the dispatcher — **not** `RoutingSerginUiDispatcher` by name, which is `internal` to that project and inaccessible from the test assembly. Calling the same registration extension production uses is both the fix for that and a more faithful test.
- Produces: nothing consumed elsewhere — this is a leaf test.

No Testcontainers, no Postgres, no `SerginWebApiFactory` — this is a plain in-memory DI container built by the test itself, proving the permission-check logic in isolation and fast.

- [ ] **Step 1: Write the test file**

```csharp
using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.SharedKernel.Application.Securities;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Dispatching;

public sealed class RoutingSerginUiDispatcherTests
{
    private static readonly DeviceQueryResponse StubResponse = new(Guid.NewGuid(), "DEV-1", Guid.NewGuid());

    [Fact]
    public async Task SendAsync_WithoutRequiredPermission_ReturnsForbidden()
    {
        ErrorOr<DeviceQueryResponse> result = await SendAsync(permissions: []);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    [Fact]
    public async Task SendAsync_WithRequiredPermission_ReachesTheHandler()
    {
        ErrorOr<DeviceQueryResponse> result =
            await SendAsync(permissions: [Permission.Create("permission.dm.devices.read")]);

        Assert.False(result.IsError);
        Assert.Equal(StubResponse, result.Value);
    }

    private static Task<ErrorOr<DeviceQueryResponse>> SendAsync(Permission[] permissions)
    {
        ServiceCollection services = new();

        services.AddSingleton<IUserContextFactory>(new StubUserContextFactory(permissions));
        services.AddScoped(p => p.GetRequiredService<IUserContextFactory>().CreateUserContext());
        services.AddScoped<ISender>(_ => new StubSender(StubResponse));
        services.AddSingleton<IDispatchRouteResolver, AlwaysLocalRouteResolver>();
        services.AddSerginBlazorKit(); // registers ISerginUiDispatcher -> RoutingSerginUiDispatcher, among others

        using ServiceProvider provider = services.BuildServiceProvider();

        ISerginUiDispatcher dispatcher = provider.GetRequiredService<ISerginUiDispatcher>();

        return dispatcher.SendAsync(new GetDeviceByIdQueryCommand(Guid.NewGuid()));
    }

    private sealed class StubUserContextFactory(Permission[] permissions) : IUserContextFactory
    {
        public IUserContext CreateUserContext() => new StubUserContext(permissions);
    }

    private sealed class StubUserContext(Permission[] permissions) : IUserContext
    {
        public UserId Id { get; } = new(Guid.NewGuid());
        public string UserName => "stub";
        public string FirstName => "Stub";
        public string LastName => "User";
        public string Email => "stub@sergin.local";
        public HashSet<Permission> Permissions { get; } = [.. permissions];
    }

    private sealed class StubSender(DeviceQueryResponse response) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            Task.FromResult((TResponse)(object)(ErrorOr<DeviceQueryResponse>)response);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");
    }

    private sealed class AlwaysLocalRouteResolver : IDispatchRouteResolver
    {
        public bool IsRemote(Type requestType) => false;
    }
}
```

- [ ] **Step 2: Run to verify both tests pass**

This task is characterization testing, not TDD driving new code — the permission-check logic under test was already written in Task 3, so there is no "write it, watch it fail, implement, watch it pass" cycle here; the first run is the only run.

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~RoutingSerginUiDispatcherTests"`
Expected: 2 passed, 0 failed. If `SendAsync_WithoutRequiredPermission_ReturnsForbidden` fails instead, re-check Task 3 Step 4's permission-check block landed before the `routeResolver.IsRemote(...)` branch, not after.

- [ ] **Step 3: Commit**

```bash
git add tests/Sergin.MeterMinder.IntegrationTests.All/Dispatching/RoutingSerginUiDispatcherTests.cs
git commit -m "Add RoutingSerginUiDispatcher permission-check tests"
```

---

### Task 7: Real gRPC loopback round trip — Local/Remote equivalence proof

**Files:**
- Create: `tests/Sergin.MeterMinder.IntegrationTests.All/Devices/DeviceGrpcRoundTripTests.cs`
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`

**Interfaces:**
- Consumes: `DeviceGrpcService`, `GetDeviceByIdGrpcInvoker`, generated `DeviceService.DeviceServiceClient`/`DeviceServiceBase` (Task 5); `IGetDeviceQueryRepository` (existing Application layer — implemented by an in-memory stub, no Postgres); `ISender` resolved from the test's own MediatR-wired container (the "Local" comparison path — `GetDeviceByIdQueryCommandHandler` itself is `internal` to the Application project and is never constructed directly); `RoutingSerginUiDispatcher`, `IDispatchRouteResolver` (Task 3).
- Produces: nothing consumed elsewhere — this is the plan's terminal proof.

This test starts a real Kestrel server bound to a loopback port (`http://127.0.0.1:0`, OS-assigned) hosting `DeviceGrpcService` behind a from-scratch minimal DI container — not the full `SerginWebApiFactory<Program>`/Postgres-backed host, so no Testcontainers dependency. It proves three things the spec's §§2–5 claim: (1) Remote and Local return byte-identical results for the same input, (2) the NotFound error path round-trips correctly through the proto `oneof`, (3) an unpermitted caller never reaches the network at all.

- [ ] **Step 1: Add the project references and packages this test needs**

In `tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`, add to the existing `<ItemGroup>` of `ProjectReference`s:

```xml
    <ProjectReference Include="..\..\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj" />
```

Then from the test project directory:
```bash
cd tests/Sergin.MeterMinder.IntegrationTests.All
dotnet add package Grpc.Net.Client
cd ../..
```
(`Grpc.AspNetCore` — needed to build/host the test's own minimal Kestrel app — arrives transitively via the new `ProjectReference` above, since `Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj` already references it.)

- [ ] **Step 2: Write the test file**

```csharp
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Presentation.Grpc;
using Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Devices;
using Sergin.SharedKernel.Application.Securities;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Grpc.Net.Client;

namespace Sergin.MeterMinder.IntegrationTests.All.Devices;

/// <summary>
/// Real Kestrel server on a loopback port, real HTTP/2 gRPC call, real DeviceGrpcService ->
/// ISender.Send -> the actual GetDeviceByIdQueryCommandHandler — just with an in-memory
/// IGetDeviceQueryRepository instead of Postgres, so it needs no Testcontainers. Proves Local and
/// Remote agree, byte for byte, for the same input.
/// </summary>
public sealed class DeviceGrpcRoundTripTests : IAsyncLifetime
{
    private static readonly Permission DevicesReadPermission = Permission.Create("permission.dm.devices.read");

    private WebApplication server = null!;
    private GrpcChannel channel = null!;
    private StubDeviceQueryRepository repository = null!;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();

        builder.WebHost.ConfigureKestrel(options =>
            options.ListenLocalhost(0, listenOptions => listenOptions.Protocols = HttpProtocols.Http2));

        repository = new StubDeviceQueryRepository();

        builder.Services.AddGrpc();
        builder.Services.AddSingleton<IGetDeviceQueryRepository>(repository);
        builder.Services.AddSingleton<IUserContextFactory>(
            new StubUserContextFactory([DevicesReadPermission]));
        builder.Services.AddScoped(p => p.GetRequiredService<IUserContextFactory>().CreateUserContext());
        builder.Services.AddMediatR(o =>
        {
            o.RegisterServicesFromAssembly(DeviceManagementApplicationAssemblyReference.Assembly);
            o.AddOpenBehavior(typeof(PermissionCheckPipelineBehavior<,>));
            o.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
        });

        server = builder.Build();
        server.MapGrpcService<DeviceGrpcService>();
        await server.StartAsync();

        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        channel = GrpcChannel.ForAddress(server.Urls.First());
    }

    public async Task DisposeAsync()
    {
        channel.Dispose();
        await server.StopAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task RemoteDispatch_ForExistingDevice_ReturnsSameResultAsLocalHandler()
    {
        DeviceIntenralId internalId = new(Guid.CreateVersion7());
        Guid publicId = Guid.CreateVersion7();
        DeviceQueryResponse expected = new(publicId, "DEV-42", Guid.CreateVersion7());
        repository.Add(internalId, expected);

        GetDeviceByIdQueryCommand command = new(publicId);

        ISerginUiDispatcher remoteDispatcher = BuildDispatcher(remote: true, permissions: [DevicesReadPermission]);
        ErrorOr<DeviceQueryResponse> remoteResult = await remoteDispatcher.SendAsync(command);

        // "Local" comparison goes through the real MediatR pipeline the server app already wired in
        // InitializeAsync (ISender -> PermissionCheckPipelineBehavior -> GetDeviceByIdQueryCommandHandler),
        // not a hand-constructed handler — GetDeviceByIdQueryCommandHandler is internal to the
        // Application project, and this is the more faithful comparison anyway: the exact same
        // in-process path RoutingSerginUiDispatcher's Local branch takes in production.
        ISender localSender = server.Services.GetRequiredService<ISender>();
        ErrorOr<DeviceQueryResponse> localResult = await localSender.Send(command);

        Assert.False(remoteResult.IsError, remoteResult.IsError ? remoteResult.FirstError.Description : string.Empty);
        Assert.False(localResult.IsError, localResult.IsError ? localResult.FirstError.Description : string.Empty);
        Assert.Equal(localResult.Value, remoteResult.Value);
    }

    [Fact]
    public async Task RemoteDispatch_ForMissingDevice_ReturnsNotFound()
    {
        ISerginUiDispatcher dispatcher = BuildDispatcher(remote: true, permissions: [DevicesReadPermission]);

        ErrorOr<DeviceQueryResponse> result = await dispatcher.SendAsync(new GetDeviceByIdQueryCommand(Guid.NewGuid()));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task RemoteDispatch_WithoutRequiredPermission_ReturnsForbidden()
    {
        // Deliberately queries for a device the shared `repository` field was never given — if the
        // permission short-circuit in RoutingSerginUiDispatcher (Task 3, Step 4) ever regressed to run
        // after the IsRemote branch instead of before it, this would fail as NotFound (from a real round
        // trip that reached the server) instead of Forbidden, not silently pass either way.
        ISerginUiDispatcher dispatcher = BuildDispatcher(remote: true, permissions: []);

        ErrorOr<DeviceQueryResponse> result =
            await dispatcher.SendAsync(new GetDeviceByIdQueryCommand(Guid.NewGuid()));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    private ISerginUiDispatcher BuildDispatcher(bool remote, Permission[] permissions)
    {
        ServiceCollection services = new();

        services.AddSingleton<IUserContextFactory>(new StubUserContextFactory(permissions));
        services.AddScoped(p => p.GetRequiredService<IUserContextFactory>().CreateUserContext());
        services.AddSingleton(new DeviceService.DeviceServiceClient(channel));
        services.AddScoped<IRemoteInvoker<GetDeviceByIdQueryCommand, DeviceQueryResponse>, GetDeviceByIdGrpcInvoker>();
        services.AddSingleton<IDispatchRouteResolver>(new FixedRouteResolver(remote));
        services.AddSerginBlazorKit(); // registers ISerginUiDispatcher -> RoutingSerginUiDispatcher, among others

        return services.BuildServiceProvider().GetRequiredService<ISerginUiDispatcher>();
    }

    private sealed class StubDeviceQueryRepository : IGetDeviceQueryRepository
    {
        private readonly Dictionary<DeviceIntenralId, DeviceQueryResponse> devices = [];

        public void Add(DeviceIntenralId id, DeviceQueryResponse response) => devices[id] = response;

        public Task<DeviceQueryResponse?> GetDeviceById(DeviceIntenralId Id, CancellationToken cancellationToken = default) =>
            Task.FromResult(devices.GetValueOrDefault(Id));
    }

    private sealed class StubUserContextFactory(Permission[] permissions) : IUserContextFactory
    {
        public IUserContext CreateUserContext() => new StubUserContext(permissions);
    }

    private sealed class StubUserContext(Permission[] permissions) : IUserContext
    {
        public UserId Id { get; } = new(Guid.NewGuid());
        public string UserName => "stub";
        public string FirstName => "Stub";
        public string LastName => "User";
        public string Email => "stub@sergin.local";
        public HashSet<Permission> Permissions { get; } = [.. permissions];
    }

    private sealed class FixedRouteResolver(bool remote) : IDispatchRouteResolver
    {
        public bool IsRemote(Type requestType) => remote;
    }
}
```

Note on `GetDeviceById`'s internal-ID lookup: `repository.GetDeviceById` is keyed by `DeviceIntenralId`, but `GetDeviceByIdQueryCommand`/the wire request carry the *public* `Guid Id` — the real `GetDeviceByIdQueryCommandHandler` wraps it as `new DeviceIntenralId(request.Id)` before querying (see `GetDeviceByIdQueryCommandHandler.cs:10`). This test's `StubDeviceQueryRepository.Add` is keyed the same way — `repository.Add(internalId, expected)` where `internalId = new DeviceIntenralId(Guid.CreateVersion7())`, a value distinct from `expected`'s own `Id` (the response's public `Guid Id` field) — matching how the real repository's `DeviceIntenralId` (EF PK) and a device's public-facing `Id` are two different values in production too.

- [ ] **Step 3: Run to verify all three pass**

Like Task 6, this is characterization testing against `DeviceGrpcService`/`GetDeviceByIdGrpcInvoker`, both already written in Task 5 — one run, not a fail-then-pass cycle.

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~DeviceGrpcRoundTripTests"`
Expected: 3 passed, 0 failed. If `RemoteDispatch_ForExistingDevice_ReturnsSameResultAsLocalHandler` fails with a gRPC `Unimplemented`/connection-refused error, check `ListenLocalhost(0, ...)` actually bound (log `server.Urls` to confirm a real address, not empty) and that `Http2UnencryptedSupport` is set *before* `GrpcChannel.ForAddress` is called.

- [ ] **Step 4: Run the whole suite once more, end to end**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`
Expected: every test in the project passes — `ModulePageRenderingTests`, `CreateAndGetUserTests`, `RoutingSerginUiDispatcherTests` (Task 6), and `DeviceGrpcRoundTripTests` (this task).

- [ ] **Step 5: Commit**

```bash
git add tests/Sergin.MeterMinder.IntegrationTests.All/Devices/DeviceGrpcRoundTripTests.cs tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj
git commit -m "Add real-Kestrel gRPC round-trip test proving Local/Remote equivalence"
```

---

## Done when

- `dotnet build Sergin.MeterMinder.slnx` succeeds.
- `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj` passes in full — the two pre-existing tests unchanged in behavior, plus the four new tests from Tasks 6–7.
- The real running host (`dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.All`) still works exactly as before — every module `Local`, `RoutingSerginUiDispatcher` swapped in transparently.
- One module (DeviceManagement), one feature (`GetDeviceById`), has a real, tested, contract-first gRPC path proving the spec's central claim: the page's call site never changes, and both paths terminate in the same `ISender.Send`.
