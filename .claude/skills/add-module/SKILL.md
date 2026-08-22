---
name: add-module
description: Scaffold a brand-new module in the Sergin modular monolith — six projects (Domain, Application, Infrastructure, Infrastructure.Data, Presentation.WebApi, and the no-suffix composition root) plus an optional Presentation.Blazor RCL, their DbContext/schema/migrations wiring, solution-file entries, and host registration — following the existing UserAccess module as the template. Invoke with /add-module.
disable-model-invocation: false
---

Scaffold a new module for: $ARGUMENTS

Expected input: `<ModuleName> <SchemaName>`, e.g. `/add-module Billing bil`. Ask the user for whatever is missing — don't guess the module name or Postgres schema code. Schema codes in use so far: `dm` (DeviceManagement), `ua` (UserAccess) — pick something short and distinct.

Also ask whether the module needs a **Blazor UI surface**. A module can expose the Web API only (`ISerginWebApiModule`), the Blazor UI only (`ISerginWebUiModule`), or both — both existing modules do both. The UI parts of this skill (project 7 in step 1, the `ISerginWebUiModule` members in step 4, the UI host wiring in step 5) are optional and marked as such; skip them for an API-only module.

This is a much bigger, more error-prone scaffold than a single feature slice (see `/add-feature` for that). Use `src/Modules/UserAccess/**` as the reference implementation for every file below — read the matching file there before writing the new one, and match its style exactly (sealed/internal where UserAccess is sealed/internal, primary constructors, no comments). Do **not** add a first aggregate/feature as part of this skill — that's a separate `/add-feature` step once the module shell builds.

## 1. Create six projects under `src/Modules/<Module>/` (seven with a UI)

The six below are plain `Microsoft.NET.Sdk` (not `.Web`) class libraries — `Directory.Build.props` at the repo root already supplies `TargetFramework`, `Nullable`, analyzers, etc., so none of these csproj files need a `PropertyGroup`. (The optional seventh, `.Presentation.Blazor`, is `Microsoft.NET.Sdk.Razor` — see below.)

| Project | References | GlobalUsings.cs |
|---|---|---|
| `Sergin.<Module>.Domain` | `SharedKernel.Domain` | `global using ErrorOr;` / `global using Ardalis.GuardClauses;` |
| `Sergin.<Module>.Application.Contracts` | `SharedKernel.Application`, `<Module>.Domain` | `global using ErrorOr;` / `Sergin.SharedKernel.Domain` / `Sergin.SharedKernel.Application` — **not** `Sergin.<Module>.Domain` yet (see note below) |
| `Sergin.<Module>.Application` | `SharedKernel.Application`, `<Module>.Domain`, `<Module>.Application.Contracts` | same globals as `.Application.Contracts`, since handlers need the same domain/SharedKernel imports plus the request/response types `.Application.Contracts` now holds |
| `Sergin.<Module>.Infrastructure` | `SharedKernel.Infrastructure`, `<Module>.Application`, `<Module>.Infrastructure.Data` | `global using Dapper;` / `global using static Dapper.SqlMapper;` |
| `Sergin.<Module>.Infrastructure.Data` | `SharedKernel.Infrastructure.Data.EFCore`, `<Module>.Application` | (none needed yet — add if EF namespaces get noisy) |
| `Sergin.<Module>.Presentation.WebApi` | `SharedKernel.Presentation.WebApi`, `<Module>.Application.Contracts` | `global using ErrorOr;` / `MediatR` / `Sergin.SharedKernel.Presentation` / `Sergin.SharedKernel.Presentation.WebApi` / `Sergin.SharedKernel.Presentation.WebApi.Endpoints` |
| `Sergin.<Module>` (composition root, no suffix) | `<Module>.Infrastructure`, `<Module>.Presentation.WebApi`, `SharedKernel.Modules` (+ `<Module>.Presentation.Blazor` if the module has a UI) | (none) |

The composition root's csproj also needs:
```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```
(copy `Sergin.UserAccess.csproj` verbatim as the template — its `ProjectReference`s + this `FrameworkReference`. Note it now carries **four** `ProjectReference`s, not three: `.Infrastructure`, `.Presentation.WebApi`, `.Presentation.Blazor`, and `SharedKernel.Modules`. Drop the `.Presentation.Blazor` one for an API-only module; keep it otherwise, because `<Module>Module` needs the assembly-reference and navigation classes from it.)

**Optional 7th project — `Sergin.<Module>.Presentation.Blazor`** (skip for an API-only module). Copy `Sergin.UserAccess.Presentation.Blazor.csproj` verbatim; it is a Razor Class Library, not a plain class library:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<ItemGroup>
		<FrameworkReference Include="Microsoft.AspNetCore.App" />

		<PackageReference Include="MudBlazor" />
	</ItemGroup>

	<ItemGroup>
		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Modules\Sergin.SharedKernel.Modules.csproj" />
		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Blazor\Sergin.SharedKernel.Presentation.Blazor.csproj" />
		<ProjectReference Include="..\Sergin.<Module>.Application.Contracts\Sergin.<Module>.Application.Contracts.csproj" />
	</ItemGroup>
</Project>
```

It references the module's `.Application.Contracts` (never `.Application` directly, and never `.Infrastructure`) — pages reach handlers through MediatR only, and only need the request/response record shapes `.Application.Contracts` holds. It needs **two** import files, both copied from UserAccess:
- `GlobalUsings.cs` — `global using ErrorOr;` / `MediatR` / `Sergin.SharedKernel.Application` (covers the `.razor.cs` code-behind).
- `_Imports.razor` — `Microsoft.AspNetCore.Components{,.Forms,.Routing,.Web}`, `MudBlazor`, `Sergin.SharedKernel.Application`, `Sergin.SharedKernel.Presentation.Blazor.Dispatching`, `Sergin.SharedKernel.Presentation.Blazor.Errors`, plus one line per feature namespace as pages get added (covers the markup).

No `InternalsVisibleTo` here — unlike endpoints and repositories, the Blazor types are `public` (the host reflects over them to route).

Two small files complete the shell, both copied from `Sergin.UserAccess.Presentation.Blazor/`:
- `<Module>BlazorAssemblyReference.cs` — `public static class <Module>BlazorAssemblyReference { public static readonly Assembly Assembly = typeof(<Module>BlazorAssemblyReference).Assembly; }`. This is what `UiAssembly` returns; **note it is a separate type from `<Module>ApplicationAssemblyReference`** and the two must not be conflated — `ApplicationAssembly` is deliberately UI-free.
- `<Module>Navigation.cs` — `public static class <Module>Navigation { public static IReadOnlyCollection<SerginNavItem> Items { get; } = [ ... ]; }`. Start it empty (`[]`); `/add-feature` adds an entry per list page. A `SerginNavItem` is `(string Label, string Href, string Icon, int Order = 0)`: `Href` must be the schema-prefixed absolute path (`"/bil/invoices"`), `Icon` is a raw string so the contract stays UI-library-free (existing modules pass MudBlazor `Icons.Material.Filled.*` constants), and `Order` sorts across modules with ties broken by `Label` — MeterMinder uses 100, UserAccess 200, so leave gaps.

**Note on the empty `Domain` project**: a C# namespace only exists once some type declares it, and this skill deliberately creates `Sergin.<Module>.Domain` with zero classes. Don't add `global using Sergin.<Module>.Domain;` to the Application project's `GlobalUsings.cs` yet — it won't compile — add that line as part of the first `/add-feature` invocation, once an aggregate under that namespace actually exists.

**`InternalsVisibleTo` — three places, not one.** `<Module>DbContext`, repositories, and endpoints are all `internal`, instantiated only from the composition root, so each of `.Infrastructure`, `.Infrastructure.Data`, and `.Presentation.WebApi` needs a `Properties/AssemblyInfo.cs` granting `[assembly: InternalsVisibleTo("Sergin.<Module>")]` (copy the UserAccess ones verbatim, swap the module name).

## 2. Application-layer plumbing (composition root of DI/MediatR)

In `Sergin.<Module>.Application/`:
- `<Module>AssemblyReference.cs` — note the actual class name is **`<Module>ApplicationAssemblyReference`** (matches `UserAccessApplicationAssemblyReference`, not just `UserAccessAssemblyReference`), wrapping `typeof(...).Assembly` for MediatR scanning.
- `I<Module>UnitOfWork.cs` — `public interface I<Module>UnitOfWork : IUnitOfWork;` (from `Sergin.SharedKernel.Application`).

In `Sergin.<Module>.Application.Contracts/`:
- `<Module>ApplicationContractsAssemblyReference.cs` — `public static class <Module>ApplicationContractsAssemblyReference { public static readonly Assembly Assembly = typeof(<Module>ApplicationContractsAssemblyReference).Assembly; }`, wrapping `typeof(...).Assembly` for `ISerginModule.ContractsAssembly`. **Note this is a third, separate assembly-reference type from both `<Module>ApplicationAssemblyReference` and `<Module>BlazorAssemblyReference`** — don't conflate any of the three.

## 3. Infrastructure.Data: DbContext, design-time factory, schema

In `Sergin.<Module>.Infrastructure.Data/`:
- `<Module>DbContext.cs` — mirror `UserAccessDbContext.cs`:
  ```csharp
  public interface I<Module>DbContext : IDbContext;

  internal sealed class <Module>DbContext(DbContextOptions<<Module>DbContext> options)
      : SerginDbContext(options), I<Module>DbContext, I<Module>UnitOfWork
  {
      public const string Schema = "<schema>";

      protected override void OnModelCreating(ModelBuilder modelBuilder)
      {
          modelBuilder.HasDefaultSchema(Schema);
          modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
          base.OnModelCreating(modelBuilder);
      }
  }
  ```
- `<Module>DbContextDesignTimeFactory.cs` — copy `UserAccessDbContextDesignTimeFactory.cs`, swapping the type name and schema. This is what lets `dotnet ef migrations add` run against `appsettings.Development.json` without the host project.
- No `IEntityTypeConfiguration` / migration yet — those come from the first `/add-feature` slice, once there's an aggregate to map. An empty DbContext with no entities is fine as the initial scaffold; skip step 4 below (EF migration) until a feature adds a table.

## 4. Composition root: `Sergin.<Module>/<Module>Module.cs`

Create `Sergin.<Module>/<Module>Module.cs` — copy `Sergin.UserAccess/UserAccessModule.cs` exactly, renaming `UserAccess` → `<Module>` and swapping the schema/DbContext/assembly-reference types:

- `public sealed class <Module>Module : ISerginWebApiModule, ISerginWebUiModule` (both from `Sergin.SharedKernel.Modules`; drop `ISerginWebUiModule` for an API-only module). Both interfaces extend the core `ISerginModule` — **one class per module implements every capability the module exposes**, and each host picks the ones it cares about.
- `Schema` → `<Module>DbContext.Schema`; `ApplicationAssembly` → `<Module>ApplicationAssemblyReference.Assembly`; `ContractsAssembly` → `<Module>ApplicationContractsAssemblyReference.Assembly`.
- `AddServices` → `services.AddModuleDbContext<<Module>DbContext, I<Module>DbContext, I<Module>UnitOfWork>(configuration, <Module>DbContext.Schema);` plus per-aggregate `Add<X>Dependencies()` calls (none yet on a fresh module).
- `MigrateAsync` → `services.MigrateDbContextAsync<<Module>DbContext>();`
- `MapEndpoints` → per-aggregate `Map<X>Endpoints()` calls (empty method body on a fresh module). *(`ISerginWebApiModule`)*
- `UiAssembly` → `<Module>BlazorAssemblyReference.Assembly` — the Blazor RCL, **never `ApplicationAssembly`**, which is deliberately UI-free. *(`ISerginWebUiModule`)*
- `NavItems` → `<Module>Navigation.Items`. *(`ISerginWebUiModule`)*

Don't add an aggregate-specific `<Aggregate>InstallationExtensions.cs` (like `UserInstallationExtensions.cs`) as part of this skill — that's created by the first `/add-feature` invocation for this module.

## 5. Wire into the host

There is only **one** host — `src/Hosts/Sergin.MeterMinder.Hosts.All/`, the Blazor Server UI. (The Web API host was dropped; see the repo `CLAUDE.md`. Implement `ISerginWebApiModule` on the module class anyway — nothing calls `MapEndpoints` today, but keeping the capability whole is what makes re-adding an API host a ~20-line `Program.cs`.)

- `Program.cs` — add `using Sergin.<Module>;` and one element to the modules collection: `IReadOnlyCollection<ISerginModule> modules = [new DeviceManagementModule(), new UserAccessModule(), new <Module>Module()];` — nothing else. `AddSerginBlazorApp`/`UseSerginWebUiAsync<App>` pick up the new `ISerginWebUiModule` automatically: its `UiAssembly` joins `AddAdditionalAssemblies`, its `NavItems` join the shared nav menu, and the bootstrap loops handle MediatR, DI, and migrations.
- csproj — add `<ProjectReference Include="..\..\Modules\<Module>\Sergin.<Module>\Sergin.<Module>.csproj" />`. If the module has a UI, add a **second** reference to `Sergin.<Module>.Presentation.Blazor` directly. The second looks redundant and is not. Static web assets (`_content/...`) propagate only through projects importing `Microsoft.NET.Sdk.StaticWebAssets`; the composition root is a plain `Microsoft.NET.Sdk`, so the chain host → composition root → RCL breaks silently at the middle hop (`ResolveReferencedProjectsStaticWebAssetsConfiguration` probes with `SkipNonexistentTargets="true"`, so nothing warns — the CSS just 404s at runtime and the UI renders unstyled). The existing csproj carries a comment saying exactly this; add the new reference next to it.
- The host is Development-only and has no authentication: every request runs as the user configured under `Sergin:DevUser` in its `appsettings.json`. If the new module's slices carry `[RequiredPermissions]`, **add those permission strings to that `Permissions` array** or the pages will render a Forbidden problem panel. An invalid entry there fails startup by design, naming the exact key and value.

Route templates in the new module's pages must start with `/<schema>/` — a startup guard in `UseSerginWebUiAsync` reflects over every module's `UiAssembly` and throws, naming any component whose `@page` doesn't. Razor `@page` templates are compile-time constants, so there is no `MapGroup(schema)` equivalent to prefix them centrally the way minimal-API endpoints are prefixed.

## 6. Register in `Sergin.MeterMinder.slnx`

Add a new `<Folder Name="/src/Modules/<Module>/">` (mirroring the DeviceManagement/UserAccess folders) listing the five non-Presentation projects, plus a `<Folder Name="/src/Modules/<Module>/Presentation/">` for the presentation projects — both `.Presentation.WebApi` and (if present) `.Presentation.Blazor`. That split (presentation projects sit in their own subfolder) matches both existing modules.

## After scaffolding

1. Build to confirm the empty module compiles and wires up cleanly — this repo treats every analyzer/style warning as a build error:
   ```
   dotnet build Sergin.MeterMinder.slnx
   ```
2. Run `dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.All` (http://localhost:5002) and confirm startup doesn't throw — an empty `NavItems` and a page-less `UiAssembly` are both fine at this stage; what this proves is that the duplicate-schema guard, the route-prefix guard, and `Sergin:DevUser` validation all pass.
4. Hand off to `/add-feature <Module> <Aggregate> <Feature> command` for the module's first vertical slice — that step is what actually creates the aggregate, the `IEntityTypeConfiguration`, the first EF migration, and (optionally) the module's first pages and nav entry.
