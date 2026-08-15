# Blazor UI Infrastructure — Design Spec

- **Date**: 2026-08-15
- **Status**: Approved (brainstorming dialogue, all decisions signed off)
- **Goal**: Give user interfaces the same composition story the APIs already have — each module owns its own UI, and one host point discovers and runs all of them together — by adding a UI capability to the existing module contract rather than inventing a parallel mechanism.

## Problem

Sergin is API-only. There is no UI code anywhere in the three repos: no `.razor`, no `wwwroot`, no Razor SDK project, and no mention of Blazor in any README, `CLAUDE.md`, or design doc. The only "UI" today is Scalar's OpenAPI explorer at `/scalar/v1`.

Adding a UI naively — one Blazor project per module wired by hand into a host — would reproduce exactly the boilerplate that `2026-07-26-module-registration-design.md` removed for endpoints: per-module registration calls scattered through `Program.cs`, growing linearly with module count. That spec already established the extension pattern for this situation (capability interfaces derived from `ISerginModule`, one implementation class per module, host chooses capabilities via `modules.OfType<T>()`) and explicitly anticipated future capabilities following it. This design applies that pattern to UI.

## Decisions made during brainstorming

1. **Topology: a new UI host calling MediatR in-process.** `Sergin.MeterMinder.Hosts.WebUi.All` sits parallel to the API host and registers the *same* `IReadOnlyCollection<ISerginModule>`, so components dispatch commands/queries directly — no HTTP hop, no duplicated DTOs, no token relay. Rejected: routing the UI through the Web API over HTTP (doubles the infrastructure — shared-contract projects, CORS, service discovery, contract versioning — for separation this modular monolith does not currently need); and merging the UI into the API host (forces the existing `/mm/*` and `/ua/*` endpoints under `/api/` to avoid colliding with UI routes, a breaking change to every endpoint and the integration tests).
2. **Render mode: Blazor Web App, global `InteractiveServer`.** Follows from decision 1 — in-process MediatR is incompatible with WebAssembly, and referencing `*.Presentation.WebApi` transitively pulls `Microsoft.AspNetCore.App`, which a WASM client project cannot take.
3. **Component library: MudBlazor 9.8.0.** Verified against NuGet that it ships an explicit `net10.0` target group. Chosen over Radzen and FluentUI for its `MudDataGrid` server-side data story, which maps onto the existing `ListQuery` contract — itself already shaped for a Material data grid (`FilterData { Id, Value, Mode }`, `SortingData(Id, Desc)`, a `FilteringType` enum with `contains`/`between`/`startsWith`).
4. **Scope: infrastructure plus one vertical per module** — Devices (MeterMinder) and Users (UserAccess), each list/detail/create. Two modules is the smallest scope that actually proves multi-module composition; a third aggregate would repeat the pattern without adding architectural information.
5. **No authentication in this work.** Instead a configuration-driven `IUserContextFactory` supplying a dev user with an explicit permission list. Real auth needs credential storage on `User` (today `UserName` + `IsActive` only), password hashing, a migration, a login slice, and a permissions source — none of which exist. That is its own spec and cycle.
6. **The dev-user seam is a correctness fix, not scaffolding.** `InternalUserContextFactory` maps `HttpContext: null → SystemUser` carrying `Permission.AllPlatform`. In a Blazor Server circuit `HttpContext` is null after the first render, so without this change every UI interaction would silently run as SYSTEM with every permission, making `PermissionCheckPipelineBehavior` dead code in the UI host and masking authorization bugs until real auth arrives.
7. **A shared `AddSerginCore` must be extracted first.** `AddSerginWebApi` currently owns MediatR, the event dispatcher, `IDbConnectionFactory`, `ILocalizer` *and* the `module.AddServices` loop. A UI host would otherwise duplicate ~30 lines. This is prerequisite work in the SharedKernel submodule.
8. **Every UI operation gets its own DI scope.** In Blazor Server "scoped" means the circuit's lifetime, not a request. The design routes all dispatch through an `ISerginDispatcher` that opens a fresh scope per call (decision detail in *Per-operation DI scope* below).

## Architecture

Three seams, each mirroring something that already exists for the API:

| Concern | API today | UI (new) |
|---|---|---|
| Capability contract | `ISerginWebApiModule.MapEndpoints(RouteGroupBuilder)` | `ISerginWebUiModule.UiAssembly` + `NavItems` |
| Host bootstrap | `AddSerginWebApi` / `UseSerginWebApiAsync` | `AddSerginWebUi` / `UseSerginWebUiAsync` |
| Route isolation | `app.MapGroup(module.Schema)` | `@page "/{schema}/…"` + a startup guard |

| Piece | Home | Content |
|---|---|---|
| `ISerginWebUiModule`, `SerginNavItem` | `Sergin.SharedKernel.Modules` | the UI capability contract |
| `AddSerginCore` | `Sergin.SharedKernel.Hosts` | registrations shared by both host types |
| `AddSerginWebUi`, `UseSerginWebUiAsync`, route guard, dev-user types | **new** `Sergin.SharedKernel.Hosts.WebUi` | UI host bootstrap |
| Shell, dispatcher, error presentation | **new** `Sergin.SharedKernel.Presentation.Blazor` | `SerginApp`, `Routes`, `MainLayout`, `NavMenu`, `ISerginDispatcher` |
| Module pages | **new** `Sergin.<Module>.Presentation.Blazor` | one RCL per module |

## The contract

```csharp
namespace Sergin.SharedKernel.Modules;

public interface ISerginWebUiModule : ISerginModule
{
    Assembly UiAssembly { get; }

    IReadOnlyCollection<SerginNavItem> NavItems { get; }
}

public sealed record SerginNavItem(string Label, string Href, string Icon, int Order = 0);
```

Each type gets its own file. Two members only: `UiAssembly` is what Blazor's `AddAdditionalAssemblies` needs to discover routable components — the framework's own multi-assembly routing hook and the direct analogue of `MapEndpoints`. `NavItems` lets a module contribute navigation without the shell knowing any module exists. `Icon` is an opaque string (MudBlazor icons are SVG-path string constants), keeping the contract project free of any UI-library reference; `Sergin.SharedKernel.Modules` remains a zero-ProjectReference leaf.

No `AddUiServices` member — modules need nothing beyond MediatR today, and `AddServices` already runs for every host. Add it when a module actually needs UI-only services.

## `AddSerginCore` extraction

New `Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs`, namespace `Microsoft.Extensions.Hosting` (the existing Aspire convention, so `Program.cs` needs no extra `using`). Takes `IHostApplicationBuilder`; returns the `"Sergin"` `IConfigurationSection` so callers can reuse it.

**Moves into `AddSerginCore`**: `GetRequiredSection("Sergin")`; `AddMediatR` (module assembly loop, then `PermissionCheckPipelineBehavior` then `ValidationPipelineBehavior`); `IEventDispatcher`/`DefaultEventDispatcher`; `EventDispatcherInterceptor`; the connection-string read with its fail-fast throw; `IDbConnectionFactory`/`PostgresDbConnectionFactory`; `AddScoped(p => p.GetRequiredService<IUserContextFactory>().CreateUserContext())`; `ILocalizer`/`DefaultLocalizer`; the `foreach module → module.AddServices(...)` loop.

**Stays in `AddSerginWebApi`**: `AddOpenApi()`, `AddHttpContextAccessor()`, `AddTransient<IUserContextFactory, InternalUserContextFactory>()`. Each host supplies its own `IUserContextFactory`; the scoped `IUserContext` registration is shared because it only resolves whichever factory the host registered.

**One added registration-time check**: `AddSerginCore` throws if two modules report the same `Schema`. That converts decision 8's silent failure mode — a host listing two classes for one module runs `AddServices` twice and double-registers the DbContext — into a fail-fast startup error for every present and future host shape. It is behaviour-additive on a set that has never contained duplicates.

**On behavior parity**: the extraction reorders registrations relative to today, and the reason that is safe should be stated rather than assumed — DI registration order matters only for last-wins on a duplicated service type or for `IEnumerable<T>` ordering, and none of these registrations duplicate a service type. The `IUserContext` factory lambda resolves lazily, so registering it before `IUserContextFactory` is fine.

**Reference moves**: `Sergin.SharedKernel.Hosts` gains the `MediatR` package and ProjectReferences to `Sergin.SharedKernel.Application`, `…Infrastracture.Data`, `…Infrastructure`, `…Infrastructure.Data.EFCore`, `…Modules` (the `Infrastracture` misspelling on two of them is the real project name). `Sergin.SharedKernel.Hosts.WebApi` keeps only `…Infrastracture.WebApi`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, and its `NuGetAuditSuppress`.

**`InternalsVisibleTo`, verified against all five `AssemblyInfo.cs` files**: `Sergin.SharedKernel.Application`, `…Infrastructure`, `…Infrastructure.Data.EFCore` (the `EventDispatcherInterceptor`) and `…Infrastracture.WebApi` **already grant `Sergin.SharedKernel.Hosts`** — no change needed. The one exception is `Sergin.SharedKernel.Infrastracture.Data/Properties/AssemblyInfo.cs`, which grants only `Sergin.Hosts.WebApi.All` and `Sergin.SharedKernel.Hosts.WebApi`; because `PostgresDbConnectionFactory` moves into `AddSerginCore`, it must add `InternalsVisibleTo("Sergin.SharedKernel.Hosts")` or the extraction will not compile. While there, drop the `Sergin.Hosts.WebApi.All` grant — that project was renamed to `Sergin.MeterMinder.Hosts.WebApi.All` and no longer exists.

## UI host bootstrap

```csharp
public static WebApplicationBuilder AddSerginWebUi(
    this WebApplicationBuilder builder, IReadOnlyCollection<ISerginModule> modules)
{
    IConfigurationSection serginSection = builder.AddSerginCore(modules);

    builder.Services.AddRazorComponents().AddInteractiveServerComponents();
    builder.Services.AddMudServices();

    builder.Services.Configure<DevUserOptions>(serginSection.GetSection(DevUserOptions.SectionName));
    builder.Services.AddSingleton<IUserContextFactory, ConfiguredUserContextFactory>();

    builder.Services.AddSingleton<ISerginDispatcher, ScopedSerginDispatcher>();

    return builder;
}

public static async Task<WebApplication> UseSerginWebUiAsync(
    this WebApplication app, IReadOnlyCollection<ISerginModule> modules)
{
    if (app.Environment.IsDevelopment())
    {
        foreach (ISerginModule module in modules)
        {
            await module.MigrateAsync(app.Services);
        }
    }

    IReadOnlyCollection<ISerginWebUiModule> uiModules = [.. modules.OfType<ISerginWebUiModule>()];

    SerginUiRouteGuard.Validate(uiModules);

    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseAntiforgery();

    app.MapStaticAssets();

    app.MapRazorComponents<SerginApp>()
       .AddInteractiveServerRenderMode()
       .AddAdditionalAssemblies([.. uiModules.Select(m => m.UiAssembly)]);

    return app;
}
```

Middleware follows the current .NET 10 Blazor Web App template: `MapStaticAssets()` (the build-time static-asset endpoint convention that replaced `UseStaticFiles()` for this template), `UseAntiforgery()`, and status-code re-execution for the not-found page. `AddRazorComponents` registers antiforgery services itself, so there is no `AddAntiforgery()` call.

Migration policy mirrors the API host exactly — Development-only. The prior spec's single-migrator rule concerns non-Development environments, where neither host migrates today; keeping the Development branch identical is what lets the UI host run standalone against a fresh database and what makes the UI integration test work.

`UseSerginWebUiAsync` is generic over the root component (`<TRootComponent> where TRootComponent : IComponent`) so the host supplies its own `App`.

**`Sergin.SharedKernel.Hosts.WebUi` must use `Microsoft.NET.Sdk.Razor`** even though it contains no `.razor` files. Plain `Microsoft.NET.Sdk` does not import `Microsoft.NET.Sdk.StaticWebAssets`, and `ResolveReferencedProjectsStaticWebAssetsConfiguration` probes project references with `SkipNonexistentTargets="true"` — so a plain-SDK project in the middle of the chain **silently swallows its RCL dependencies' static web assets**, and MudBlazor's CSS/JS never reaches the host. The Razor SDK is a no-op for codegen on a project with no components. This is also why the UI host references the module RCLs directly (see PR 3).

**Both routing APIs are required.** `MapRazorComponents<T>().AddAdditionalAssemblies(...)` populates the *server-side* endpoint table used for static SSR; the `Router` component's `AdditionalAssemblies` parameter populates *client-side* routing once the circuit is interactive. Supplying only the first makes module pages 404 on first navigation; supplying only the second makes them 404 on direct URL entry.

## Per-operation DI scope

In Blazor Server, **"scoped" means the lifetime of the SignalR circuit, not a request**. The module `DbContext`s (registered scoped by `AddDbContext`), `IDbConnectionFactory` (scoped) and `IUserContext` (scoped) would therefore be shared by every interaction in a circuit that may live for hours, producing three well-known failures: an unbounded change tracker, stale reads served from the change tracker, and `InvalidOperationException: A second operation was started on this context…` whenever two renders overlap.

```csharp
public interface ISerginDispatcher
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

internal sealed class ScopedSerginDispatcher(IServiceScopeFactory scopeFactory) : ISerginDispatcher
{
    public async Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

        return await sender.Send(request, cancellationToken);
    }
}
```

**Rule for every UI component: inject `ISerginDispatcher`, never `ISender`.** This is the single most important convention in the new infrastructure and belongs in each module's `CLAUDE.md`. It changes nothing in the API host, where scoped already means per-request.

## Route isolation and the startup guard

Razor `@page` templates are compile-time constants, so the host cannot wrap module pages in a prefix the way `app.MapGroup(module.Schema)` does for endpoints. The convention is that every module page route begins with its schema segment (`@page "/mm/devices"`, `@page "/ua/users"`), and a startup guard turns a violation into a fail-fast error rather than a silent route collision between two modules:

```csharp
internal static class SerginUiRouteGuard
{
    public static void Validate(IReadOnlyCollection<ISerginWebUiModule> modules)
    {
        foreach (ISerginWebUiModule module in modules)
        {
            string prefix = $"/{module.Schema}/";

            IReadOnlyCollection<string> offenders =
            [
                .. module.UiAssembly.GetTypes()
                    .Where(type => typeof(IComponent).IsAssignableFrom(type))
                    .SelectMany(type => type.GetCustomAttributes<RouteAttribute>())
                    .Select(route => route.Template)
                    .Where(template => !template.StartsWith(prefix, StringComparison.Ordinal))
            ];

            if (offenders.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Module '{module.Schema}' declares routable components outside its '{prefix}' prefix: " +
                    string.Join(", ", offenders));
            }
        }
    }
}
```

## `ErrorOr<T>` in components

`ToApiResult()`/`EndpointResult<T>` resolve `ILocalizer` from `httpContext.RequestServices` and are unusable from a component. `Sergin.SharedKernel.Presentation.Blazor` gets a presentation helper reusing the same localization-key convention as `ApiProblemResults` (`l[error.Code]` for the message) and mapping `ErrorType` to a MudBlazor `Severity`, so error text stays consistent between API and UI without dragging `HttpContext` in.

The existing localization gap is untouched: `DefaultLocalizer` is an identity passthrough with no `.resx` anywhere, so errors render as raw keys (`General.NotFound`) in both API and UI.

## List queries from the UI

List pages build the query directly rather than through `ListQueryRequestModel`, so the Blazor projects never reference `Sergin.SharedKernel.Presentation.WebApi`. Verified signature, in namespace `Sergin.SharedKernel.Application.Commands.Queries`:

```csharp
ListQueryFactory.Create<TResponseData>(
    PageSize size, PageIndex index,
    Term? Term = default, Filtering? Filtering = default, Sorting? Sorting = default)
```

`PageSize` and `PageIndex` both define implicit conversions from `int`, so the call site is `ListQueryFactory.Create<GetDeviceListItem>(state.PageSize, state.Page + 1)`.

Two traps confirmed by reading the source:

- **`PageIndex` is 1-based** (`Default = 1`, `Skip => Size * (Index - 1)`), while MudBlazor's `GridState.Page` is **0-based**. The `+ 1` is required; getting it wrong silently skips the first page of every grid.
- **Two `ListQueryResponse<T>` types exist.** The live one is `ListQueryResponse.cs` in namespace `Sergin.SharedKernel.Application`; a dead duplicate sits in `Commands/Queries/ListQueryResponse.cs` under namespace `RTS.Common.Domain.Repository.Query`, left over from another codebase. Bind to the former. Its namespace differs from `ListQuery<T>`'s, so `_Imports.razor` needs both.

List queries have no dedicated command type — the handler implements `IListQueryHandler<TItem>` against the shared generic `ListQuery<TItem>`.

## File inventory, in merge order

The three repos land in dependency order. Local development across all three works before anything merges, because submodules are checked out as ordinary working trees — build with `dotnet build Sergin.MeterMinder.slnx` from the root throughout.

### PR 1 — `Sergin.SharedKernel` submodule

**New projects** (register in `Sergin.SharedKernel.slnx` *and* `Sergin.MeterMinder.slnx`):

| Project | Solution folder | SDK |
|---|---|---|
| `Sergin.SharedKernel.Hosts.WebUi` | `/src/SharedKernel/Hosts/` | `Microsoft.NET.Sdk` + `FrameworkReference` |
| `Sergin.SharedKernel.Presentation.Blazor` | `/src/SharedKernel/Presentation/` | `Microsoft.NET.Sdk.Razor` |

**New**: `Sergin.SharedKernel.Modules/{ISerginWebUiModule.cs, SerginNavItem.cs}`; `Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs`; `Sergin.SharedKernel.Hosts.WebUi/{SerginWebUiExtensions.cs, SerginUiRouteGuard.cs, DevUserOptions.cs, ConfiguredUserContextFactory.cs}`; `Sergin.SharedKernel.Presentation.Blazor/{SerginApp.razor, Routes.razor, Layout/MainLayout.razor, Layout/NavMenu.razor, _Imports.razor, Dispatch/ISerginDispatcher.cs, Dispatch/ScopedSerginDispatcher.cs, Results/ErrorPresentation.cs, wwwroot/app.css}`.

The dev-user types live in `Hosts.WebUi` rather than a new `Sergin.SharedKernel.Infrastructure.WebUi`: they are two small host-specific types, and a new project would force a choice between propagating the existing `Infrastracture` misspelling or introducing an inconsistent sibling.

**Modified**: `Sergin.SharedKernel.Hosts.csproj`; `Sergin.SharedKernel.Hosts.WebApi/SerginWebApiExtensions.cs` (`AddSerginWebApi` becomes `AddSerginCore` + three web-only registrations; `UseSerginWebApiAsync` unchanged); `Sergin.SharedKernel.Hosts.WebApi.csproj`; `Sergin.SharedKernel.Infrastracture.Data/Properties/AssemblyInfo.cs`; `Directory.Packages.props` (MudBlazor 9.8.0, alphabetical); `.editorconfig`; `Sergin.SharedKernel.slnx`; `README.md`; `.claude/CLAUDE.md`.

### PR 2 — `Sergin.UserAccess` submodule

**New project** `Sergin.UserAccess.Presentation.Blazor` (`Microsoft.NET.Sdk.Razor`; references `Sergin.UserAccess.Application`, `Sergin.SharedKernel.Presentation.Blazor`, MudBlazor). That repo has no solution or props of its own, so it is registered only in `Sergin.MeterMinder.slnx` under `/src/Modules/UserAccess/Presentation/`.

Files: `_Imports.razor`, `UserAccessBlazorAssemblyReference.cs`, `Users/Pages/UserList.razor` (`@page "/ua/users"`), `Users/Pages/UserDetail.razor` (`@page "/ua/users/{UserId:guid}"`, with the Deactivate action), `Users/Pages/UserCreate.razor` (`@page "/ua/users/new"`).

**Modified**: `UserAccessModule.cs` implements `ISerginWebUiModule`; `Sergin.UserAccess.csproj`; `.claude/CLAUDE.md`.

### PR 3 — `Sergin.MeterMinder` (this repo)

**New projects**: `src/Modules/MeterMinder/Sergin.MeterMinder.Presentation.Blazor` (`Microsoft.NET.Sdk.Razor`, solution folder `/src/Modules/MeterMinder/Presentation/`); `src/Hosts/Sergin.MeterMinder.Hosts.WebUi.All` (`Microsoft.NET.Sdk.Web`); `tests/Sergin.MeterMinder.IntegrationTests.WebUi.All`.

Devices pages mirror the Users set: `/mm/devices`, `/mm/devices/{DeviceId:guid}`, `/mm/devices/new`. Blazor prefers literal segments over parameters, so `/mm/devices/new` resolves ahead of the `{DeviceId:guid}` route.

```csharp
using Sergin.MeterMinder;
using Sergin.SharedKernel.Modules;
using Sergin.UserAccess;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("sergin-ui");

IReadOnlyCollection<ISerginModule> modules = [new MeterMinderModule(), new UserAccessModule()];

builder.AddSerginWebUi(modules);

WebApplication app = builder.Build();

await app.UseSerginWebUiAsync(modules);

await app.RunAsync();

public partial class Program;
```

**Shell split**: the host owns `Components/App.razor` (the HTML document, `<base href>`, the MudBlazor CSS/JS links via `@Assets[...]`, `HeadOutlet`, the Blazor script) and `Components/Routes.razor` (the `Router`, which needs `AdditionalAssemblies` from the module catalog). Everything reusable — `SerginMainLayout`, `SerginNavMenu`, the dispatcher, error presentation — lives in `Sergin.SharedKernel.Presentation.Blazor`. That is roughly 30 lines of per-host document markup, which is where genuine per-host customization belongs; a second UI host reuses the layout and nav unchanged.

The host **must reference the two module Blazor RCLs directly**, not merely transitively through the composition roots. `Sergin.MeterMinder` and `Sergin.UserAccess` are plain `Microsoft.NET.Sdk` projects, which break static-web-asset propagation at the middle hop, so `_content/...` assets from the module RCLs would be silently dropped. Those references look redundant and are not; the csproj carries a comment saying so.

The host also needs `appsettings.json` (with a `Sergin:DevUser` section), `Properties/launchSettings.json` on **ports 5002/5003** (5000/5001/5432/18888/4317 are taken), a `Dockerfile` modelled on the API host's, and `wwwroot/` for its favicon. It reuses the API host's `UserSecretsId` so one `dotnet user-secrets set` covers both hosts.

**Modified**: `MeterMinderModule.cs` implements `ISerginWebUiModule`; `Sergin.MeterMinder.csproj`; root `Directory.Packages.props`; root `.editorconfig`; `Sergin.MeterMinder.slnx`; `docker-compose/{docker-compose.yml, docker-compose.override.yml, launchSettings.json}` gain a `sergin.hosts-ui` service; `.claude/CLAUDE.md`; `.claude/skills/{add-module,add-feature}/SKILL.md`; `README.md`.

**Cleanup**: delete the stale `src/Hosts/Sergin.Hosts.WebApi.All/` directory — only a `.csproj.user`, an empty `Properties/`, and stale `bin`/`obj`; not in the solution, residue from the MeterMinder rename.

## Accepted trade-offs

**The API host will carry MudBlazor.** Per decision 8 of the module-registration spec, each module ships one class implementing all its capabilities. So `MeterMinderModule` implements both `ISerginWebApiModule` and `ISerginWebUiModule`, and `Sergin.MeterMinder` must reference `Sergin.MeterMinder.Presentation.Blazor` — putting MudBlazor and the Razor SDK into the API host's dependency closure and Docker image. The alternative, a separate `MeterMinderUiModule` in its own composition root, keeps the API host clean but reintroduces exactly the footgun that decision prevents: a host listing both classes runs `AddServices` twice and double-registers the DbContext. Accepted: a few megabytes of unreferenced assemblies, no correctness impact, versus a latent double-registration bug.

**Grids ship with paging only, using `MudTable` rather than `MudDataGrid`.** `ListQueryRequestModel.ToListQuery<T>()` silently drops `Filtering` and `Sorting`, and none of the three query repositories reference `Term`, `Filtering` or `Sorting` — the SQL is a bare `count(*)` plus `LIMIT`/`OFFSET`. Because the UI builds `ListQuery<T>` directly it *could* populate all three, but nothing downstream reads them, so the gap is repository-side and cannot be closed from the UI.

`MudDataGrid` would require *explicitly disabling* sorting and filtering, leaving a switch the next contributor can flip back on to no effect. `MudTable` shows no sort or filter affordance unless a `MudTableSortLabel` is added, so there is nothing to disable and nothing that looks broken. Each table carries a caption stating that server-side sorting and filtering are not implemented. `MudTable<T>.ServerData` takes `TableState` (whose `Page` is 0-based) and returns `TableData<T> { Items, TotalItems }`.

**Both list queries are missing an `ORDER BY`.** `DeviceQueryRepository.GetListAsync` and `UserQueryRepository.GetListAsync` page with bare `LIMIT`/`OFFSET` over an unordered result set, which Postgres does not guarantee to be stable — page two can repeat and skip rows. The API has this bug today; a UI pager makes it visible on the first click. Each of PR 2 and PR 3 adds `ORDER BY id` to its module's list query as a separate, clearly-labelled one-line fix (IDs are UUIDv7, so this is also chronological).

## Analyzers versus Razor

`AnalysisMode=All` + `TreatWarningsAsErrors` + `CodeAnalysisTreatWarningsAsErrors` + `EnforceCodeStyleInBuild` + SonarAnalyzer apply to Razor-generated sources, which routinely trip rules meaningless for generated component code (`CA1515` on public component types, `CA1812`, `S1118`, `S3903`, nullable warnings on `[Parameter]` properties, naming rules on generated members).

Analysis must **not** be blanket-disabled. Follow the precedent already in the root `.editorconfig`, which scopes generated-code treatment for migrations:

```ini
[*/Database/Migrations/*.cs]
generated_code = true
```

The structural half of the answer matters more than the suppression:

> **All C# lives in `.razor.cs` code-behind partial classes. `.razor` files carry markup only — no `@code { }` blocks.**

This is a hard convention, not a style preference. Code inside `@code { }` is compiled into generated output and would therefore be *exempt from analysis* — an unaudited hole in a repo whose entire build philosophy is that analyzers gate everything. Code-behind files are ordinary `.cs`, matched by the existing `[*.cs]` sections and fully analyzed. It also shrinks the generated-code surface to markup only. This rule goes in all three `CLAUDE.md` files.

With that in place, the suppression is one `[*.g.cs] generated_code = true` section mirroring the migrations precedent, plus a `[*.razor]` formatting section. Add specific rule suppressions only as a real build surfaces them — the first implementation step is a throwaway spike that builds a scratch MudBlazor page under the real props and records exactly which diagnostic IDs fire, because guessing about `AnalysisMode=All` + Sonar over source-generated code is how this sinks. Root and SharedKernel submodule each have their own `.editorconfig`; they are byte-identical today and must stay so. `[Parameter]` properties should use `required` or a sensible default rather than a suppression.

Two diagnostics should be left as errors rather than suppressed: `RZ10012` (unresolvable markup element — almost always a missing `@using`) and MudBlazor's own `MUD0001`/`MUD0002` analyzers. Both are correct, useful failures.

MudBlazor is added as a version-less `PackageReference` with a `<PackageVersion>` entry in **both** `Directory.Packages.props` files, kept alphabetical.

## Error handling

- Missing `"Sergin"` section or connection string: unchanged fail-fast from `AddSerginCore`, now shared by both hosts.
- A module page routed outside its schema prefix: `SerginUiRouteGuard` throws at startup naming the module and the offending templates.
- `ErrorOr` failures in components surface through the MudBlazor snackbar via `ErrorPresentation`, using the same localization keys as the API's ProblemDetails.
- Running outside Development without real authentication: `ConfiguredUserContextFactory` fails fast rather than silently granting the dev user's permissions in a deployed environment.

## Testing and verification

1. `dotnet build Sergin.MeterMinder.slnx` — analyzer-clean; warnings are errors.
2. `dotnet build src/SharedKernel/Sergin.SharedKernel.slnx` — the submodule stays standalone-buildable.
3. `dotnet test tests/Sergin.MeterMinder.IntegrationTests.WebApi.All/…` — the existing API suite stays green, proving the `AddSerginCore` extraction changed no API behavior.
4. `dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.WebApi.All` — `/scalar/v1` still lists all 10 endpoints.
5. `dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.WebUi.All` — at `http://localhost:5002` the nav shows entries contributed by **both** modules; `/mm/devices` and `/ua/users` list, page, open a detail, and create; a created row appears in the list.
6. `docker compose -f docker-compose/docker-compose.yml up --build` — both hosts start.
7. New `tests/Sergin.MeterMinder.IntegrationTests.WebUi.All` — static-SSR smoke tests reusing the existing generic `SerginWebApiFactory<TEntryPoint>` (host-agnostic despite its name; it also forces `Development`, which the UI host requires). Blazor Server returns real server-rendered HTML before any circuit exists, so a plain `HttpClient` can assert content. Asserting that `/mm/devices` and `/ua/users` both return 200 and that each page's HTML contains **both** modules' nav links covers, for real, the four things most likely to be silently broken: `AddAdditionalAssemblies` (without it these 404), cross-module nav aggregation, the route-prefix guard, and the whole `AddSerginWebUi`/`AddSerginCore` graph resolving.

   **This must be a separate test project, not new files in the existing one.** `public partial class Program;` sits in the global namespace in both hosts, so a single project referencing both host assemblies fails with CS0433 (`'Program' exists in both …`) — an ambiguity no `using` can resolve. If one project spanning both hosts is ever wanted, the fix is a per-host public marker type rather than `Program`, since `WebApplicationFactory<TEntryPoint>` only uses `typeof(TEntryPoint).Assembly`.

Deliberately **not** added: bUnit component tests. Worth revisiting once components carry real logic; today they would mostly assert MudBlazor's own behavior.

## Documentation and skills updates (part of this work)

- **Root `.claude/CLAUDE.md`**: a "Host / UI composition" section covering `ISerginWebUiModule`, the schema-prefix route convention, and the `ISerginDispatcher`-never-`ISender` rule.
- **`.claude/skills/add-module/SKILL.md`**: a module now optionally ships a `Presentation.Blazor` project and implements `ISerginWebUiModule`.
- **`.claude/skills/add-feature/SKILL.md`**: an optional UI slice alongside the endpoint slice.
- **Submodule `CLAUDE.md` files**: the same conventions, scoped to what each repo owns.

## Out of scope / future notes

- **Real authentication** — credential storage, hashing, a login slice, a permissions source. Its own spec and cycle. Until then `PermissionCheckPipelineBehavior` is enforced against the configured dev user.
- **Server-side sort/filter/search** through `ListQuery`. The shape is already implied by the unused `FilterData`/`SortingData` types; wiring it means changing `ToListQuery`, `ListQueryFactory` call sites, and all three query repositories' SQL.
- **Localization resources** — `DefaultLocalizer` stays an identity passthrough; errors render as raw keys in both API and UI.
- **Manufacturers UI** — repeats the pattern Devices and Users establish.
- **An Aspire AppHost** — none exists today (only ServiceDefaults plus the dashboard container); orchestration stays Docker Compose. Note that the root `README.md` and `.claude/CLAUDE.md` both overstate this as ".NET Aspire for local orchestration".
- **`User.IsActive` is not exposed by any read model**, so the Users UI cannot show active state without a new query slice.
- The list-query `[RequiredPermissions]` structural gap noted in `CLAUDE.md` is untouched — list pages remain unauthorized, as list endpoints already are.
