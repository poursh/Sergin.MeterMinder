# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Sergin is a .NET 10 **modular monolith** platform, built with DDD + Clean Architecture and per-feature vertical slices. PostgreSQL is the storage. There are currently two modules: **DeviceManagement** — a Head-End System (HES) for smart electricity/gas/water meters (device communication, data collection) — and **UserAccess**, for identity and access. **One host composes them**: `Sergin.MeterMinder.Hosts.All`, a Blazor Server UI.

**There is no Web API host.** `Sergin.MeterMinder.Hosts.WebApi.All` was deliberately dropped — the Blazor UI dispatches to its module handlers in-process through MediatR, so the HTTP hop bought nothing. **Everything below the host is unchanged and still compiles**: both modules still implement `ISerginWebApiModule` and still ship `.Presentation.WebApi` endpoint classes, and `Sergin.SharedKernel.Hosts.WebApi` (`AddSerginWebApi`/`UseSerginWebApiAsync`) still builds as part of the solution. Nothing calls `MapEndpoints` today, so **that code is live-but-unhosted, not dead** — restoring an API means adding a ~20-line `Program.cs` host, not rewriting the endpoints. Don't "clean up" the WebApi capability as unused, and keep new feature slices shipping their endpoint alongside their page as the `/add-feature` skill describes.

**Aspire here is ServiceDefaults plus a dashboard container, not an AppHost.** `Sergin.SharedKernel.Hosts`'s `AddServiceDefaults` wires OpenTelemetry/health checks/resilience/service discovery, and `docker-compose.yml` runs the `aspire-dashboard` image for telemetry — but there is no Aspire AppHost project anywhere in the repo. **Local orchestration is Docker Compose.** (Older prose in this repo called it "Aspire for local orchestration"; that was an overstatement.)

**This repo (`Sergin.MeterMinder`) is the root/hostable repo of a three-repo split** — it's never itself embedded as someone else's submodule. `src/SharedKernel/` and `src/Modules/UserAccess/` are **git submodules** pointing at their own repos ([Sergin.SharedKernel](https://github.com/poursh/Sergin.SharedKernel), [Sergin.UserAccess](https://github.com/poursh/Sergin.UserAccess)) — changes to their code happen via PRs in those repos, not here. Clone with `git clone --recurse-submodules`, or run `git submodule update --init --recursive` after a plain clone (see Commands below). Each of the three repos carries its own `.claude/CLAUDE.md` scoped to what it owns; this file only covers what's specific to being the host (the `DeviceManagement` module itself, the Host project, and how the pieces compose).

## Commands

Run all commands from the repo root. The solution uses the modern XML format (`Sergin.MeterMinder.slnx`); pass it explicitly or run from the repo root so the CLI resolves it automatically. Requires the .NET 10 SDK / VS 17.13+ / Rider.

```bash
# First-time clone (or after cloning without --recurse-submodules)
git submodule update --init --recursive

# Build (warnings are errors — see below)
dotnet build Sergin.MeterMinder.slnx

# Run the Blazor Server UI directly (Development only — it refuses to start elsewhere, see below)
dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.All       # http://localhost:5002, landing page /

# Run everything in Docker (app + postgres:17 + Aspire dashboard)
docker compose -f docker-compose/docker-compose.yml up --build

# Run the integration test suite (needs Docker — spins up a real postgres:17 via Testcontainers)
dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj
```

**Ports** (`launchSettings.json` for `dotnet run`, `docker-compose.yml` for the containers — they agree):

| Port | What |
|---|---|
| 5002 / 5003 | Blazor UI host, http / https |
| 5432 | PostgreSQL |
| 18888 | Aspire dashboard UI |
| 4317 | OTLP ingest (maps to the dashboard container's 18889) |

There is **one** test project, integration-only — xUnit + `Testcontainers.PostgreSql` +
`Microsoft.AspNetCore.Mvc.Testing`, exercising a real host end-to-end against a disposable container rather
than mocks. There are no unit test projects yet.

- `tests/Sergin.MeterMinder.IntegrationTests.All` — drives `Sergin.MeterMinder.Hosts.All`. Two shapes:
  - `Shell/ModulePageRenderingTests.cs` asserts each module's pages render server-side, that they render
    *interactively* rather than falling back to static SSR, that the shell composed nav entries from both
    modules, that the configured `Sergin:ApplicationName` reaches the app bar, and that `/` serves the
    home slot with a Home nav entry that is *not* active on a module page.
  - `Users/CreateAndGetUserTests.cs` is the only **write**-path test: command handler → domain factory →
    EF repository → `SaveChangesAsync` → raw-SQL list read.

**Write-path tests dispatch in-process, not over HTTP.** With no API host there is no endpoint to POST to, so
`CreateAndGetUserTests` resolves `ISerginUiDispatcher` from `factory.Services` and sends `CreateUserCommand`
the same way a Blazor page does. Use that shape for new write coverage rather than reaching for `HttpClient`.
Dispatching (not raw `ISender`) matters: it opens a fresh DI scope per send, so the read genuinely round-trips
through Postgres instead of being served from the writing `DbContext`'s change tracker.

**If a second host is ever added back, its test suite must be a separate project.** Each host ends its
`Program.cs` with `public partial class Program;` in the *global* namespace (so `WebApplicationFactory<Program>`
can bind to it), so referencing two host projects from one test project is a `CS0433` "type exists in both"
ambiguity. That constraint is dormant while there is only one host — it is not gone.

**Test fixture pattern**: every test class shares one `SerginWebApiFactory<Program>` (`WebApplicationFactory<TEntryPoint>, IAsyncLifetime`,
generic over the host's entry point) via the `[Collection(nameof(IntegrationTestCollection))]` attribute —
so don't spin up a new factory per test class.
`SerginWebApiFactory<TEntryPoint>` lives in the `Sergin.SharedKernel.IntegrationTests` submodule project
(referenced here via `ProjectReference`, not owned by this repo) so any host can reuse it — despite the `WebApi` in its
name it is presentation-agnostic, and the Blazor host uses it unchanged. It starts a
`Testcontainers.PostgreSql` container in `InitializeAsync` and sets the `Sergin__ConnectionStrings__Database` env var
*before* the host builds (a `ConfigureWebHost` override runs too late for this). Test classes inject
`SerginWebApiFactory<Program>` via primary constructor and call `factory.CreateClient()` to request real pages.

### EF Core migrations

Each module owns its own `DbContext` and migrations, so `--project` must point at that module's `Infrastructure.Data` project. `DeviceManagementDbContext` and `UserAccessDbContext` each have an `IDesignTimeDbContextFactory` that reads the connection string from the `Sergin:ConnectionStrings:Database` key in `appsettings.Development.json`:

```bash
dotnet ef migrations add <Name> \
  --project src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data \
  --startup-project src/Hosts/Sergin.MeterMinder.Hosts.All

dotnet ef migrations add <Name> \
  --project src/Modules/UserAccess/Sergin.UserAccess.Infrastructure.Data \
  --startup-project src/Hosts/Sergin.MeterMinder.Hosts.All
```

Migrations are applied automatically at startup **only in the Development environment** (the host bootstrap's `UseSerginWebUiAsync` in `Sergin.SharedKernel.Hosts.WebUi` calls every module's `ISerginModule.MigrateAsync`; `UseSerginWebApiAsync` does the same for an API host, when one exists).

**Connection string sourcing**: the value isn't committed. The write side (both `DbContext`s), the read side (`IDbConnectionFactory`), and both design-time factories all read the same `Sergin:ConnectionStrings:Database` key. At runtime it comes from the `Sergin__ConnectionStrings__Database` environment variable (set in `docker-compose.yml`) or user secrets (the host declares a `UserSecretsId`) — `appsettings.json` carries only an empty placeholder and `appsettings.Development.json` carries none. **Gotcha**: the design-time factories load *only* `appsettings.Development.json` (not env vars or user secrets), so `dotnet ef` finds no connection string there unless you add the key to that file locally. `migrations add` scaffolds fine without one; `database update` from the CLI won't connect (startup auto-apply in Development is unaffected).

## Git conventions

- **Commit authorship**: Never add a `Co-Authored-By: Claude` trailer or otherwise attribute commits to Claude/the assistant. Commit under the user's configured git identity only.
- **Work in a git worktree by default.** Create one — without asking — for a new feature, a refactor, an experiment, any change spanning more than a couple of files or more than one session, when the main working tree has uncommitted changes the new work shouldn't mix with, or when two or more agents will run in parallel against this repo (dispatch those with `isolation: "worktree"` so each gets its own copy). Stay in the main working tree for read-only tasks (questions, code search, review), for a single small fix on the branch already checked out, or when the user asks to work in place. State the decision in one line at the start of the task and proceed.
  - **A fresh worktree checks out `src/SharedKernel` and `src/Modules/UserAccess` empty**, because both are submodules. Run `git submodule update --init --recursive` inside the worktree before `dotnet build Sergin.MeterMinder.slnx` or `dotnet test`, or the build fails on unresolvable `ProjectReference`s. `graphify-out/graph.json` is gitignored and likewise absent — rebuild it there (`graphify update .`, then `python .claude/skills/graphify/scripts/graphify_repair.py`) only if the work actually needs graph queries.

## Critical build constraint

`Directory.Build.props` sets `TreatWarningsAsErrors=true`, `AnalysisMode=All`, and enables **SonarAnalyzer.CSharp** + `EnforceCodeStyleInBuild`. Any analyzer warning, style violation, or nullable warning **fails the build**. Nullable and implicit usings are enabled solution-wide. Write code that passes analysis cleanly the first time.

**Central Package Management is on.** `Directory.Packages.props` at the repo root sets `ManagePackageVersionsCentrally=true` and holds every package version as a `<PackageVersion>` entry. `PackageReference` items in the `.csproj` files (and the `SonarAnalyzer.CSharp` reference in `Directory.Build.props`) carry **no `Version` attribute** — a leftover version fails the build with NU1008. When adding a package to a project, reference it version-less (`<PackageReference Include="Foo" />`) and add/update its `<PackageVersion Include="Foo" Version="x.y.z" />` in `Directory.Packages.props`; keep that list alphabetical. The `Microsoft.Extensions.Options` transitive pin in `Sergin.SharedKernel.Hosts.WebApi` uses `PackageReference Update=` (also version-less) with its version centralized. `Directory.Packages.props` is registered in the `/solution-items/` folder of `Sergin.MeterMinder.slnx` alongside `Directory.Build.props`.

## Architecture

### Host / module composition

There is **one runnable host**, composing both modules. It is a `Program.cs` of roughly twenty lines: build an `IReadOnlyCollection<ISerginModule>` (`[new DeviceManagementModule(), new UserAccessModule()]`), hand it to the presentation's bootstrap before `Build()`, hand it back after. Adding a module to the host = one `ProjectReference` + one element in that collection, plus a direct reference to the module's Blazor RCL (see below).

- **`Sergin.MeterMinder.Hosts.All`** — the runnable Blazor Server UI ("all-in-one" host): `builder.AddSerginBlazorApp(modules)`, then `await app.UseSerginWebUiAsync<App>(modules)`, where `App` is the host's own root component (`Components/App.razor`, with `Components/Routes.razor` injecting `SerginUiModuleCatalog` for `AdditionalAssemblies`). Interactive server render mode; no WASM.
- **`Sergin.SharedKernel.Hosts`** — Aspire service defaults (`AddServiceDefaults`: OpenTelemetry, health checks, resilience, service discovery) **plus `AddSerginCore`** — see below.
- **`Sergin.SharedKernel.Hosts.WebApi`** — Web API bootstrap (`SerginWebApiExtensions`, namespace `Microsoft.Extensions.Hosting`): `AddSerginWebApi` adds OpenAPI and `IHttpContextAccessor`, registers `IUserContextFactory` as the `HttpContext`-derived `InternalUserContextFactory`, then calls `AddSerginCore`; `UseSerginWebApiAsync` migrates every module (Development only), maps each `ISerginWebApiModule`'s endpoints under `MapGroup(module.Schema)`, then maps OpenAPI and (Development-only) Scalar. **Compiled but currently unhosted** — no project calls it since the API host was dropped. It is kept working on purpose; see the Overview.
- **`Sergin.SharedKernel.Hosts.WebUi`** — Blazor bootstrap (`SerginWebUiExtensions`, same `Microsoft.Extensions.Hosting` namespace). See "The Blazor UI host" below.
- **Modules** live under `src/Modules/<ModuleName>/`: currently **`DeviceManagement`** (schema `dm`) and **`UserAccess`** (schema `ua`). A module is wired into hosts through its **`<Module>Module` class** (in the `Sergin.<Module>` composition project, no suffix), implementing the contracts it exposes from `Sergin.SharedKernel.Modules`. `ISerginModule` is the core contract — `Schema`, `ApplicationAssembly`, `AddServices` (calls the generic `AddModuleDbContext<TContext, TIContext, TIUnitOfWork>` helper plus per-aggregate `Add<X>Dependencies()`), `MigrateAsync` — and two capability interfaces extend it:
  - **`ISerginWebApiModule`** adds `MapEndpoints(RouteGroupBuilder)` (per-aggregate `Map<X>Endpoints()`).
  - **`ISerginWebUiModule`** adds `UiAssembly` (the assembly holding the module's routable Razor components — **never `ApplicationAssembly`**, which is deliberately UI-free) and `NavItems` (`IReadOnlyCollection<SerginNavItem>`; `SerginNavItem` is `(Label, Href, Icon, Order)`, with `Icon` a plain `string` so the contract leaf stays free of any UI library — the modules currently pass MudBlazor `Icons.Material.*` constants into it).

  **One class per module implements all its capabilities** — both `DeviceManagementModule` and `UserAccessModule` are declared `: ISerginWebApiModule, ISerginWebUiModule` — and which capabilities actually run is the host's choice: the UI host only ever reads `UiAssembly`/`NavItems`, and with no API host today nothing calls `MapEndpoints` at all. Keep both implemented anyway; that is exactly what makes re-adding an API host cheap. Each module has its own `CLAUDE.md` (`src/Modules/<Module>/CLAUDE.md`) covering aggregate-specific details (implemented feature slices, quirks, unfinished pieces) that don't belong here.

**`AddSerginCore` is the presentation-agnostic half of both bootstraps.** It lives in `Sergin.SharedKernel.Hosts` (`SerginCoreExtensions`, section name `Sergin`) and registers everything that doesn't depend on how the app is presented: MediatR scanning every module's `ApplicationAssembly`, the pipeline behaviors, the event dispatcher/interceptor, `IDbConnectionFactory`, the scoped `IUserContext` resolved from whatever `IUserContextFactory` is registered, the localizer, and the `module.AddServices(...)` loop (guarded against two modules claiming the same schema).

**`AddSerginCore` deliberately does *not* register an `IUserContextFactory`** — that is the one registration left host-shaped, because an API host derives the user from `HttpContext` while the UI host derives it from configuration. **Every host must register its own `IUserContextFactory` *before* calling `AddSerginCore`**; `AddSerginWebApi` and `AddSerginBlazorApp` each do exactly that. A new host that forgets it will fail to resolve `IUserContext`.

**Both bootstraps call `AddSerginCore` internally, so a single process cannot call both today.** Doing so would run `AddSerginCore` twice (double-registering every `DbContext` and MediatR handler) and register two `IUserContextFactory` implementations, where last-wins would silently hand the API the UI's config-driven dev user. Re-adding an API therefore means a **separate host project**, not a second `Add…` call in this one — unless `AddSerginCore` is first made idempotent and the factory choice hoisted to the caller.

### The Blazor UI host

`AddSerginBlazorApp(modules)` (in `Sergin.SharedKernel.Hosts.WebUi`) adds Razor Components with interactive server rendering, calls `AddSerginBlazorKit()` (MudBlazor services + `ISerginUiDispatcher` + `IUiErrorPresenter`), binds `Sergin:DevUser` to `DevUserOptions`, registers `ConfiguredUserContextFactory` as the `IUserContextFactory`, calls `AddSerginCore(modules)`, and finally registers two shell singletons: a `SerginUiModuleCatalog` built from `modules.OfType<ISerginWebUiModule>()`, and a `SerginHome` built from the optional `configureHome` callback. `UseSerginWebUiAsync<TRootComponent>(modules)` runs the route-prefix guard, migrates every module (Development only), then `UseAntiforgery()` / `MapStaticAssets()` / `MapRazorComponents<TRootComponent>().AddAdditionalAssemblies(catalog.RoutableAssemblies).AddInteractiveServerRenderMode()`.

- **The UI host refuses to start outside Development.** `AddSerginBlazorApp` throws an `InvalidOperationException` on the first line if `builder.Environment.IsDevelopment()` is false, naming the environment and telling you to implement a real `IUserContextFactory` — because this host has **no authentication at all**: every request runs as one user read from `Sergin:DevUser` in `appsettings.json` (`Id`, `UserName`, `FirstName`, `LastName`, `Email`, `Permissions`). The UI host's `appsettings.json` currently grants `permission.dm.devices.read` and `permission.ua.users.read` — the two the UI actually exercises. There is a third `[RequiredPermissions]` slice, `permission.dm.manufacturers.read` on `GetManufacturerByIdQueryCommand`, deliberately **not** granted: no Manufacturers UI exists, and `CreateDevicePage` reaches manufacturers only through the *list* query, which carries no attribute (see the list-query gap below). Grant it if a manufacturer detail page is ever added. Drop one and `PermissionCheckPipelineBehavior` returns `Error.Forbidden()`, which that module's detail page hands to `IUiErrorPresenter.Present` and renders through `SerginProblemPanel` instead of the record. This is also the reason `ASPNETCORE_ENVIRONMENT: Development` in `docker-compose.yml` is mandatory for this service, not merely convenient.
- **The app title comes from `Sergin:ApplicationName`**, bound to `SerginApplicationOptions` (in `Sergin.SharedKernel.Presentation`, so it is presentation-agnostic and a future API host could reuse it for an OpenAPI title). `SerginMainLayout` renders it in the MudBlazor app bar — it used to hard-code `Sergin`. Omit the key and the C# default `"Sergin Application"` applies; set it blank and startup fails naming the key, via the same `IValidateOptions<T>` pattern as `DevUserOptions`. The layout also emits it as a default `<PageTitle>`, but that is only a **fallback**: `HeadOutlet` keeps the last title rendered and the body renders after the layout, so every page's own `<PageTitle>` (`Devices`, `New user`, …) still wins. There is no title *composition* (`Devices · Sergin Application`) — Blazor has no built-in mechanism for it, and adding one would mean touching every page.
- **The site root is a slot, not a page, and the shell owns the route.** `SerginHomePage` (`@page "/"`, in `Sergin.SharedKernel.Presentation.Blazor`) renders whatever `SerginHome.ComponentType` points at through `<DynamicComponent>`; a host chooses that component with `builder.AddSerginBlazorApp(modules, configureHome: home => home.UseComponent<MyDashboard>())`, and with no callback falls back to `SerginWelcome`, SharedKernel's app-agnostic placeholder. **This host registers `Components/MeterMinderHome.razor`** — the app name from `Sergin:ApplicationName` plus a short description of what the product is. Put product copy there, never in `SerginWelcome`, which every host on the platform shares. Four things about the shape are deliberate. **A host must not declare its own `@page "/"`** — supplying a custom root is what `UseComponent<T>()` is for, and a second `"/"` template makes the routes ambiguous at router build. The shell's assembly is in `SerginUiModuleCatalog.RoutableAssemblies` so both the endpoint mapping *and* a host's `Routes.razor` (`Router.AdditionalAssemblies`) see the route — omit either and `/` works on first load but not on in-app navigation; the guard walks `catalog.Modules`, not that list, so `"/"` is exempt from the `/{schema}/` rule. `SerginHome` is a plain singleton rather than `IOptions<T>`: composed in code, bound to no configuration key, so it follows `SerginUiModuleCatalog`'s precedent, not `DevUserOptions`'. And `UseComponent<T>` constrains to `IComponent, new()` — Blazor instantiates components through `Activator.CreateInstance`, so a component with a primary constructor would fail at first render; the constraint makes that a compile error. `UseNavItem(label, icon, order)` takes **no href**: the path is fixed by the `@page` template, so anything passed could only repeat or contradict it, and a contradiction is a nav entry that 404s. `SerginNavMenu.OnInitialized` merges the entry in and switches an href of `SerginHome.RootPath` to `NavLinkMatch.All` — under the menu's default `Prefix`, `/` is a prefix of every route and Home would render active on every page. `HomeNavLink_IsNotActive_OnAModulePage` guards that.
- **A bad `Sergin:DevUser` key fails startup with the offending key and value named.** `DevUserOptions` is registered `.Bind(...).ValidateOnStart()` alongside an `IValidateOptions<DevUserOptions>` (`DevUserOptionsValidator`) that delegates to `DevUserOptions.Validate(out string failure)` — so instead of a generic "options validation failed" you get e.g. `Sergin:DevUser:Permissions contains 'totally-invalid', which is not a valid permission: ...`, or `Sergin:DevUser:Id must be a non-empty GUID.`. This is the point of the custom validator over a `.Validate(predicate)` lambda; keep new keys validated the same way.
- **Every module page route must start with `/{schema}/`** — `/dm/devices`, `/ua/users/{Id:guid}`, `/ua/users/new`. `UseSerginWebUiAsync` enforces it before anything else: it reflects over every `ISerginWebUiModule.UiAssembly` for exported `IComponent`s carrying a `[Route]`, and throws at startup listing each offending component's full type name and template. **The reason there is a guard rather than central prefixing**: `@page` templates are compile-time constants, so there is no `MapGroup(schema)` equivalent for Razor routes the way there is for minimal-API endpoints. The prefix has to be written into every `@page` string by hand, so it's checked at startup instead.
- **The UI host csproj references each module's `Presentation.Blazor` RCL directly, and those references are not redundant.** Static web assets (`_content/...`) propagate only through projects that import `Microsoft.NET.Sdk.StaticWebAssets`. The module composition roots (`Sergin.MeterMinder.DeviceManagement`, `Sergin.UserAccess`) are plain `Microsoft.NET.Sdk`, so the chain host → composition root → RCL is silently broken at the middle hop (`ResolveReferencedProjectsStaticWebAssetsConfiguration` probes with `SkipNonexistentTargets="true"`, so nothing warns). Without the direct references, `_content/MudBlazor/MudBlazor.min.css` 404s and the UI renders unstyled. Verified empirically: with them in place it serves 200. Adding a UI-bearing module to this host therefore means **two** `ProjectReference`s, not one — the composition root and the RCL — and the csproj carries a comment saying so.

### Per-module project layering

A module is split into projects that enforce Clean Architecture dependency direction. **`src/Modules/UserAccess/**/Users/**` is the canonical reference implementation** — it's the most complete and current slice; when in doubt about the "right" shape for a new feature, read the matching file there before writing the new one.

- **`.Domain`** — aggregates/entities, strongly-typed IDs, repository interfaces. Depends only on `SharedKernel.Domain`. Aggregates are built via a private/parameterless constructor + a `static Create(...)` factory method (e.g. `User.Create(UserName)`, `Device.Create(...)`) — no public setters; mutate via named methods on the aggregate (e.g. `User.Deactivate()`).
  - ID generation always uses `Guid.CreateVersion7()`, never `Guid.NewGuid()` — e.g. `new UserInternalId(Guid.CreateVersion7())`; `RowVersion.Create()` follows the same call.
  - `Create(...)` returns via **object-initializer syntax** against the private parameterless constructor (`new User { Id = ..., UserName = userName, IsActive = true }`), not a parameterized constructor call — match this shape for new aggregates.
  - Strongly-typed IDs/value objects are declared as trailing `sealed record`s in the **same file** as their owning aggregate (e.g. `UserInternalId` and `UserName` both live in `User.cs`), not split into separate files.
- **`.Application`** — MediatR commands/queries + handlers, `IUnitOfWork`, query repository interfaces. Feature folders hold the full slice under `<Aggregate>/Commands/<Feature>/...` — **queries live under `Commands/` too**, not a separate `Queries/` folder; don't invent one.
- **`.Infrastructure`** — write-side repositories (EF Core) and read-side query repositories (raw SQL via `IDbConnectionFactory`).
  - Generic PK lookup uses the array-args overload: `dbContext.Set<T>().FindAsync([id, cancellationToken], cancellationToken: cancellationToken)`, not `FindAsync(id, cancellationToken)`.
  - Aggregate-specific lookups (`GetByUserName`, `GetByDeviceId`) use `SingleOrDefaultAsync(x => x.Field == value, cancellationToken)` and are added directly to the repository interface (`IUserRepository`, `IDeviceRepository`) — this is the precedent for adding a lookup beyond generic CRUD, rather than reaching into EF from the Application layer.
- **`.Infrastructure.Data`** — the module's `DbContext`, `IEntityTypeConfiguration`s, value converters, and migrations.
  - Value converter template for a wrapped value object — copy this skeleton rather than re-deriving it:
    ```csharp
    internal sealed class FooConverter : ValueConverter<Foo, TPrimitive>
    {
        private static readonly ConverterMappingHints defaultHints = new();
        public FooConverter() : this(null) { }
        public FooConverter(ConverterMappingHints? mappingHints)
            : base(x => x.Value, x => new Foo(x), defaultHints.With(mappingHints)) { }
    }
    ```
    For a **nullable** wrapped value object, both type params and both conversion expressions get a null ternary instead (`ValueConverter<Foo?, TPrimitive?>`, `x => x == null ? null : x.Value` / `x => x == null ? null : new Foo(x)`) — see `ManufacturerAddressConverter` as the reference example.
- **`.Presentation.WebApi`** — minimal-API endpoints implementing `IEndpoint`.
- **`.Presentation.Blazor`** — *optional*; present for both modules today. A Razor Class Library (`Microsoft.NET.Sdk.Razor`, `FrameworkReference Microsoft.AspNetCore.App` + `PackageReference MudBlazor`) holding the module's routable pages, organized per aggregate as `<Aggregate>/Pages/*.razor` + `*.razor.cs` and `<Aggregate>/Models/*.cs`. It also carries a `<Module>BlazorAssemblyReference` (what `UiAssembly` returns) and a `<Module>Navigation` static class exposing `IReadOnlyCollection<SerginNavItem> Items` (what `NavItems` returns). It references `SharedKernel.Modules`, `SharedKernel.Presentation.Blazor`, and the module's own `.Application` — **not** `.Infrastructure`; pages reach handlers through MediatR, never a repository.
- **`Sergin.<Module>`** (no suffix) — the module's composition root that references all the above and hosts the module's `<Module>Module` class.

### Adding a new feature

Use the **`/add-feature`** skill (`.claude/skills/add-feature/SKILL.md`) to scaffold a new CQRS vertical slice (command or query) — it encodes the full file-by-file layout (Application handler, Infrastructure repository wiring, Presentation endpoint, optional Blazor page, DI/route registration) following the UserAccess/Users reference pattern. Don't hand-roll the layout from memory; invoke the skill or read it for the authoritative shape. Use **`/add-module`** (`.claude/skills/add-module/SKILL.md`) for a whole new module, including the optional `.Presentation.Blazor` RCL and `ISerginWebUiModule` wiring.

### CQRS split

- **Writes**: endpoint → MediatR `ICommand` → `ICommandHandler` → domain `AggregateRoot` factory/behavior method → `IRepository` (EF Core) → `IUnitOfWork.SaveChangesAsync`. Each module has its own unit of work (e.g. `IDeviceManagementUnitOfWork`, `IUserAccessUnitOfWork`), implemented by its `DbContext`.
- **Reads**: query handlers use dedicated query-repository interfaces (`I<Feature>QueryRepository`) backed by **raw SQL through `IDbConnectionFactory`** (Dapper-style `QuerySingleOrDefaultAsync` / `QueryMultipleAsync`), bypassing EF entirely for read models. A query handler maps a `null` result to `Error.NotFound()`.
  - Each query method opens its own `using DbConnection connection = await connectionFactory.CreateConnectionAsync();` — connections aren't shared or injected, one per call.
  - SQL is a raw `"""..."""` string literal; snake_case columns are aliased to match the response record's exact property casing so Dapper's binder matches (`SELECT user_name AS userName FROM ua.users WHERE id = @Id;`).
  - List queries batch **two** statements through one `QueryMultipleAsync` call — a `SELECT count(*) ...;` followed by the paged `SELECT ... LIMIT @PageSize OFFSET @Offset;` — then read them off the same `GridReader` (`ReadSingleAsync<int>()` then `ReadAsync<TItem>()`), wrapped as `new ListQueryResponse<TItem>(list, count)`. Both `UserQueryRepository` and `DeviceQueryRepository` use this exact shape.
  - The not-found idiom is **bare `Error.NotFound()`** with no custom code/description. Since `ApiProblemResults` localizes on `error.Code`, every not-found response across the API currently renders identical generic text regardless of aggregate — don't invent a per-feature `Error.NotFound(code, description)` without first checking the localization resources support it.

### CQRS structural gotchas

- **List-query features have no dedicated request record.** `Get<Aggregate>ListQueryCommandHandler` implements `IListQueryHandler<TItem>` directly against the shared generic `ListQuery<TItem>` (built by `ListQueryRequestModel.ToListQuery<TItem>()` in the endpoint) — there is no `Get<Aggregate>ListQueryCommand` type to attribute. This is *why* `[RequiredPermissions]` can't be applied to any `GetList` slice today — a structural gap in the shared generic type, not an inconsistently-applied convention. If a list feature needs authorization, that requires introducing a feature-specific list-query type first; there's no existing precedent for that shape, so flag it to the user rather than guessing.
- **`.Produces<TResponse>()` is called on Create/GetList endpoints but omitted on GetOne endpoints**, consistently in both modules. Match whichever family you're extending rather than "completing" the other.
- **Endpoint route strings never include the schema segment** (`/users`, not `/ua/users`) — the schema prefix is added exactly once by the host bootstrap (`app.MapGroup(module.Schema)` inside `UseSerginWebApiAsync`).
- **No FK-existence check on write.** `CreateDeviceCommandHandler` inserts a `Device` referencing `ManufacturerId` without checking the manufacturer exists — a bad ID surfaces as a raw Postgres FK-violation exception, not an `ErrorOr` result. This is the current state of the only cross-aggregate FK in the codebase, not an established pattern to replicate — no existing slice shows how to convert this into a friendly `ErrorOr` error.

### Cross-cutting conventions

- **Results**: handlers return `ErrorOr<T>` (the `ErrorOr` library, global-imported). Endpoints call `.ToApiResult()` to convert to an `IResult`/ProblemDetails.
- **MediatR pipeline behaviors** (registered in `Sergin.SharedKernel.Hosts.WebApi`'s `AddSerginWebApi`, order matters):
  1. `PermissionCheckPipelineBehavior` — enforces `[RequiredPermissionsAttribute]` on any `IBaseCommand` (covers both commands and queries) against `IUserContext`.
  2. `ValidationPipelineBehavior` — runs an optional FluentValidation `IValidator<TRequest>` if one is registered.
- **Permissions**: apply `[RequiredPermissions("permission.<schema>.<resource>.<action>")]` to a command/query record when it needs authorization, e.g. `"permission.ua.users.read"`, `"permission.dm.devices.read"`. This is opt-in per slice, not universally applied today — most commands have no attribute yet, so don't assume its absence on an existing handler is an oversight to fix incidentally.
- **Validation**: FluentValidation is wired but optional — no `AbstractValidator<T>` exists in the codebase yet. Add one alongside a command/query only when the feature actually needs input validation beyond what the domain factory already guards; it's picked up automatically by `ValidationPipelineBehavior` if registered.
- **Domain events**: `AggregateRoot` supports `Raise(IDomainEvent)` / `DomainEvents` / `ClearDomainEvents()`, and `EventDispatcherInterceptor` dispatches + clears them on EF `SaveChanges` — but **no aggregate currently calls `Raise(...)`**. This is present-but-unused infrastructure; follow it when a feature needs to react to a domain change, don't assume events are already flowing anywhere. Two more SharedKernel building blocks are in the same "present-but-unused" state: `Ardalis.GuardClauses` is globally imported in every `.Domain` project, but no `Create`/value-object constructor actually calls a guard clause; and `RowVersion` exists for optimistic concurrency, but no aggregate carries one today.
- **Naming/sealing conventions**: response records are `<Feature>CommandResponse` for commands (`CreateUserCommandResponse(Guid Id)`) and `<Aggregate>QueryResponse` for a single-item query (`UserQueryResponse`, `DeviceQueryResponse` — not `Get<Aggregate>ByIdResponse`); list items are `Get<Aggregate>ListItem`. GetOne query/request records keep the blended `Get<Aggregate>ByIdQueryCommand` suffix even though they implement `IQuery<T>` — match it, don't rename to `...Query`. Application-layer commands/queries/responses are always `sealed record`; Presentation-layer `[FromBody]` request DTOs (`NewUserModel`, `NewDeviceModel`) are plain `record`, not sealed. Handler classes are `internal sealed class`; **endpoint classes are `internal class`, never sealed** — consistent across every existing endpoint in both modules. When one concrete class implements several one-per-feature query interfaces, register it against **each** interface with its own `AddTransient<IInterface, Impl>()` call, not a single `AddTransient<Impl>()` with forwarding.
- **Strongly-typed IDs**: `record` wrappers (e.g. `DeviceId(string)`, `UserInternalId(Guid)`, `DeviceIntenralId(Guid)`) mapped to columns via EF value converters. Note the existing misspelling `DeviceIntenralId` is the real type name — match existing spelling when referencing it.
- **Database schema**: each module maps to its own Postgres schema (`DeviceManagement` → `dm`, `UserAccess` → `ua`) via `HasDefaultSchema` (set in the module's `DbContext`) + a per-schema migrations history table (configured by the shared `AddModuleDbContext` helper that `<Module>Module.AddServices` calls). `UseSnakeCaseNamingConvention()` maps PascalCase members to snake_case columns.
- **Endpoints**: implement `IEndpoint.MapEndpoint`, are instantiated and mapped in the aggregate's `<Aggregate>InstallationExtensions.Map<Aggregate>Endpoints`, called from the module's `<Module>Module.MapEndpoints`, and grouped under a route prefix.
- **User context**: `InternalUserContextFactory` currently returns a `SYSTEM`/`ANONYMOUS` stub user (real auth is commented out / not yet wired).
- **Local variable typing**: declare a local as the narrowest interface its actual usage needs, not the first concrete type that happens to compile — e.g. `IReadOnlyCollection<T>` instead of `List<T>` when the variable is only ever handed to something expecting that interface. Collection expressions (`[.. ...]`) can target an interface directly since C# 12; the compiler picks the backing implementation, so narrowing costs nothing. Reference example: `UserQueryRepository`/`DeviceQueryRepository`/`ManufacturerQueryRepository`'s `GetListAsync` materialize Dapper's `IEnumerable<T>` result as `IReadOnlyCollection<TItem> list = [.. await res.ReadAsync<TItem>()];` before passing it to `ListQueryResponse<TData>`'s constructor — not `List<T>`.
- Each project has a `GlobalUsings.cs`; check it before adding `using` statements that may already be global. Notably: `.Domain` projects globally import `ErrorOr` and `Ardalis.GuardClauses`; `.Application` projects import `ErrorOr`, `Sergin.SharedKernel.Domain`, `Sergin.SharedKernel.Application`, and the module's own `.Domain`; `.Presentation.WebApi` projects import `ErrorOr`, `MediatR`, `Sergin.SharedKernel.Presentation*`; `.Infrastructure` projects import `Dapper` and `static Dapper.SqlMapper` (so raw `QuerySingleOrDefaultAsync` etc. are callable unqualified). `.Presentation.Blazor` projects have **two** import files and both matter: `GlobalUsings.cs` (`ErrorOr`, `MediatR`, `Sergin.SharedKernel.Application`) covers the `.razor.cs` code-behind, while `_Imports.razor` covers the markup and additionally pulls in `MudBlazor`, `Sergin.SharedKernel.Presentation.Blazor.Dispatching`/`.Errors`, and each feature's Application namespace. A `@using` added to a `.razor` file individually is a smell — put it in `_Imports.razor`.

### Blazor UI conventions

These apply to every `.Presentation.Blazor` project and to the shared components in `Sergin.SharedKernel.Presentation.Blazor`.

- **Inject `ISerginUiDispatcher`, never `ISender`/`IMediator`.** In Blazor Server, "scoped" is the SignalR **circuit's** lifetime — as long as the user's tab stays open — not a request's. Resolving `ISender` straight off the circuit's provider would share one `DbContext` for that entire time, producing an unbounded change tracker, stale first-level-cache reads, and "a second operation was started on this context" the moment two components render in parallel. `ScopedSerginUiDispatcher.SendAsync` opens a fresh `IServiceScope` per call — the same lifetime a single HTTP request would get — resolves `ISender` inside it, and disposes it on return. It is registered as a **singleton** (it holds only the root `IServiceScopeFactory`), which is correct and not a bug to "fix" to scoped.
  - Single-item send: `await Dispatcher.SendAsync(new GetDeviceByIdQueryCommand(Id))` → `ErrorOr<TResponse>`.
  - List send: `await Dispatcher.SendListAsync<GetDeviceListItem>(pageSize, pageIndex, cancellationToken)` → `ErrorOr<ListQueryResponse<TItem>>`. This extension exists because list features have no dedicated command type (see "CQRS structural gotchas") — it is the UI-side equivalent of `ListQueryRequestModel.ToListQuery<TItem>()`, minus the `[FromQuery]` binding attributes. **`pageIndex` is 1-based; MudBlazor's `TableState.Page` is 0-based** — every list page passes `state.Page + 1` and says so in a comment.
- **`.razor` files are markup-only; every line of C# lives in the matching `.razor.cs` `partial class`.** Code inside an `@code { }` block compiles through the Razor source generator into output the analyzer pipeline (`TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer.CSharp) does not gate — an unaudited hole in a repo where analyzers gate every other line. Every Blazor file in the repo follows this; there are **zero** `@code` blocks today. Keep it that way.
- **Error rendering, two shapes, both via `IUiErrorPresenter`** (`MudUiErrorPresenter`, which maps an `Error` through the same `SerginProblemFactory` the API uses, so API and UI render identical text for a given `error.Code`):
  - `ErrorPresenter.Notify(result.FirstError)` — transient MudBlazor snackbar. Used by list pages and by create/mutate submits, where the page still has something to show.
  - `problem = ErrorPresenter.Present(result.FirstError)` assigned to a `SerginProblem?` field bound to `<SerginProblemPanel Problem="problem" />` — an inline alert. Used by detail pages, where the failure *is* the whole page. Set `problem = null` on the success path.
- **Route templates carry the schema prefix** (`@page "/dm/devices"`), enforced at startup — see "The Blazor UI host".
- **Page code-behinds are `public sealed partial class`** with `[Inject] private ... { get; set; } = default!;` properties (not primary-constructor injection) and `[Parameter] public Guid Id { get; set; }` for route parameters. Form models are `public sealed class` under `<Aggregate>/Models/` with `System.ComponentModel.DataAnnotations` attributes (`NewDeviceFormModel`, `NewUserFormModel`) — separate from the Application-layer command and from the WebApi `New<X>Model` request DTO.
- **List pages are `MudTable` with `ServerData` and paging only** — no sort/filter/search controls. Not because the shared query type lacks the fields: `ListQuery` *does* carry `Term`, `Filtering`, and `Sorting`. The plumbing below it drops them. `ListQueryRequestModel.ToListQuery<T>()` forwards `Term` but not `Filtering`/`Sorting`; `SendListAsync` forwards none of the three; and no query repository reads any of them — `DeviceQueryRepository.GetListAsync` and `UserQueryRepository.GetListAsync` bind `PageSize`/`Offset` only, with a hardcoded `ORDER BY id`. So a UI control bound to any of them would silently do nothing, which is worse than omitting it; `DeviceListPage.razor` says as much in a trailing `MudText`. Wiring sort/filter through is a real read-side feature, not a UI-only change.

## SharedKernel and UserAccess are separate repos, mounted as submodules

- **`src/SharedKernel/`** ([Sergin.SharedKernel](https://github.com/poursh/Sergin.SharedKernel)) — framework-level building blocks shared across modules, mirroring the module layering: `.Domain` (`AggregateRoot`, `Entity`, guard clauses, `RowVersion`), `.Application` (command/query abstractions, pipeline behaviors, security, localization, time), `.Infrastructure` + `.Infrastructure.Data.EFCore` (`SerginDbContext` base, `IDbConnectionFactory` implementations, interceptors), `.Presentation` (the presentation-agnostic `SerginProblem`/`SerginProblemFactory` both front ends map errors through), `.Presentation.WebApi` (`IEndpoint`, result mapping to ProblemDetails), and `.Presentation.Blazor` (`AddSerginBlazorKit`, `ISerginUiDispatcher`, `IUiErrorPresenter`, `SerginUiModuleCatalog`, and the shell components `SerginMainLayout`/`SerginNavMenu`/`SerginProblemPanel`), plus the three host-bootstrap projects `Hosts` (`AddServiceDefaults`, `AddSerginCore`), `Hosts.WebApi`, and `Hosts.WebUi`. Prefer extending these over duplicating primitives in a module. Fully standalone-buildable on its own (`dotnet build Sergin.SharedKernel.slnx` from inside that repo) — it has zero dependencies outside itself. See its own `.claude/CLAUDE.md` for the full reference.
- **`src/Modules/UserAccess/`** ([Sergin.UserAccess](https://github.com/poursh/Sergin.UserAccess)) — the UserAccess module. **Embed-only**: that repo deliberately has no solution file or `Directory.Build.props`/`Directory.Packages.props` of its own — it only compiles once mounted here (or in any other host that also provides a `Sergin.SharedKernel` submodule at a matching relative path). This is why `git submodule update --init --recursive` is required before `dotnet build Sergin.MeterMinder.slnx` works from a fresh clone. See its own `.claude/CLAUDE.md` for module-specific conventions.

Both are mounted at the *same relative paths* they occupied before the split (`src/SharedKernel/`, `src/Modules/UserAccess/`), which is what lets every `ProjectReference` in this repo and in UserAccess's own `.csproj` files resolve without any path rewrites — MSBuild's `Directory.Build.props`/`Directory.Packages.props` auto-discovery walks up the physical directory tree and doesn't care that a submodule boundary sits partway up.
# graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

**The graph is not committed — a fresh clone has none, and every rule below silently does nothing until you build it.** Only `graphify-out/cache/semantic/` is tracked (28 files); `graph.json`, `graph.html`, `GRAPH_REPORT.md`, the AST cache, and the manifests are all gitignored, because they re-derive from source in seconds while `graph.json` alone is 2.3 MB that churns on every code edit and reshuffles node ids on rebuild, making it unmergeable in practice. Build it on first clone with:

```bash
graphify update .                                          # rebuilds from source; the committed semantic cache replays, so this costs zero tokens
python .claude/skills/graphify/scripts/graphify_repair.py   # re-adds the edges the extractor cannot produce (see below)
```

The tracked cache is keyed by a hash of `.claude/skills/graphify/references/extraction-spec.md`. Editing that file invalidates every entry, and the next build re-runs the LLM extraction (~510K input tokens across parallel subagents) instead of replaying it — so treat the spec as expensive to change, and commit the regenerated cache alongside any edit to it.

Rules:
- **graphify** (`.claude/skills/graphify/SKILL.md`) - any input to knowledge graph. Trigger: `/graphify`
When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
- **Then run `python .claude/skills/graphify/scripts/graphify_repair.py`** from the repo root. Every rebuild — full `/graphify` or incremental `graphify update .` — drops two sets of edges the extractor cannot produce, and the repair script adds them back deterministically (no LLM, no cost, idempotent, safe to re-run). Without it: the doc-extracted nodes and the code nodes form two disjoint graphs, so no query can reach a Markdown explanation from the code it describes; and C# extension-method calls resolve to nothing, so `AddSerginCore`, `ToApiResult`, `AddModuleDbContext` and the rest of the host-composition spine appear to have no callers. The script's docstring explains both gaps. It deliberately leaves community assignments alone, so curated community names survive a repair.
