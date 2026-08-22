# Application Contracts Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split each module's MediatR command/query request and response records out of its `.Application` project into a new `.Application.Contracts` project, so presentation layers can depend on request/response shapes without pulling in handlers, repository interfaces, or `IUnitOfWork`.

**Architecture:** Pure structural move (Approach B from the spec) across three git repos — `Sergin.SharedKernel` and `Sergin.UserAccess` (submodules of this host repo) and the host repo itself. Each module gains a new `.Application.Contracts` project holding its request/response records verbatim (same namespace, same code); `.Application` keeps its handlers and adds a `ProjectReference` back to `.Contracts`; every presentation project (`.Presentation.WebApi`, `.Presentation.Blazor`, and DeviceManagement's `.Presentation.Grpc`) swaps its `.Application` reference for `.Application.Contracts`. `ISerginModule` gains a `ContractsAssembly` member, and the dispatch-mode resolver's assembly→schema map is extended so both `ApplicationAssembly` and `ContractsAssembly` route to the same module — this is the one behavior-affecting change and it must land in the same commit as the first type move for a module.

**Tech Stack:** .NET 10, C# (sealed records, MediatR `ICommand<T>`/`IQuery<T>`), `Sergin.MeterMinder.slnx` (XML slnx format), Central Package Management (`Directory.Packages.props`), git submodules.

**Spec:** docs/superpowers/specs/2026-08-22-application-contracts-split-design.md

## Global Constraints

- **`TreatWarningsAsErrors=true`, `AnalysisMode=All`, SonarAnalyzer.CSharp, `EnforceCodeStyleInBuild`** are set solution-wide by `Directory.Build.props`. Any analyzer warning, style violation, or nullable warning fails the build. Nullable and implicit usings are enabled everywhere.
- **Central Package Management is on.** `Directory.Packages.props` at the repo root holds every package version; `PackageReference` items in `.csproj` files carry no `Version` attribute. This change adds no new NuGet packages, so no `Directory.Packages.props` edits are needed — every new project only adds `ProjectReference`s.
- **Namespace-preservation rule**: every moved type keeps its existing namespace unchanged (e.g. `Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create` stays that namespace even though the type now lives in `Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj`'s output assembly). No `using` statement anywhere in the codebase changes as a result of this plan — only `.csproj` `<ProjectReference>` elements and, in the host repo, `Sergin.MeterMinder.slnx`.
- **The dispatch-resolver fix must land in the same commit as the first type move for that module.** There is no safe intermediate state where a module's request/response types have moved to `.Application.Contracts` but `ModuleDispatchRouteResolver`'s assembly→schema map hasn't been extended to recognize `ContractsAssembly` — that state breaks every dispatch of that module's requests, not degrades it. Phase 2's and Phase 3's first commit in each repo therefore bundles the type move with nothing else contentious, but the *resolver* fix itself is Phase 1 (SharedKernel), landed before any module's types actually move.
- **Repo-boundary assumption**: this plan treats Phases 1–3 as ordinary commits made directly inside this one working copy's submodule directories (`src/SharedKernel/`, `src/Modules/UserAccess/`) — not as separate PRs merged upstream in each submodule's own remote first. Phase 1 commits inside `src/SharedKernel/` (a git repo in its own right, since it's a submodule), Phase 2 commits inside `src/Modules/UserAccess/` (same), and Phase 3 commits at the host repo root, which also stages the two submodule pointer changes (`git add src/SharedKernel src/Modules/UserAccess`) as part of its own commit. No `git push` to either submodule's remote is required for this plan to be internally consistent; that is a separate step outside this plan's scope if the user wants the submodule commits published upstream.
- **`ISerginModule.ContractsAssembly` is added as a hard, non-default interface member — no `virtual`/default-interface-member escape hatch.** Justification (per spec §6 step 1's coordination-need note, which says the mechanism is a call to make at execution time and flags that "one may not be necessary if the submodule bumps land together in practice"): under the repo-boundary assumption above, `DeviceManagementModule` and `UserAccessModule` are updated to implement `ContractsAssembly` in Phases 2–3 of *this same plan*, before the host repo ever builds against the new `ISerginModule` shape — there is no published intermediate state where a consumer builds against the new interface without also having the new member implemented. A default-interface-member would only earn its keep if SharedKernel's PR could merge and be consumed standalone before UserAccess/DeviceManagement catch up; this plan's single-working-copy sequencing makes that scenario impossible, so the added complexity (and the design smell of a "temporary" DIM nobody would remember to remove) isn't justified.
- **Full end-to-end build+test verification (`dotnet build Sergin.MeterMinder.slnx` then `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/...`) can only run for real once the host repo's submodule pointers are updated** to include the Phase 1 (SharedKernel) and Phase 2 (UserAccess) commits — i.e. only at the end of Phase 3. Phase 1 verifies with a standalone `dotnet build Sergin.SharedKernel.slnx` from inside `src/SharedKernel/`; Phase 2 can only reason about correctness (there is no solution file inside `src/Modules/UserAccess/` to build standalone — see its own CLAUDE.md, "embed-only") and defers its actual compile check to Phase 3.
- **Verbatim move, no primitivization**: command/query constructors keep their existing domain-typed parameters (e.g. `CreateDeviceCommand(DeviceId DeviceId, ManufacturerId ManufacturerId)`, not primitives). `.Application.Contracts` therefore references the module's `.Domain` project, same as `.Application` does today.
- **What does not move**: handler classes (`*CommandHandler`/`*QueryCommandHandler`, all `internal sealed`), query-repository interfaces (`IGet*QueryRepository`), `IUnitOfWork` interfaces, anything under `.Infrastructure`/`.Infrastructure.Data`. These stay in `.Application` exactly where they are today.
- **Manufacturer and DeactivateUser command/response types are in scope**, resolving the spec's own open follow-up ("confirming the exact set of Manufacturer command/response types... before executing the move"): the source tree was read as part of writing this plan (Task 8, Task 13) and the full set of presentation-facing request/response records for both modules is enumerated explicitly in each task below — not just the subset named in the spec's illustrative §2 list.

---

## Phase 1 — SharedKernel repo (`src/SharedKernel/`)

This repo has its own solution file (`Sergin.SharedKernel.slnx`) and zero external dependencies (per its own CLAUDE.md). It does not itself implement `ISerginModule` anywhere — only defines the interface and consumes `IReadOnlyCollection<ISerginModule>` via closures — so adding a new required member does not break this repo's own standalone build; the standalone build/test gate for this phase is `dotnet build Sergin.SharedKernel.slnx`, run from inside `src/SharedKernel/`. No module implements `ContractsAssembly` yet at the end of this phase — that is deliberate and safe under this plan's single-working-copy assumption (see Global Constraints).

### Task 1: Add `ISerginModule.ContractsAssembly`

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Modules/ISerginModule.cs` (currently 17 lines, full current content below)

Current full content of `ISerginModule.cs`:
```csharp
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sergin.SharedKernel.Modules;

public interface ISerginModule
{
    string Schema { get; }

    Assembly ApplicationAssembly { get; }

    void AddServices(IServiceCollection services, IConfigurationSection configuration);

    Task MigrateAsync(IServiceProvider services);
}
```

**Interfaces:**
- Consumes: nothing from earlier tasks (first task in the plan).
- Produces: `ISerginModule.ContractsAssembly` (`Assembly`, get-only) — every later task that implements `ISerginModule` (Task 9's `DeviceManagementModule`, Task 14's `UserAccessModule`) must implement this member, and Task 2/Task 3 (`ModuleDispatchRouteResolver`, `SerginWebUiExtensions`) consume it to extend the assembly→schema map.

- [ ] **Step 1: Add the `ContractsAssembly` member to the interface**

  Open `C:\@factory\Sergin\Sergin.MeterMinder\src\SharedKernel\Sergin.SharedKernel.Modules\ISerginModule.cs` and change:
  ```csharp
  public interface ISerginModule
  {
      string Schema { get; }

      Assembly ApplicationAssembly { get; }

      void AddServices(IServiceCollection services, IConfigurationSection configuration);

      Task MigrateAsync(IServiceProvider services);
  }
  ```
  to:
  ```csharp
  public interface ISerginModule
  {
      string Schema { get; }

      Assembly ApplicationAssembly { get; }

      Assembly ContractsAssembly { get; }

      void AddServices(IServiceCollection services, IConfigurationSection configuration);

      Task MigrateAsync(IServiceProvider services);
  }
  ```

- [ ] **Step 2: Build the SharedKernel solution standalone to confirm the interface change compiles**

  Run from inside `src/SharedKernel/`:
  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder\src\SharedKernel
  dotnet build Sergin.SharedKernel.slnx
  ```
  Expected: build succeeds. No project in this repo implements `ISerginModule`, so nothing here fails to compile from the added member — this only confirms the interface file itself is syntactically valid and the solution still builds.

- [ ] **Step 3: Commit inside the SharedKernel submodule working tree**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder\src\SharedKernel
  git add Sergin.SharedKernel.Modules/ISerginModule.cs
  git commit -m "Add ISerginModule.ContractsAssembly for the Application Contracts split"
  ```

### Task 2: Extend `ModuleDispatchRouteResolver`'s assembly→schema lookup

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/Dispatching/ModuleDispatchRouteResolver.cs` (currently 70 lines)

Current relevant excerpt (constructor + `IsRemote`):
```csharp
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
    ...
}
```

This class itself needs **no code change** — it already takes `schemaByAssembly` as an injected dictionary and does a generic `TryGetValue` lookup keyed by whatever assembly the request type belongs to. The fix belongs entirely in how that dictionary is *built* (Task 3, `SerginWebUiExtensions`), which currently builds it from `ApplicationAssembly` only. This task exists to update the type's doc comment, since it currently says "does not belong to any registered module's ApplicationAssembly" in the thrown exception message, which will no longer be strictly accurate once `ContractsAssembly` also maps into the same dictionary.

**Interfaces:**
- Consumes: `ISerginModule.ContractsAssembly` (Task 1).
- Produces: no new public surface; the exception message text changes (informational only, not depended on by any test in the read source).

- [ ] **Step 1: Update the exception message to reflect both assemblies**

  In `C:\@factory\Sergin\Sergin.MeterMinder\src\SharedKernel\Sergin.SharedKernel.Hosts.WebUi\Dispatching\ModuleDispatchRouteResolver.cs`, change:
  ```csharp
        if (!schemaByAssembly.TryGetValue(schemaSourceType.Assembly, out string? schema))
        {
            throw new InvalidOperationException(
                $"'{requestType.FullName}' does not belong to any registered module's ApplicationAssembly.");
        }
  ```
  to:
  ```csharp
        if (!schemaByAssembly.TryGetValue(schemaSourceType.Assembly, out string? schema))
        {
            throw new InvalidOperationException(
                $"'{requestType.FullName}' does not belong to any registered module's ApplicationAssembly "
                + "or ContractsAssembly.");
        }
  ```

- [ ] **Step 2: Update the class's XML doc comment**

  Change the doc comment line that currently reads:
  ```csharp
  /// Maps a request type to its owning module's schema via the request's declaring assembly (the same
  /// reflection style UseSerginWebUiAsync's @page prefix guard already uses), then looks that schema up
  /// in DispatchModeOptions. Constructed with a closure over the registered modules by whichever host
  /// bootstrap calls AddSerginBlazorApp — not resolved from DI, matching SerginUiModuleCatalog's and
  /// DispatchModeOptionsValidator's precedent.
  /// </summary>
  ```
  to:
  ```csharp
  /// Maps a request type to its owning module's schema via the request's declaring assembly (the same
  /// reflection style UseSerginWebUiAsync's @page prefix guard already uses), then looks that schema up
  /// in DispatchModeOptions. The lookup dictionary maps both a module's ApplicationAssembly and its
  /// ContractsAssembly to the same schema, since a request's record type may be declared in either
  /// assembly depending on whether the module has adopted the Application Contracts split. Constructed
  /// with a closure over the registered modules by whichever host bootstrap calls AddSerginBlazorApp —
  /// not resolved from DI, matching SerginUiModuleCatalog's and DispatchModeOptionsValidator's precedent.
  /// </summary>
  ```

- [ ] **Step 3: Build the SharedKernel solution standalone**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder\src\SharedKernel
  dotnet build Sergin.SharedKernel.slnx
  ```
  Expected: build succeeds.

  (Commit deferred to Task 3's Step, since Task 3 changes the dictionary-construction call site that this doc comment describes — landing both in one commit keeps the description and the behavior it describes in the same diff.)

### Task 3: Extend `SerginWebUiExtensions`'s assembly→schema dictionary construction

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs:79-81`

Current exact lines:
```csharp
        builder.Services.AddSingleton<IDispatchRouteResolver>(p => new ModuleDispatchRouteResolver(
            modules.ToDictionary(module => module.ApplicationAssembly, module => module.Schema),
            p.GetRequiredService<IOptions<DispatchModeOptions>>()));
```

This is the one line that must change so a request type declared in a module's new `.Application.Contracts` assembly still resolves to the right schema. `Dictionary<TKey,TValue>` built via a single `.ToDictionary(...)` call cannot have two source sequences merged into it in one expression without either concatenating input sequences or building it imperatively; use `Enumerable.Concat` over two projections of `modules` to keep this a single expression, matching the existing one-liner style.

**Interfaces:**
- Consumes: `ISerginModule.ApplicationAssembly` (pre-existing), `ISerginModule.ContractsAssembly` (Task 1), `ModuleDispatchRouteResolver` constructor signature `(IReadOnlyDictionary<Assembly, string> schemaByAssembly, IOptions<DispatchModeOptions> options)` (pre-existing, Task 2 didn't change it).
- Produces: nothing new consumed by later tasks — this is the load-bearing fix that Global Constraints' "resolver fix must land in the same commit as the first type move" refers to, but the fix itself lands here, ahead of any type actually moving, which is safe because both assemblies of every existing module currently resolve to the same schema value (no module has adopted the split yet, so `ApplicationAssembly` and `ContractsAssembly` are trivially different local variables mapping to the same schema string — no collision, no behavior change until Phase 2/3 add real `.Contracts` assemblies).

- [ ] **Step 1: Replace the single-source dictionary construction with a two-source `Concat`**

  In `C:\@factory\Sergin\Sergin.MeterMinder\src\SharedKernel\Sergin.SharedKernel.Hosts.WebUi\SerginWebUiExtensions.cs`, change lines 79-81 from:
  ```csharp
        builder.Services.AddSingleton<IDispatchRouteResolver>(p => new ModuleDispatchRouteResolver(
            modules.ToDictionary(module => module.ApplicationAssembly, module => module.Schema),
            p.GetRequiredService<IOptions<DispatchModeOptions>>()));
  ```
  to:
  ```csharp
        IReadOnlyDictionary<Assembly, string> schemaByAssembly = modules
            .Select(module => (Assembly: module.ApplicationAssembly, module.Schema))
            .Concat(modules.Select(module => (Assembly: module.ContractsAssembly, module.Schema)))
            .ToDictionary(entry => entry.Assembly, entry => entry.Schema);

        builder.Services.AddSingleton<IDispatchRouteResolver>(p => new ModuleDispatchRouteResolver(
            schemaByAssembly,
            p.GetRequiredService<IOptions<DispatchModeOptions>>()));
  ```

  Note: `Assembly` is already `using System.Reflection;` (line 1 of the file) — no new `using` needed. `IReadOnlyDictionary<Assembly, string>` matches `ModuleDispatchRouteResolver`'s first constructor parameter type exactly (see Task 2's current excerpt).

- [ ] **Step 2: Build the SharedKernel solution standalone**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder\src\SharedKernel
  dotnet build Sergin.SharedKernel.slnx
  ```
  Expected: build succeeds — `System.Linq`'s `Concat`/`Select`/`ToDictionary` are already implicitly available (implicit usings + `System.Linq` is a standard implicit global using under the SDK's `ImplicitUsings=enable`).

- [ ] **Step 3: Commit Tasks 1–3 together as the SharedKernel-side resolver fix**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder\src\SharedKernel
  git add Sergin.SharedKernel.Modules/ISerginModule.cs Sergin.SharedKernel.Hosts.WebUi/Dispatching/ModuleDispatchRouteResolver.cs Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs
  git commit -m "Extend dispatch route resolver to map ContractsAssembly alongside ApplicationAssembly"
  ```

  (If Task 1's Step 3 already committed `ISerginModule.cs` separately, this `git add` of that same file is a no-op for it — `git add` on an unchanged tracked file stages nothing extra. Either committing structure is acceptable; this plan defaults to three separate small commits per file only if the executor prefers granularity, but the single combined commit above is the one this plan specifies as the phase's final commit.)

### Task 4: Update SharedKernel's own CLAUDE.md

**Files:**
- Modify: `src/SharedKernel/.claude/CLAUDE.md`

Current relevant bullet (inside the "Project layering" section, under `Sergin.SharedKernel.Modules`):
```
- **`Sergin.SharedKernel.Modules`** — the module contract every Sergin module implements: `ISerginModule` is the core contract (`Schema`, `ApplicationAssembly`, `AddServices`, `MigrateAsync`); `ISerginWebApiModule : ISerginModule` adds only `MapEndpoints`; `ISerginWebUiModule : ISerginModule` adds only `UiAssembly` (the assembly holding the module's routable components — deliberately never `ApplicationAssembly`, which stays UI-free) and `NavItems` (`IReadOnlyCollection<SerginNavItem>`, its nav-menu entries, each an `(Label, Href, Icon, Order)` record). A module class implements whichever of the two presentation interfaces match the surfaces it exposes — one class per module implements all its capabilities; which capabilities a given host wires up is that host's choice. This is the seam a host uses to compose modules — see `docs/superpowers/specs/2026-07-26-module-registration-design.md` in the [Sergin.MeterMinder](https://github.com/poursh/Sergin.MeterMinder) repo for the original design rationale (that repo is where the doc lives; it predates this repo's extraction).
```

**Interfaces:**
- Consumes: nothing (documentation only).
- Produces: nothing (documentation only).

- [ ] **Step 1: Update the `Sergin.SharedKernel.Modules` bullet to mention `ContractsAssembly`**

  In `C:\@factory\Sergin\Sergin.MeterMinder\src\SharedKernel\.claude\CLAUDE.md`, change:
  ```
  - **`Sergin.SharedKernel.Modules`** — the module contract every Sergin module implements: `ISerginModule` is the core contract (`Schema`, `ApplicationAssembly`, `AddServices`, `MigrateAsync`); `ISerginWebApiModule : ISerginModule` adds only `MapEndpoints`; `ISerginWebUiModule : ISerginModule` adds only `UiAssembly` (the assembly holding the module's routable components — deliberately never `ApplicationAssembly`, which stays UI-free) and `NavItems` (`IReadOnlyCollection<SerginNavItem>`, its nav-menu entries, each an `(Label, Href, Icon, Order)` record). A module class implements whichever of the two presentation interfaces match the surfaces it exposes — one class per module implements all its capabilities; which capabilities a given host wires up is that host's choice. This is the seam a host uses to compose modules — see `docs/superpowers/specs/2026-07-26-module-registration-design.md` in the [Sergin.MeterMinder](https://github.com/poursh/Sergin.MeterMinder) repo for the original design rationale (that repo is where the doc lives; it predates this repo's extraction).
  ```
  to:
  ```
  - **`Sergin.SharedKernel.Modules`** — the module contract every Sergin module implements: `ISerginModule` is the core contract (`Schema`, `ApplicationAssembly`, `ContractsAssembly`, `AddServices`, `MigrateAsync`); `ISerginWebApiModule : ISerginModule` adds only `MapEndpoints`; `ISerginWebUiModule : ISerginModule` adds only `UiAssembly` (the assembly holding the module's routable components — deliberately never `ApplicationAssembly`, which stays UI-free) and `NavItems` (`IReadOnlyCollection<SerginNavItem>`, its nav-menu entries, each an `(Label, Href, Icon, Order)` record). `ApplicationAssembly` is the assembly holding a module's MediatR handlers/repository interfaces/`IUnitOfWork`; `ContractsAssembly` is a separate, thinner assembly (a module's `.Application.Contracts` project) holding only its MediatR command/query request and response records — the shapes a presentation layer actually needs. `ModuleDispatchRouteResolver` (in `Sergin.SharedKernel.Hosts.WebUi`) maps both assemblies to the same module/schema, since a request record may be declared in either one depending on whether the module has adopted the split; see the [Sergin.MeterMinder](https://github.com/poursh/Sergin.MeterMinder) repo's `docs/superpowers/specs/2026-08-22-application-contracts-split-design.md` for the full rationale. A module class implements whichever of the two presentation interfaces match the surfaces it exposes — one class per module implements all its capabilities; which capabilities a given host wires up is that host's choice. This is the seam a host uses to compose modules — see `docs/superpowers/specs/2026-07-26-module-registration-design.md` in the [Sergin.MeterMinder](https://github.com/poursh/Sergin.MeterMinder) repo for the original design rationale (that repo is where the doc lives; it predates this repo's extraction).
  ```

- [ ] **Step 2: Commit the SharedKernel CLAUDE.md update**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder\src\SharedKernel
  git add .claude/CLAUDE.md
  git commit -m "Document ISerginModule.ContractsAssembly in SharedKernel CLAUDE.md"
  ```

Phase 1 is now complete: `dotnet build Sergin.SharedKernel.slnx` (run from `src/SharedKernel/`) is this phase's standalone verification gate, exercised at the end of Task 3. The full integration suite cannot run yet — nothing implements `ContractsAssembly`, and this repo has no test project of its own (per its CLAUDE.md, `Sergin.SharedKernel.IntegrationTests` is shared *infrastructure*, not a runnable suite).

---

## Phase 2 — UserAccess repo (`src/Modules/UserAccess/`)

This repo is **embed-only**: no solution file, no `Directory.Build.props`/`Directory.Packages.props` of its own (per its CLAUDE.md) — it only compiles once mounted inside a host that also provides a `Sergin.SharedKernel` submodule. Every task below creates/edits files and can be reasoned about for correctness, but **the actual `dotnet build` verification for this phase happens in Phase 3**, once the host repo's submodule pointers include this phase's commits. Do not attempt `dotnet build`/`dotnet ef` from inside `src/Modules/UserAccess/` directly — there is nothing to build against without the host's solution file.

### Task 5: Create the `Sergin.UserAccess.Application.Contracts` project

**Files:**
- Create: `src/Modules/UserAccess/Sergin.UserAccess.Application.Contracts/Sergin.UserAccess.Application.Contracts.csproj`
- Create: `src/Modules/UserAccess/Sergin.UserAccess.Application.Contracts/UserAccessApplicationContractsAssemblyReference.cs`

**Interfaces:**
- Consumes: `Sergin.SharedKernel.Application.csproj` (pre-existing, path `..\..\..\SharedKernel\Sergin.SharedKernel.Application\Sergin.SharedKernel.Application.csproj` relative to this new project, same relative path `Sergin.UserAccess.Application.csproj` already uses), `Sergin.UserAccess.Domain.csproj` (pre-existing, path `..\Sergin.UserAccess.Domain\Sergin.UserAccess.Domain.csproj`).
- Produces: `UserAccessApplicationContractsAssemblyReference.Assembly` (static `Assembly` field) — consumed by Task 14 (`UserAccessModule.ContractsAssembly`).

- [ ] **Step 1: Create the project directory and csproj**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess\Sergin.UserAccess.Application.Contracts\Sergin.UserAccess.Application.Contracts.csproj` with this exact content (mirrors `Sergin.UserAccess.Application.csproj`'s two references — `.Contracts` needs the same two: `SharedKernel.Application` for `ICommand<T>`/`IQuery<T>`/`RequiredPermissionsAttribute`, and `.Domain` because command constructors stay domain-typed under Approach B):
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <ItemGroup>
      <ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Application\Sergin.SharedKernel.Application.csproj" />
      <ProjectReference Include="..\Sergin.UserAccess.Domain\Sergin.UserAccess.Domain.csproj" />
    </ItemGroup>
  </Project>
  ```

- [ ] **Step 2: Create the assembly-reference marker class**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess\Sergin.UserAccess.Application.Contracts\UserAccessApplicationContractsAssemblyReference.cs` (mirrors `UserAccessApplicationAssemblyReference.cs`'s exact shape):
  ```csharp
  using System.Reflection;

  namespace Sergin.UserAccess.Application.Contracts;

  public static class UserAccessApplicationContractsAssemblyReference
  {
      public static readonly Assembly Assembly = typeof(UserAccessApplicationContractsAssemblyReference).Assembly;
  }
  ```

- [ ] **Step 3: Create a `GlobalUsings.cs` matching `.Application`'s shape, minus the `.Application`-only import**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess\Sergin.UserAccess.Application.Contracts\GlobalUsings.cs`:
  ```csharp
  global using ErrorOr;
  global using Sergin.SharedKernel.Domain;
  global using Sergin.SharedKernel.Application;
  global using Sergin.UserAccess.Domain;
  ```
  This is the same content as `Sergin.UserAccess.Application/GlobalUsings.cs` (Task 6 will leave that file unchanged) — both projects need `ErrorOr` (for `sealed record` return types used elsewhere) and the two SharedKernel/Domain globals for `ICommand<T>`/`IQuery<T>`/domain value objects like `UserName`.

- [ ] **Step 4: No build step here** — see Phase 2 preamble; standalone build is not possible from this repo. Correctness is checked by inspection at this step and confirmed for real in Phase 3, Task 20.

- [ ] **Step 5: Commit inside the UserAccess submodule working tree**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess
  git add Sergin.UserAccess.Application.Contracts
  git commit -m "Add Sergin.UserAccess.Application.Contracts project shell"
  ```

### Task 6: Move UserAccess's command/query request+response records into `.Application.Contracts`

**Files:**
- Create: `src/Modules/UserAccess/Sergin.UserAccess.Application.Contracts/Users/Commands/Create/CreateUserCommand.cs`
- Create: `src/Modules/UserAccess/Sergin.UserAccess.Application.Contracts/Users/Commands/Create/CreateUserCommandResponse.cs`
- Create: `src/Modules/UserAccess/Sergin.UserAccess.Application.Contracts/Users/Commands/DeactivateUser/DeactivateUserCommand.cs`
- Create: `src/Modules/UserAccess/Sergin.UserAccess.Application.Contracts/Users/Commands/DeactivateUser/DeactivateUserCommandResponse.cs`
- Create: `src/Modules/UserAccess/Sergin.UserAccess.Application.Contracts/Users/Commands/GetOne/GetUserByIdQueryCommand.cs`
- Create: `src/Modules/UserAccess/Sergin.UserAccess.Application.Contracts/Users/Commands/GetOne/UserQueryResponse.cs`
- Create: `src/Modules/UserAccess/Sergin.UserAccess.Application.Contracts/Users/Commands/GetList/GetUserListItem.cs`
- Delete: `src/Modules/UserAccess/Sergin.UserAccess.Application/Users/Commands/Create/CreateUserCommand.cs`
- Delete: `src/Modules/UserAccess/Sergin.UserAccess.Application/Users/Commands/Create/CreateUserCommandResponse.cs`
- Delete: `src/Modules/UserAccess/Sergin.UserAccess.Application/Users/Commands/DeactivateUser/DeactivateUserCommand.cs`
- Delete: `src/Modules/UserAccess/Sergin.UserAccess.Application/Users/Commands/DeactivateUser/DeactivateUserCommandResponse.cs`
- Delete: `src/Modules/UserAccess/Sergin.UserAccess.Application/Users/Commands/GetOne/GetUserByIdQueryCommand.cs`
- Delete: `src/Modules/UserAccess/Sergin.UserAccess.Application/Users/Commands/GetOne/UserQueryResponse.cs`
- Delete: `src/Modules/UserAccess/Sergin.UserAccess.Application/Users/Commands/GetList/GetUserListItem.cs`

This moves **all** presentation-facing request/response records for the `Users` aggregate — `Create`, `DeactivateUser`, `GetOne`, and `GetList`'s response-item type (list features have no dedicated request type per the "CQRS structural gotchas" convention) — not just the `Create`/GetOne/GetList subset the spec's illustrative §2 list names. `DeactivateUserCommand`/`DeactivateUserCommandResponse` must move too: `Sergin.UserAccess.Presentation.WebApi`'s `DeactivateUser` endpoint dispatches `DeactivateUserCommand` exactly like `CreateUserCommand`, so leaving it behind would force that presentation project to keep a `.Application` reference alongside its new `.Application.Contracts` one — defeating the split for that one type.

**Interfaces:**
- Consumes: `Sergin.UserAccess.Application.Contracts.csproj` (Task 5).
- Produces (namespaces/types now homed in `Sergin.UserAccess.Application.Contracts.dll`, unchanged from today):
  - `Sergin.UserAccess.Application.Users.Commands.Create.CreateUserCommand` : `ICommand<CreateUserCommandResponse>`, `CreateUserCommand(UserName UserName)`
  - `Sergin.UserAccess.Application.Users.Commands.Create.CreateUserCommandResponse(Guid Id)`
  - `Sergin.UserAccess.Application.Users.Commands.DeactivateUser.DeactivateUserCommand` : `ICommand<DeactivateUserCommandResponse>`, `DeactivateUserCommand(Guid Id)`
  - `Sergin.UserAccess.Application.Users.Commands.DeactivateUser.DeactivateUserCommandResponse(Guid Id)`
  - `Sergin.UserAccess.Application.Users.Commands.GetOne.GetUserByIdQueryCommand` : `IQuery<UserQueryResponse>`, `[RequiredPermissions("permission.ua.users.read")]`, `GetUserByIdQueryCommand(Guid Id)`
  - `Sergin.UserAccess.Application.Users.Commands.GetOne.UserQueryResponse(Guid Id, string UserName)`
  - `Sergin.UserAccess.Application.Users.Commands.GetList.GetUserListItem(Guid Id, string UserName)`
  - Consumed by Task 7 (`.Application` adds a `ProjectReference` back to `.Contracts` since handlers still implement `ICommandHandler<CreateUserCommand, CreateUserCommandResponse>` etc.), Task 8 (`.Presentation.WebApi`/`.Presentation.Blazor` swap references).

- [ ] **Step 1: Create `CreateUserCommand.cs` in `.Application.Contracts` with the exact current content**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess\Sergin.UserAccess.Application.Contracts\Users\Commands\Create\CreateUserCommand.cs`:
  ```csharp
  using Sergin.UserAccess.Domain.Users;
  using Sergin.SharedKernel.Application.Commands;

  namespace Sergin.UserAccess.Application.Users.Commands.Create;

  public sealed record CreateUserCommand(UserName UserName) : ICommand<CreateUserCommandResponse>;
  ```

- [ ] **Step 2: Create `CreateUserCommandResponse.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess\Sergin.UserAccess.Application.Contracts\Users\Commands\Create\CreateUserCommandResponse.cs`:
  ```csharp
  namespace Sergin.UserAccess.Application.Users.Commands.Create;

  public sealed record CreateUserCommandResponse(Guid Id);
  ```

- [ ] **Step 3: Create `DeactivateUserCommand.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess\Sergin.UserAccess.Application.Contracts\Users\Commands\DeactivateUser\DeactivateUserCommand.cs`:
  ```csharp
  using Sergin.SharedKernel.Application.Commands;

  namespace Sergin.UserAccess.Application.Users.Commands.DeactivateUser;

  public sealed record DeactivateUserCommand(Guid Id) : ICommand<DeactivateUserCommandResponse>;
  ```

- [ ] **Step 4: Create `DeactivateUserCommandResponse.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess\Sergin.UserAccess.Application.Contracts\Users\Commands\DeactivateUser\DeactivateUserCommandResponse.cs`:
  ```csharp
  namespace Sergin.UserAccess.Application.Users.Commands.DeactivateUser;

  public sealed record DeactivateUserCommandResponse(Guid Id);
  ```

- [ ] **Step 5: Create `GetUserByIdQueryCommand.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess\Sergin.UserAccess.Application.Contracts\Users\Commands\GetOne\GetUserByIdQueryCommand.cs`:
  ```csharp
  using Sergin.SharedKernel.Application.Commands.Queries;
  using Sergin.SharedKernel.Application.Securities.Authorization;

  namespace Sergin.UserAccess.Application.Users.Commands.GetOne;

  [RequiredPermissions("permission.ua.users.read")]
  public sealed record GetUserByIdQueryCommand(Guid Id) : IQuery<UserQueryResponse>;
  ```

- [ ] **Step 6: Create `UserQueryResponse.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess\Sergin.UserAccess.Application.Contracts\Users\Commands\GetOne\UserQueryResponse.cs`:
  ```csharp
  namespace Sergin.UserAccess.Application.Users.Commands.GetOne;

  public sealed record UserQueryResponse(Guid Id, string UserName);
  ```

- [ ] **Step 7: Create `GetUserListItem.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess\Sergin.UserAccess.Application.Contracts\Users\Commands\GetList\GetUserListItem.cs`:
  ```csharp
  namespace Sergin.UserAccess.Application.Users.Commands.GetList;

  public sealed record GetUserListItem(Guid Id, string UserName);
  ```

- [ ] **Step 8: Delete the seven original files from `.Application`**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess
  rm Sergin.UserAccess.Application/Users/Commands/Create/CreateUserCommand.cs
  rm Sergin.UserAccess.Application/Users/Commands/Create/CreateUserCommandResponse.cs
  rm Sergin.UserAccess.Application/Users/Commands/DeactivateUser/DeactivateUserCommand.cs
  rm Sergin.UserAccess.Application/Users/Commands/DeactivateUser/DeactivateUserCommandResponse.cs
  rm Sergin.UserAccess.Application/Users/Commands/GetOne/GetUserByIdQueryCommand.cs
  rm Sergin.UserAccess.Application/Users/Commands/GetOne/UserQueryResponse.cs
  rm Sergin.UserAccess.Application/Users/Commands/GetList/GetUserListItem.cs
  ```

- [ ] **Step 9: Commit the move**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess
  git add Sergin.UserAccess.Application.Contracts Sergin.UserAccess.Application
  git commit -m "Move Users command/query request and response records into Application.Contracts"
  ```

### Task 7: Add `.Application`'s `ProjectReference` back to `.Application.Contracts`

**Files:**
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Application/Sergin.UserAccess.Application.csproj`

Current full content:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Application\Sergin.SharedKernel.Application.csproj" />
    <ProjectReference Include="..\Sergin.UserAccess.Domain\Sergin.UserAccess.Domain.csproj" />
  </ItemGroup>
</Project>
```

This project keeps both its existing references (handlers still need `SharedKernel.Application` for `ICommandHandler<,>`/`IQueryHandler<,>` and `.Domain` for aggregate/repository types) and gains a third: `.Application.Contracts`, because e.g. `CreateUserCommandHandler` implements `ICommandHandler<CreateUserCommand, CreateUserCommandResponse>` — both generic arguments are types that just moved out of this project.

**Interfaces:**
- Consumes: `Sergin.UserAccess.Application.Contracts.csproj` (Task 5).
- Produces: `.Application`'s output assembly now transitively carries `.Application.Contracts` to every consumer of `.Application` (Task 9's composition root, which references `.Infrastructure` → `.Application`).

- [ ] **Step 1: Add the third `ProjectReference`**

  In `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess\Sergin.UserAccess.Application\Sergin.UserAccess.Application.csproj`, change:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <ItemGroup>
      <ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Application\Sergin.SharedKernel.Application.csproj" />
      <ProjectReference Include="..\Sergin.UserAccess.Domain\Sergin.UserAccess.Domain.csproj" />
    </ItemGroup>
  </Project>
  ```
  to:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <ItemGroup>
      <ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Application\Sergin.SharedKernel.Application.csproj" />
      <ProjectReference Include="..\Sergin.UserAccess.Domain\Sergin.UserAccess.Domain.csproj" />
      <ProjectReference Include="..\Sergin.UserAccess.Application.Contracts\Sergin.UserAccess.Application.Contracts.csproj" />
    </ItemGroup>
  </Project>
  ```

- [ ] **Step 2: Commit**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess
  git add Sergin.UserAccess.Application/Sergin.UserAccess.Application.csproj
  git commit -m "Reference Application.Contracts from Application for handler signatures"
  ```

### Task 8: Swap `.Presentation.WebApi` and `.Presentation.Blazor` references from `.Application` to `.Application.Contracts`

**Files:**
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.WebApi/Sergin.UserAccess.Presentation.WebApi.csproj`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.Blazor/Sergin.UserAccess.Presentation.Blazor.csproj`

Current full content of `Sergin.UserAccess.Presentation.WebApi.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.WebApi\Sergin.SharedKernel.Presentation.WebApi.csproj" />
    <ProjectReference Include="..\Sergin.UserAccess.Application\Sergin.UserAccess.Application.csproj" />
  </ItemGroup>
</Project>
```

Current full content of `Sergin.UserAccess.Presentation.Blazor.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<ItemGroup>
		<FrameworkReference Include="Microsoft.AspNetCore.App" />

		<PackageReference Include="MudBlazor" />
	</ItemGroup>

	<ItemGroup>
		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Modules\Sergin.SharedKernel.Modules.csproj" />
		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Blazor\Sergin.SharedKernel.Presentation.Blazor.csproj" />
		<ProjectReference Include="..\Sergin.UserAccess.Application\Sergin.UserAccess.Application.csproj" />
	</ItemGroup>
</Project>
```

Neither project's source files reference anything from `.Application` beyond the moved request/response record namespaces (confirmed by reading `Sergin.UserAccess.Presentation.WebApi/GlobalUsings.cs` and `Sergin.UserAccess.Presentation.Blazor/GlobalUsings.cs` + `_Imports.razor` — none imports a handler-only or repository-only namespace; `_Imports.razor` imports `Sergin.UserAccess.Application.Users.Commands.{Create,DeactivateUser,GetList,GetOne}`, all of which are namespaces that now resolve inside `.Application.Contracts.dll` instead, unchanged). This is a pure reference swap, not an addition.

**Interfaces:**
- Consumes: `Sergin.UserAccess.Application.Contracts.csproj` (Task 5), the moved types (Task 6).
- Produces: nothing new consumed by later tasks in this phase; Task 9 (composition root) is unaffected because it references `.Infrastructure`/`.Presentation.WebApi`/`.Presentation.Blazor`/`SharedKernel.Modules`, none of which change in this task in a way that alters the composition root's own csproj.

- [ ] **Step 1: Swap the reference in `.Presentation.WebApi`**

  In `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess\Sergin.UserAccess.Presentation.WebApi\Sergin.UserAccess.Presentation.WebApi.csproj`, change:
  ```xml
      <ProjectReference Include="..\Sergin.UserAccess.Application\Sergin.UserAccess.Application.csproj" />
  ```
  to:
  ```xml
      <ProjectReference Include="..\Sergin.UserAccess.Application.Contracts\Sergin.UserAccess.Application.Contracts.csproj" />
  ```
  Full file after the change:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <ItemGroup>
      <ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.WebApi\Sergin.SharedKernel.Presentation.WebApi.csproj" />
      <ProjectReference Include="..\Sergin.UserAccess.Application.Contracts\Sergin.UserAccess.Application.Contracts.csproj" />
    </ItemGroup>
  </Project>
  ```

- [ ] **Step 2: Swap the reference in `.Presentation.Blazor`**

  In `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess\Sergin.UserAccess.Presentation.Blazor\Sergin.UserAccess.Presentation.Blazor.csproj`, change:
  ```xml
  		<ProjectReference Include="..\Sergin.UserAccess.Application\Sergin.UserAccess.Application.csproj" />
  ```
  to:
  ```xml
  		<ProjectReference Include="..\Sergin.UserAccess.Application.Contracts\Sergin.UserAccess.Application.Contracts.csproj" />
  ```
  Full file after the change:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk.Razor">
  	<ItemGroup>
  		<FrameworkReference Include="Microsoft.AspNetCore.App" />

  		<PackageReference Include="MudBlazor" />
  	</ItemGroup>

  	<ItemGroup>
  		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Modules\Sergin.SharedKernel.Modules.csproj" />
  		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Blazor\Sergin.SharedKernel.Presentation.Blazor.csproj" />
  		<ProjectReference Include="..\Sergin.UserAccess.Application.Contracts\Sergin.UserAccess.Application.Contracts.csproj" />
  	</ItemGroup>
  </Project>
  ```

- [ ] **Step 3: Commit**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess
  git add Sergin.UserAccess.Presentation.WebApi/Sergin.UserAccess.Presentation.WebApi.csproj Sergin.UserAccess.Presentation.Blazor/Sergin.UserAccess.Presentation.Blazor.csproj
  git commit -m "Point UserAccess presentation projects at Application.Contracts instead of Application"
  ```

### Task 9: Implement `ContractsAssembly` on `UserAccessModule`

**Files:**
- Modify: `src/Modules/UserAccess/Sergin.UserAccess/UserAccessModule.cs`

Current full content:
```csharp
using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Modules;
using Sergin.UserAccess.Application;
using Sergin.UserAccess.Infrastructure.Data;
using Sergin.UserAccess.Presentation.Blazor;
using Sergin.UserAccess.Users;

namespace Sergin.UserAccess;

public sealed class UserAccessModule : ISerginWebApiModule, ISerginWebUiModule
{
    public string Schema => UserAccessDbContext.Schema;

    public Assembly ApplicationAssembly => UserAccessApplicationAssemblyReference.Assembly;

    public Assembly UiAssembly => UserAccessBlazorAssemblyReference.Assembly;

    public IReadOnlyCollection<SerginNavItem> NavItems => UserAccessNavigation.Items;

    public void AddServices(IServiceCollection services, IConfigurationSection configuration)
    {
        services.AddModuleDbContext<UserAccessDbContext, IUserAccessDbContext, IUserAccessUnitOfWork>(configuration, UserAccessDbContext.Schema);

        services.AddUserDependencies();
    }

    public Task MigrateAsync(IServiceProvider services) => services.MigrateDbContextAsync<UserAccessDbContext>();

    public void MapEndpoints(RouteGroupBuilder group) => group.MapUserEndpoints();
}
```

The composition root project (`Sergin.UserAccess.csproj`, read in full: references `.Infrastructure`, `.Presentation.WebApi`, `.Presentation.Blazor`, `SharedKernel.Modules` — **not** `.Application` or `.Application.Contracts` directly) needs `Sergin.UserAccess.Application.Contracts`'s marker type to be visible for this new line to compile. It gets it transitively: `.Infrastructure` → `.Application` (Task 7 added `.Application` → `.Application.Contracts`), and `.Presentation.WebApi`/`.Presentation.Blazor` → `.Application.Contracts` directly (Task 8). No csproj change is needed on `Sergin.UserAccess.csproj` itself — this mirrors exactly how `UserAccessApplicationAssemblyReference` is already visible here today without a direct `.Application` reference.

**Interfaces:**
- Consumes: `UserAccessApplicationContractsAssemblyReference.Assembly` (Task 5), `ISerginModule.ContractsAssembly` (Phase 1, Task 1).
- Produces: `UserAccessModule` now fully implements the extended `ISerginModule` — consumed at runtime by `Sergin.MeterMinder.Hosts.All`'s `Program.cs` (unchanged in this phase) once the host repo bumps its submodule pointer in Phase 3.

- [ ] **Step 1: Add the `ContractsAssembly` property and its `using`**

  In `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess\Sergin.UserAccess\UserAccessModule.cs`, add a `using` for the Contracts namespace and the new property. Change:
  ```csharp
  using System.Reflection;
  using Microsoft.AspNetCore.Routing;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;
  using Sergin.SharedKernel.Infrastructure.Data.EFCore;
  using Sergin.SharedKernel.Modules;
  using Sergin.UserAccess.Application;
  using Sergin.UserAccess.Infrastructure.Data;
  using Sergin.UserAccess.Presentation.Blazor;
  using Sergin.UserAccess.Users;

  namespace Sergin.UserAccess;

  public sealed class UserAccessModule : ISerginWebApiModule, ISerginWebUiModule
  {
      public string Schema => UserAccessDbContext.Schema;

      public Assembly ApplicationAssembly => UserAccessApplicationAssemblyReference.Assembly;

      public Assembly UiAssembly => UserAccessBlazorAssemblyReference.Assembly;
  ```
  to:
  ```csharp
  using System.Reflection;
  using Microsoft.AspNetCore.Routing;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;
  using Sergin.SharedKernel.Infrastructure.Data.EFCore;
  using Sergin.SharedKernel.Modules;
  using Sergin.UserAccess.Application;
  using Sergin.UserAccess.Application.Contracts;
  using Sergin.UserAccess.Infrastructure.Data;
  using Sergin.UserAccess.Presentation.Blazor;
  using Sergin.UserAccess.Users;

  namespace Sergin.UserAccess;

  public sealed class UserAccessModule : ISerginWebApiModule, ISerginWebUiModule
  {
      public string Schema => UserAccessDbContext.Schema;

      public Assembly ApplicationAssembly => UserAccessApplicationAssemblyReference.Assembly;

      public Assembly ContractsAssembly => UserAccessApplicationContractsAssemblyReference.Assembly;

      public Assembly UiAssembly => UserAccessBlazorAssemblyReference.Assembly;
  ```

  (The rest of the file — `NavItems`, `AddServices`, `MigrateAsync`, `MapEndpoints` — is unchanged.)

- [ ] **Step 2: No standalone build here** — see Phase 2 preamble. Correctness reasoning: `UserAccessApplicationContractsAssemblyReference` lives in namespace `Sergin.UserAccess.Application.Contracts` (Task 5, Step 2) and this file now `using`s that namespace; the type is reachable because `Sergin.UserAccess.csproj` transitively references `.Application.Contracts` through `.Infrastructure` → `.Application` → `.Application.Contracts` and through `.Presentation.WebApi`/`.Presentation.Blazor` → `.Application.Contracts` directly, exactly as reasoned in this task's file header above.

- [ ] **Step 3: Commit**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess
  git add Sergin.UserAccess/UserAccessModule.cs
  git commit -m "Implement ISerginModule.ContractsAssembly on UserAccessModule"
  ```

### Task 10: Update UserAccess's own CLAUDE.md

**Files:**
- Modify: `src/Modules/UserAccess/.claude/CLAUDE.md`

Current relevant bullet (in "Per-project layering"):
```
- **`Sergin.UserAccess.Application`** — MediatR commands/queries + handlers, query repository interfaces. Feature folders hold the full slice under `<Aggregate>/Commands/<Feature>/...` — **queries live under `Commands/` too**, not a separate `Queries/` folder.
```

And the composition-root bullet:
```
- **`Sergin.UserAccess`** (no-suffix composition root) — implements `ISerginWebApiModule` and `ISerginWebUiModule` from `Sergin.SharedKernel.Modules` (`UserAccessModule` class): `Schema`, `ApplicationAssembly`, `UiAssembly` (points to `Sergin.UserAccess.Presentation.Blazor`), `AddServices` (calls `AddModuleDbContext<TContext, TIContext, TIUnitOfWork>` plus per-aggregate `Add<X>Dependencies()`), `MigrateAsync`, `MapEndpoints` (per-aggregate `Map<X>Endpoints()`), and `NavItems` (list of UI navigation items collected from the Blazor project).
```

**Interfaces:**
- Consumes: nothing (documentation only).
- Produces: nothing (documentation only).

- [ ] **Step 1: Add a `.Application.Contracts` bullet right before the `.Application` bullet**

  In `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess\.claude\CLAUDE.md`, change:
  ```
  - **`Sergin.UserAccess.Application`** — MediatR commands/queries + handlers, query repository interfaces. Feature folders hold the full slice under `<Aggregate>/Commands/<Feature>/...` — **queries live under `Commands/` too**, not a separate `Queries/` folder.
  ```
  to:
  ```
  - **`Sergin.UserAccess.Application.Contracts`** — the module's MediatR command/query request and response records only (e.g. `CreateUserCommand`, `CreateUserCommandResponse`, `GetUserByIdQueryCommand`, `UserQueryResponse`, `GetUserListItem`, `DeactivateUserCommand`, `DeactivateUserCommandResponse`), moved verbatim out of `.Application` — same namespace, same domain-typed constructor arguments, same `[RequiredPermissions]` attributes. References only `SharedKernel.Application` and this module's own `.Domain`. Exists so presentation layers (`.Presentation.WebApi`, `.Presentation.Blazor`) can depend on request/response shapes without pulling in handlers, repository interfaces, or `IUserAccessUnitOfWork` — those all stay in `.Application`. Carries `UserAccessApplicationContractsAssemblyReference`, exposing `typeof(...).Assembly` for `ISerginModule.ContractsAssembly`. See the [Sergin.MeterMinder](https://github.com/poursh/Sergin.MeterMinder) repo's `docs/superpowers/specs/2026-08-22-application-contracts-split-design.md` for the full design.
  - **`Sergin.UserAccess.Application`** — MediatR handlers, query repository interfaces, `IUserAccessUnitOfWork`. References `.Application.Contracts` (added by the same split) for the request/response types its handlers implement `ICommandHandler<TCommand, TResponse>`/`IQueryHandler<TQuery, TResponse>` against. Feature folders hold the full slice under `<Aggregate>/Commands/<Feature>/...` — **queries live under `Commands/` too**, not a separate `Queries/` folder.
  ```

- [ ] **Step 2: Update the composition-root bullet to mention `ContractsAssembly`**

  Change:
  ```
  - **`Sergin.UserAccess`** (no-suffix composition root) — implements `ISerginWebApiModule` and `ISerginWebUiModule` from `Sergin.SharedKernel.Modules` (`UserAccessModule` class): `Schema`, `ApplicationAssembly`, `UiAssembly` (points to `Sergin.UserAccess.Presentation.Blazor`), `AddServices` (calls `AddModuleDbContext<TContext, TIContext, TIUnitOfWork>` plus per-aggregate `Add<X>Dependencies()`), `MigrateAsync`, `MapEndpoints` (per-aggregate `Map<X>Endpoints()`), and `NavItems` (list of UI navigation items collected from the Blazor project).
  ```
  to:
  ```
  - **`Sergin.UserAccess`** (no-suffix composition root) — implements `ISerginWebApiModule` and `ISerginWebUiModule` from `Sergin.SharedKernel.Modules` (`UserAccessModule` class): `Schema`, `ApplicationAssembly`, `ContractsAssembly` (points to `Sergin.UserAccess.Application.Contracts`), `UiAssembly` (points to `Sergin.UserAccess.Presentation.Blazor`), `AddServices` (calls `AddModuleDbContext<TContext, TIContext, TIUnitOfWork>` plus per-aggregate `Add<X>Dependencies()`), `MigrateAsync`, `MapEndpoints` (per-aggregate `Map<X>Endpoints()`), and `NavItems` (list of UI navigation items collected from the Blazor project).
  ```

- [ ] **Step 3: Commit**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess
  git add .claude/CLAUDE.md
  git commit -m "Document Sergin.UserAccess.Application.Contracts in module CLAUDE.md"
  ```

Phase 2 is now complete at the source level. No standalone build/test can run inside `src/Modules/UserAccess/` (embed-only repo, no solution file) — Task 20 in Phase 3 is the first point this phase's correctness is actually confirmed by the compiler.

---

## Phase 3 — Host repo (this repo)

This phase updates the DeviceManagement module the same way Phase 2 updated UserAccess, then bumps both submodule pointers, updates `Sergin.MeterMinder.slnx`, updates the two scaffolding skills, updates this repo's own CLAUDE.md, and finally runs the full build+test verification — the first point in this plan any of it can be confirmed end-to-end.

### Task 11: Create the `Sergin.MeterMinder.DeviceManagement.Application.Contracts` project

**Files:**
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/DeviceManagementApplicationContractsAssemblyReference.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/GlobalUsings.cs`

**Interfaces:**
- Consumes: `Sergin.SharedKernel.Application.csproj` (path `..\..\..\SharedKernel\Sergin.SharedKernel.Application\Sergin.SharedKernel.Application.csproj`, same relative path `Sergin.MeterMinder.DeviceManagement.Application.csproj` already uses), `Sergin.MeterMinder.DeviceManagement.Domain.csproj` (path `..\Sergin.MeterMinder.DeviceManagement.Domain\Sergin.MeterMinder.DeviceManagement.Domain.csproj`).
- Produces: `DeviceManagementApplicationContractsAssemblyReference.Assembly` — consumed by Task 15 (`DeviceManagementModule.ContractsAssembly`).

- [ ] **Step 1: Create the csproj**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <ItemGroup>
      <ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Application\Sergin.SharedKernel.Application.csproj" />
      <ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Domain\Sergin.MeterMinder.DeviceManagement.Domain.csproj" />
    </ItemGroup>
  </Project>
  ```

- [ ] **Step 2: Create the assembly-reference marker class**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Application.Contracts\DeviceManagementApplicationContractsAssemblyReference.cs`:
  ```csharp
  using System.Reflection;

  namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts;

  public static class DeviceManagementApplicationContractsAssemblyReference
  {
      public static readonly Assembly Assembly = typeof(DeviceManagementApplicationContractsAssemblyReference).Assembly;
  }
  ```

- [ ] **Step 3: Create `GlobalUsings.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Application.Contracts\GlobalUsings.cs` (same content as `Sergin.MeterMinder.DeviceManagement.Application/GlobalUsings.cs`, which Task 12 leaves unchanged):
  ```csharp
  global using ErrorOr;
  global using Sergin.SharedKernel.Domain;
  global using Sergin.SharedKernel.Application;
  global using Sergin.MeterMinder.DeviceManagement.Domain;
  ```

- [ ] **Step 4: Add the project to `Sergin.MeterMinder.slnx`**

  (Doing this now, rather than deferring to a later "solution file" task, means Step 5 below can build immediately — this project has no `dotnet build`-able parent until it's in the solution or referenced by something that is.)

  In `C:\@factory\Sergin\Sergin.MeterMinder\Sergin.MeterMinder.slnx`, find the `/src/Modules/DeviceManagement/` folder block:
  ```xml
    <Folder Name="/src/Modules/DeviceManagement/">
      <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Sergin.MeterMinder.DeviceManagement.Application.csproj" />
      <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Domain/Sergin.MeterMinder.DeviceManagement.Domain.csproj" />
      <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.csproj" />
      <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure/Sergin.MeterMinder.DeviceManagement.Infrastructure.csproj" />
      <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement/Sergin.MeterMinder.DeviceManagement.csproj" />
    </Folder>
  ```
  and add the new project as the first entry (alphabetically it sorts before `.Application`, matching this file's existing alphabetical-within-folder ordering):
  ```xml
    <Folder Name="/src/Modules/DeviceManagement/">
      <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj" />
      <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Sergin.MeterMinder.DeviceManagement.Application.csproj" />
      <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Domain/Sergin.MeterMinder.DeviceManagement.Domain.csproj" />
      <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.csproj" />
      <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure/Sergin.MeterMinder.DeviceManagement.Infrastructure.csproj" />
      <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement/Sergin.MeterMinder.DeviceManagement.csproj" />
    </Folder>
  ```

- [ ] **Step 5: Build to confirm the new project compiles standalone**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder
  dotnet build Sergin.MeterMinder.slnx
  ```
  Expected: succeeds — this new project has no dependents yet, so this only confirms itself and its two `ProjectReference`s compile.

- [ ] **Step 6: Commit**

  ```bash
  git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts Sergin.MeterMinder.slnx
  git commit -m "Add Sergin.MeterMinder.DeviceManagement.Application.Contracts project shell"
  ```

### Task 12: Move DeviceManagement's Devices command/query request+response records into `.Application.Contracts`

**Files:**
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Devices/Commands/Create/CreateDeviceCommand.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Devices/Commands/Create/CreateDeviceCommandResponse.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Devices/Commands/GetOne/GetDeviceByIdQueryCommand.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Devices/Commands/GetOne/DeviceQueryResponse.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Devices/Commands/GetList/GetDeviceListItem.cs`
- Delete: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/Create/CreateDeviceCommand.cs`
- Delete: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/Create/CreateDeviceCommandResponse.cs`
- Delete: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/GetOne/GetDeviceByIdQueryCommand.cs`
- Delete: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/GetOne/DeviceQueryResponse.cs`
- Delete: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/GetList/GetDeviceListItem.cs`

**Interfaces:**
- Consumes: `Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj` (Task 11).
- Produces (unchanged namespaces, new assembly):
  - `Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create.CreateDeviceCommand` : `ICommand<CreateDeviceCommandResponse>`, `CreateDeviceCommand(DeviceId DeviceId, ManufacturerId ManufacturerId)`
  - `Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create.CreateDeviceCommandResponse(Guid Id)`
  - `Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne.GetDeviceByIdQueryCommand` : `IQuery<DeviceQueryResponse>`, `[RequiredPermissions("permission.dm.devices.read")]`, `GetDeviceByIdQueryCommand(Guid Id)`
  - `Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne.DeviceQueryResponse(Guid Id, string DeviceId, Guid ManufacturerId)`
  - `Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList.GetDeviceListItem(Guid Id, string DeviceId, Guid ManufacturerId)`
  - Consumed by Task 13 (`.Application` back-reference), Task 14 (presentation reference swaps, including `.Presentation.Grpc`).

- [ ] **Step 1: Create `CreateDeviceCommand.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Devices\Commands\Create\CreateDeviceCommand.cs`:
  ```csharp
  using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
  using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
  using Sergin.SharedKernel.Application.Commands;

  namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;

  public sealed record CreateDeviceCommand(DeviceId DeviceId, ManufacturerId ManufacturerId) : ICommand<CreateDeviceCommandResponse>;
  ```

- [ ] **Step 2: Create `CreateDeviceCommandResponse.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Devices\Commands\Create\CreateDeviceCommandResponse.cs`:
  ```csharp
  namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;

  public sealed record CreateDeviceCommandResponse(Guid Id);
  ```

- [ ] **Step 3: Create `GetDeviceByIdQueryCommand.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Devices\Commands\GetOne\GetDeviceByIdQueryCommand.cs`:
  ```csharp
  using Sergin.SharedKernel.Application.Commands.Queries;
  using Sergin.SharedKernel.Application.Securities.Authorization;

  namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;

  [RequiredPermissions("permission.dm.devices.read")]
  public sealed record GetDeviceByIdQueryCommand(Guid Id) : IQuery<DeviceQueryResponse>;
  ```

- [ ] **Step 4: Create `DeviceQueryResponse.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Devices\Commands\GetOne\DeviceQueryResponse.cs`:
  ```csharp
  namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;

  public sealed record DeviceQueryResponse(Guid Id, string DeviceId, Guid ManufacturerId);
  ```

- [ ] **Step 5: Create `GetDeviceListItem.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Devices\Commands\GetList\GetDeviceListItem.cs`:
  ```csharp
  namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;

  public sealed record GetDeviceListItem(Guid Id, string DeviceId, Guid ManufacturerId);
  ```

- [ ] **Step 6: Delete the five original files from `.Application`**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder
  rm src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/Create/CreateDeviceCommand.cs
  rm src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/Create/CreateDeviceCommandResponse.cs
  rm src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/GetOne/GetDeviceByIdQueryCommand.cs
  rm src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/GetOne/DeviceQueryResponse.cs
  rm src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/GetList/GetDeviceListItem.cs
  ```

- [ ] **Step 7: Commit**

  ```bash
  git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application
  git commit -m "Move Devices command/query request and response records into Application.Contracts"
  ```

### Task 13: Move DeviceManagement's Manufacturers command/query request+response records into `.Application.Contracts`

**Files:**
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Manufacturers/Commands/Create/CreateManufacturerCommand.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Manufacturers/Commands/Create/CreateManufacturerCommandResponse.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Manufacturers/Commands/GetOne/GetManufacturerByIdQueryCommand.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Manufacturers/Commands/GetOne/ManufacturerQueryResponse.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Manufacturers/Commands/GetList/GetManufacturerListItem.cs`
- Delete: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/Commands/Create/CreateManufacturerCommand.cs`
- Delete: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/Commands/Create/CreateManufacturerCommandResponse.cs`
- Delete: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/Commands/GetOne/GetManufacturerByIdQueryCommand.cs`
- Delete: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/Commands/GetOne/ManufacturerQueryResponse.cs`
- Delete: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/Commands/GetList/GetManufacturerListItem.cs`

This resolves the spec's own open follow-up: the source tree (read while writing this plan) shows the full set of Manufacturer feature slices is `Create`, `GetOne`, and `GetList` — the same three-slice shape as `Devices` — not just `GetManufacturerByIdQueryCommand` as the spec's illustrative §2 list names. All five request/response records move, matching the "verbatim move of every presentation-facing record" principle applied to `Devices` and to UserAccess's `DeactivateUser` (Task 6).

**Interfaces:**
- Consumes: `Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj` (Task 11).
- Produces (unchanged namespaces, new assembly):
  - `Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create.CreateManufacturerCommand` : `ICommand<CreateManufacturerCommandResponse>`, `CreateManufacturerCommand(ManufacturerName Name, ManufacturerAddress? Address)`
  - `Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create.CreateManufacturerCommandResponse(Guid Id)`
  - `Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne.GetManufacturerByIdQueryCommand` : `IQuery<ManufacturerQueryResponse>`, `[RequiredPermissions("permission.dm.manufacturers.read")]`, `GetManufacturerByIdQueryCommand(Guid Id)`
  - `Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne.ManufacturerQueryResponse(Guid Id, string Name, string? Address)`
  - `Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList.GetManufacturerListItem(Guid Id, string Name, string? Address)`

- [ ] **Step 1: Create `CreateManufacturerCommand.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Manufacturers\Commands\Create\CreateManufacturerCommand.cs`:
  ```csharp
  using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
  using Sergin.SharedKernel.Application.Commands;

  namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;

  public sealed record CreateManufacturerCommand(ManufacturerName Name, ManufacturerAddress? Address) : ICommand<CreateManufacturerCommandResponse>;
  ```

- [ ] **Step 2: Create `CreateManufacturerCommandResponse.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Manufacturers\Commands\Create\CreateManufacturerCommandResponse.cs`:
  ```csharp
  namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;

  public sealed record CreateManufacturerCommandResponse(Guid Id);
  ```

- [ ] **Step 3: Create `GetManufacturerByIdQueryCommand.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Manufacturers\Commands\GetOne\GetManufacturerByIdQueryCommand.cs`:
  ```csharp
  using Sergin.SharedKernel.Application.Commands.Queries;
  using Sergin.SharedKernel.Application.Securities.Authorization;

  namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;

  [RequiredPermissions("permission.dm.manufacturers.read")]
  public sealed record GetManufacturerByIdQueryCommand(Guid Id) : IQuery<ManufacturerQueryResponse>;
  ```

- [ ] **Step 4: Create `ManufacturerQueryResponse.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Manufacturers\Commands\GetOne\ManufacturerQueryResponse.cs`:
  ```csharp
  namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;

  public sealed record ManufacturerQueryResponse(Guid Id, string Name, string? Address);
  ```

- [ ] **Step 5: Create `GetManufacturerListItem.cs`**

  Create `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Manufacturers\Commands\GetList\GetManufacturerListItem.cs`:
  ```csharp
  namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;

  public sealed record GetManufacturerListItem(Guid Id, string Name, string? Address);
  ```

- [ ] **Step 6: Delete the five original files from `.Application`**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder
  rm src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/Commands/Create/CreateManufacturerCommand.cs
  rm src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/Commands/Create/CreateManufacturerCommandResponse.cs
  rm src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/Commands/GetOne/GetManufacturerByIdQueryCommand.cs
  rm src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/Commands/GetOne/ManufacturerQueryResponse.cs
  rm src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/Commands/GetList/GetManufacturerListItem.cs
  ```

- [ ] **Step 7: Commit**

  ```bash
  git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application
  git commit -m "Move Manufacturers command/query request and response records into Application.Contracts"
  ```

### Task 14: Add `.Application`'s `ProjectReference` back to `.Application.Contracts`, and build

**Files:**
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Sergin.MeterMinder.DeviceManagement.Application.csproj`

Current full content:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Application\Sergin.SharedKernel.Application.csproj" />
    <ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Domain\Sergin.MeterMinder.DeviceManagement.Domain.csproj" />
  </ItemGroup>
</Project>
```

**Interfaces:**
- Consumes: `Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj` (Task 11).
- Produces: `.Application`'s output assembly transitively carries `.Application.Contracts` to `.Infrastructure` (and onward to the composition root).

- [ ] **Step 1: Add the third `ProjectReference`**

  In `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Application\Sergin.MeterMinder.DeviceManagement.Application.csproj`, change to:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <ItemGroup>
      <ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Application\Sergin.SharedKernel.Application.csproj" />
      <ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Domain\Sergin.MeterMinder.DeviceManagement.Domain.csproj" />
      <ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj" />
    </ItemGroup>
  </Project>
  ```

- [ ] **Step 2: Build to confirm `.Application`'s handlers still resolve their (now-moved) request/response types**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder
  dotnet build Sergin.MeterMinder.slnx
  ```
  Expected: this will still fail at this point, because `.Presentation.WebApi`, `.Presentation.Blazor`, and `.Presentation.Grpc` haven't had their references swapped yet (Task 15) — their `_Imports.razor`/handler-adjacent code references namespaces that used to live in `.Application` and no longer compile against a project that no longer exposes them. **This is expected and not a regression to chase down**; proceed to Task 15, then build again (Task 15's own build step) as the real gate.

- [ ] **Step 3: Commit**

  ```bash
  git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Sergin.MeterMinder.DeviceManagement.Application.csproj
  git commit -m "Reference Application.Contracts from Application for handler signatures"
  ```

### Task 15: Swap `.Presentation.WebApi`, `.Presentation.Blazor`, and `.Presentation.Grpc` references from `.Application` to `.Application.Contracts`

**Files:**
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.csproj`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.csproj`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj`

Current full content of `Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.WebApi\Sergin.SharedKernel.Presentation.WebApi.csproj" />
    <ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Application\Sergin.MeterMinder.DeviceManagement.Application.csproj" />
  </ItemGroup>
</Project>
```

Current full content of `Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<ItemGroup>
		<FrameworkReference Include="Microsoft.AspNetCore.App" />

		<PackageReference Include="MudBlazor" />
	</ItemGroup>

	<ItemGroup>
		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Modules\Sergin.SharedKernel.Modules.csproj" />
		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Blazor\Sergin.SharedKernel.Presentation.Blazor.csproj" />
		<ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Application\Sergin.MeterMinder.DeviceManagement.Application.csproj" />
	</ItemGroup>
</Project>
```

Current full content of `Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

	<ItemGroup>
		<FrameworkReference Include="Microsoft.AspNetCore.App" />
	</ItemGroup>

	<ItemGroup>
		<PackageReference Include="Google.Protobuf" />
		<PackageReference Include="Grpc.AspNetCore" />
		<PackageReference Include="Grpc.Net.Client" />
		<PackageReference Include="Grpc.Tools">
			<PrivateAssets>all</PrivateAssets>
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
		</PackageReference>
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

`.Presentation.Grpc`'s `GetDeviceByIdGrpcInvoker.cs` (client) and `DeviceGrpcService.cs` (server) both implement the dual-mode dispatch mechanism against `GetDeviceByIdQueryCommand`/`DeviceQueryResponse` — exactly the types that moved in Task 12 — and its `GlobalUsings.cs` is `global using ErrorOr; global using MediatR;` only (no `.Application`-namespace global), so the swap is safe by the same reasoning as WebApi/Blazor.

**Interfaces:**
- Consumes: `Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj` (Task 11), the moved types (Tasks 12–13).
- Produces: nothing new consumed later in this phase.

- [ ] **Step 1: Swap the reference in `.Presentation.WebApi`**

  In `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Presentation.WebApi\Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.csproj`, change to:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <ItemGroup>
      <ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.WebApi\Sergin.SharedKernel.Presentation.WebApi.csproj" />
      <ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj" />
    </ItemGroup>
  </Project>
  ```

- [ ] **Step 2: Swap the reference in `.Presentation.Blazor`**

  In `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Presentation.Blazor\Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.csproj`, change to:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk.Razor">
  	<ItemGroup>
  		<FrameworkReference Include="Microsoft.AspNetCore.App" />

  		<PackageReference Include="MudBlazor" />
  	</ItemGroup>

  	<ItemGroup>
  		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Modules\Sergin.SharedKernel.Modules.csproj" />
  		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Blazor\Sergin.SharedKernel.Presentation.Blazor.csproj" />
  		<ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj" />
  	</ItemGroup>
  </Project>
  ```

- [ ] **Step 3: Swap the reference in `.Presentation.Grpc`**

  In `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj`, change:
  ```xml
  		<ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Application\Sergin.MeterMinder.DeviceManagement.Application.csproj" />
  ```
  to:
  ```xml
  		<ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj" />
  ```
  Full file after the change:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

  	<ItemGroup>
  		<FrameworkReference Include="Microsoft.AspNetCore.App" />
  	</ItemGroup>

  	<ItemGroup>
  		<PackageReference Include="Google.Protobuf" />
  		<PackageReference Include="Grpc.AspNetCore" />
  		<PackageReference Include="Grpc.Net.Client" />
  		<PackageReference Include="Grpc.Tools">
  			<PrivateAssets>all</PrivateAssets>
  			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  		</PackageReference>
  	</ItemGroup>

  	<ItemGroup>
  		<ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj" />
  		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Grpc\Sergin.SharedKernel.Presentation.Grpc.csproj" />
  	</ItemGroup>

  	<ItemGroup>
  		<Protobuf Include="Protos\devices.proto" GrpcServices="Both" AdditionalImportDirs="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Grpc\Protos" />
  	</ItemGroup>

  </Project>
  ```

- [ ] **Step 4: Build to confirm all three presentation projects compile against `.Application.Contracts`**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder
  dotnet build Sergin.MeterMinder.slnx
  ```
  Expected: this build is still expected to fail at this point — `DeviceManagementModule.cs` (Task 16) hasn't yet implemented `ContractsAssembly`, so the DeviceManagement composition-root project doesn't satisfy the interface member added in Phase 1, Task 1. UserAccess's `UserAccessModule` from Phase 2 also isn't visible yet to this build, because the host repo's submodule pointer for `src/Modules/UserAccess` hasn't been bumped (Task 18). **Both are expected and addressed in the next two tasks** — this step exists to catch any *unexpected* compile error in the three csproj edits themselves (e.g. a typo in a relative path), which would show up as a "project file not found" or "type or namespace not found" error distinct from the still-missing `ContractsAssembly` implementation.

- [ ] **Step 5: Commit**

  ```bash
  git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.csproj src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.csproj src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj
  git commit -m "Point DeviceManagement presentation projects at Application.Contracts instead of Application"
  ```

### Task 16: Implement `ContractsAssembly` on `DeviceManagementModule`

**Files:**
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement/DeviceManagementModule.cs`

Current full content:
```csharp
using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.MeterMinder.DeviceManagement.Devices;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.MeterMinder.DeviceManagement.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Modules;

namespace Sergin.MeterMinder.DeviceManagement;

public sealed class DeviceManagementModule : ISerginWebApiModule, ISerginWebUiModule
{
    public string Schema => DeviceManagementDbContext.Schema;

    public Assembly ApplicationAssembly => DeviceManagementApplicationAssemblyReference.Assembly;

    public Assembly UiAssembly => DeviceManagementBlazorAssemblyReference.Assembly;

    public IReadOnlyCollection<SerginNavItem> NavItems => DeviceManagementNavigation.Items;

    public void AddServices(IServiceCollection services, IConfigurationSection configuration)
    {
        services.AddModuleDbContext<DeviceManagementDbContext, IDeviceManagementDbContext, IDeviceManagementUnitOfWork>(configuration, DeviceManagementDbContext.Schema);

        services.AddDeviceDependencies();
        services.AddManufacturerDependencies();
    }

    public Task MigrateAsync(IServiceProvider services) => services.MigrateDbContextAsync<DeviceManagementDbContext>();

    public void MapEndpoints(RouteGroupBuilder group) => group.MapDeviceEndpoints().MapManufacturerEndpoints();
}
```

The composition root (`Sergin.MeterMinder.DeviceManagement.csproj`, referencing `.Infrastructure`, `.Presentation.WebApi`, `.Presentation.Blazor`) gets `.Application.Contracts` transitively the same way `UserAccessModule` does in Task 9: via `.Infrastructure` → `.Application` → `.Application.Contracts` (Task 14) and via `.Presentation.WebApi`/`.Presentation.Blazor` → `.Application.Contracts` directly (Task 15).

**Interfaces:**
- Consumes: `DeviceManagementApplicationContractsAssemblyReference.Assembly` (Task 11), `ISerginModule.ContractsAssembly` (Phase 1, Task 1).
- Produces: `DeviceManagementModule` now fully implements the extended `ISerginModule`, consumed at runtime by `Sergin.MeterMinder.Hosts.All`'s `Program.cs` (unchanged — it already does `new DeviceManagementModule()`).

- [ ] **Step 1: Add the `ContractsAssembly` property and its `using`**

  In `C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement\DeviceManagementModule.cs`, change:
  ```csharp
  using System.Reflection;
  using Microsoft.AspNetCore.Routing;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;
  using Sergin.MeterMinder.DeviceManagement.Application;
  using Sergin.MeterMinder.DeviceManagement.Devices;
  using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
  using Sergin.MeterMinder.DeviceManagement.Manufacturers;
  using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor;
  using Sergin.SharedKernel.Infrastructure.Data.EFCore;
  using Sergin.SharedKernel.Modules;

  namespace Sergin.MeterMinder.DeviceManagement;

  public sealed class DeviceManagementModule : ISerginWebApiModule, ISerginWebUiModule
  {
      public string Schema => DeviceManagementDbContext.Schema;

      public Assembly ApplicationAssembly => DeviceManagementApplicationAssemblyReference.Assembly;

      public Assembly UiAssembly => DeviceManagementBlazorAssemblyReference.Assembly;
  ```
  to:
  ```csharp
  using System.Reflection;
  using Microsoft.AspNetCore.Routing;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;
  using Sergin.MeterMinder.DeviceManagement.Application;
  using Sergin.MeterMinder.DeviceManagement.Application.Contracts;
  using Sergin.MeterMinder.DeviceManagement.Devices;
  using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
  using Sergin.MeterMinder.DeviceManagement.Manufacturers;
  using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor;
  using Sergin.SharedKernel.Infrastructure.Data.EFCore;
  using Sergin.SharedKernel.Modules;

  namespace Sergin.MeterMinder.DeviceManagement;

  public sealed class DeviceManagementModule : ISerginWebApiModule, ISerginWebUiModule
  {
      public string Schema => DeviceManagementDbContext.Schema;

      public Assembly ApplicationAssembly => DeviceManagementApplicationAssemblyReference.Assembly;

      public Assembly ContractsAssembly => DeviceManagementApplicationContractsAssemblyReference.Assembly;

      public Assembly UiAssembly => DeviceManagementBlazorAssemblyReference.Assembly;
  ```

  (The rest of the file is unchanged.)

- [ ] **Step 2: Build**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder
  dotnet build Sergin.MeterMinder.slnx
  ```
  Expected: DeviceManagement-side compile errors from the missing `ContractsAssembly` implementation are now resolved. The build may still fail if `src/Modules/UserAccess` (a submodule) doesn't yet have Phase 2's commits checked out in this working copy's submodule pointer — that's Task 18. If this working copy's `src/Modules/UserAccess` directory already *is* the same working tree Phase 2 committed into (true under this plan's single-working-copy assumption — Phase 2 committed directly inside `src/Modules/UserAccess/`), then the submodule's working directory already has Phase 2's changes on disk, and only the host repo's recorded submodule *pointer* (a gitlink SHA) is stale until Task 18's commit — but `dotnet build` reads files from disk, not from git's recorded pointer, so **this build should already succeed** once Task 16 lands, without needing to wait for Task 18. Task 18 exists to make the host repo's own commit correctly record the submodule pointer for anyone re-cloning, not to unblock this build.

- [ ] **Step 3: Commit**

  ```bash
  git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement/DeviceManagementModule.cs
  git commit -m "Implement ISerginModule.ContractsAssembly on DeviceManagementModule"
  ```

### Task 17: Add the DeviceManagement Contracts project's build-verification checkpoint

**Files:**
- None (verification-only task; confirms Tasks 11–16 together produce a clean build with both modules' `ContractsAssembly` implemented).

**Interfaces:**
- Consumes: everything from Tasks 11–16.
- Produces: a build in a known-good state before touching submodule pointers (Task 18) and `Sergin.MeterMinder.slnx`'s remaining folder (Task 19 already added the DeviceManagement `.Contracts` project in Task 11 — this task's build is the first one where every DeviceManagement-side file this plan touches is in place).

- [ ] **Step 1: Full build**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder
  dotnet build Sergin.MeterMinder.slnx
  ```
  Expected: succeeds. If it doesn't, the failure is scoped to something in Tasks 11–16 (DeviceManagement side) or an already-committed Phase 2 change (UserAccess side, present on disk in this working copy even though the host repo's submodule pointer isn't bumped yet) — re-check the specific csproj/`.cs` diffs from those tasks against what's actually on disk before proceeding.

- [ ] **Step 2: No commit** — this is a checkpoint, not a change. Proceed to Task 18.

### Task 18: Bump both submodule pointers in the host repo's index

**Files:**
- Modify (gitlink, not a text file): `src/SharedKernel` (submodule pointer)
- Modify (gitlink, not a text file): `src/Modules/UserAccess` (submodule pointer)

Under this plan's single-working-copy assumption, Phases 1 and 2 already committed directly into `src/SharedKernel/` and `src/Modules/UserAccess/`'s own git histories (each is a real git repository via `.git` gitlink, mounted as a submodule of the host). The host repo's index currently still points at the commit that existed *before* those phases ran. This task's `git add` (not a code edit) records the new tip commits of each submodule as the pointer the host repo tracks.

**Interfaces:**
- Consumes: Phase 1's final commit (SharedKernel, Task 4), Phase 2's final commit (UserAccess, Task 10).
- Produces: a host-repo commit that, on a fresh clone + `git submodule update --init --recursive`, checks out both submodules at the commits containing this plan's Phase 1/2 changes.

- [ ] **Step 1: Confirm each submodule's working tree is at the expected commit**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder\src\SharedKernel
  git log -1 --oneline
  cd C:\@factory\Sergin\Sergin.MeterMinder\src\Modules\UserAccess
  git log -1 --oneline
  ```
  Expected: the SharedKernel log shows Task 4's commit ("Document ISerginModule.ContractsAssembly in SharedKernel CLAUDE.md") as HEAD; the UserAccess log shows Task 10's commit ("Document Sergin.UserAccess.Application.Contracts in module CLAUDE.md") as HEAD.

- [ ] **Step 2: Stage the submodule pointer changes from the host repo root**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder
  git add src/SharedKernel src/Modules/UserAccess
  git status
  ```
  Expected: `git status` shows both paths staged with a message indicating a new commit is tracked (`modified: src/SharedKernel (new commits)` / `modified: src/Modules/UserAccess (new commits)`), not a full directory diff — `git add` on a submodule path stages only the gitlink SHA change.

  (No separate commit here — this stages the pointer bump alongside the rest of Phase 3's remaining changes, committed together at Task 22.)

### Task 19: Confirm `Sergin.MeterMinder.slnx` needs no further edit

**Files:**
- None (verification-only; Task 11, Step 4 already added the one project entry this plan requires).

Per the spec (§5: "`Sergin.MeterMinder.slnx` gains 2 new project entries, one per module's new Contracts project"), the second entry — `Sergin.UserAccess.Application.Contracts` — is **not** added to `Sergin.MeterMinder.slnx` in this plan, and that is correct, not an omission: `Sergin.UserAccess.Application.Contracts.csproj` lives inside the `src/Modules/UserAccess/` submodule, and that submodule's projects are *not* individually listed as `<Project Path="...">` entries in this solution file at all — re-reading the current `Sergin.MeterMinder.slnx` (captured in full while researching this plan) shows its `/src/Modules/UserAccess/` folder already lists `Sergin.UserAccess.Application`, `Sergin.UserAccess.Domain`, `Sergin.UserAccess.Infrastructure.Data`, `Sergin.UserAccess.Infrastructure`, and `Sergin.UserAccess`, plus a `/src/Modules/UserAccess/Presentation/` folder for `.Presentation.WebApi`/`.Presentation.Blazor` — so UserAccess projects **are** individually listed, the same as DeviceManagement's. This task therefore corrects course: the second slnx entry **is** required.

**Interfaces:**
- Consumes: nothing.
- Produces: the corrected instruction executed as Task 19a below.

- [ ] **Step 1 (Task 19a): Add `Sergin.UserAccess.Application.Contracts` to `Sergin.MeterMinder.slnx`**

  In `C:\@factory\Sergin\Sergin.MeterMinder\Sergin.MeterMinder.slnx`, find the `/src/Modules/UserAccess/` folder block:
  ```xml
    <Folder Name="/src/Modules/UserAccess/">
      <Project Path="src/Modules/UserAccess/Sergin.UserAccess.Application/Sergin.UserAccess.Application.csproj" />
      <Project Path="src/Modules/UserAccess/Sergin.UserAccess.Domain/Sergin.UserAccess.Domain.csproj" />
      <Project Path="src/Modules/UserAccess/Sergin.UserAccess.Infrastructure.Data/Sergin.UserAccess.Infrastructure.Data.csproj" />
      <Project Path="src/Modules/UserAccess/Sergin.UserAccess.Infrastructure/Sergin.UserAccess.Infrastructure.csproj" />
      <Project Path="src/Modules/UserAccess/Sergin.UserAccess/Sergin.UserAccess.csproj" />
    </Folder>
  ```
  and change it to:
  ```xml
    <Folder Name="/src/Modules/UserAccess/">
      <Project Path="src/Modules/UserAccess/Sergin.UserAccess.Application.Contracts/Sergin.UserAccess.Application.Contracts.csproj" />
      <Project Path="src/Modules/UserAccess/Sergin.UserAccess.Application/Sergin.UserAccess.Application.csproj" />
      <Project Path="src/Modules/UserAccess/Sergin.UserAccess.Domain/Sergin.UserAccess.Domain.csproj" />
      <Project Path="src/Modules/UserAccess/Sergin.UserAccess.Infrastructure.Data/Sergin.UserAccess.Infrastructure.Data.csproj" />
      <Project Path="src/Modules/UserAccess/Sergin.UserAccess.Infrastructure/Sergin.UserAccess.Infrastructure.csproj" />
      <Project Path="src/Modules/UserAccess/Sergin.UserAccess/Sergin.UserAccess.csproj" />
    </Folder>
  ```

  (This addition is possible only now, in Phase 3, because `Sergin.MeterMinder.slnx` lives in the host repo — Phase 2 could not have made this edit from inside the UserAccess submodule.)

- [ ] **Step 2: No commit yet** — staged together with the rest of Phase 3 at Task 22.

### Task 20: Full build — the first point Phase 2's UserAccess changes are actually compiled

**Files:**
- None (verification-only).

**Interfaces:**
- Consumes: everything from Tasks 1–19.
- Produces: the confirmation Phase 2's preamble deferred — that `Sergin.UserAccess.Application.Contracts` (created purely by file-reasoning in Phase 2, never build-verified there) actually compiles once mounted in this host.

- [ ] **Step 1: Build**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder
  dotnet build Sergin.MeterMinder.slnx
  ```
  Expected: succeeds. This is the first real compiler confirmation of every file created/edited in Phase 2 (Tasks 5–10) — if it fails, the likely causes are a typo in one of Phase 2's hand-written files (compare against the exact content blocks in Tasks 5–10 above) or a missed reference swap.

- [ ] **Step 2: If the build fails, do not proceed** — fix the specific file named in the compiler error, matching it back against the exact content shown in this plan's Phase 2 tasks, then re-run Step 1. Do not improvise a different shape than what Phase 2 specified — the whole point of the verbatim-move constraint is that every file's content is already fully determined by the plan.

### Task 21: Update `/add-feature` and `/add-module` skills to scaffold into `.Application.Contracts`

**Files:**
- Modify: `.claude/skills/add-feature/SKILL.md`
- Modify: `.claude/skills/add-module/SKILL.md`

**Interfaces:**
- Consumes: nothing (documentation only).
- Produces: nothing (documentation only) — but without this task, the next `/add-feature` invocation regresses a scaffolded feature's command/response types back into `.Application`, per the spec's §5/Risks section.

- [ ] **Step 1: Update `/add-feature`'s "Layout to create" numbered list, items 1–2**

  In `C:\@factory\Sergin\Sergin.MeterMinder\.claude\skills\add-feature\SKILL.md`, change:
  ```
  **Command** (state-changing):
  1. `src/Modules/<Module>/Sergin.<Module>.Application/<Aggregate>/Commands/<Feature>/<Feature>Command.cs` — `public sealed record <Feature>Command(...) : ICommand<<Feature>CommandResponse>;`
  2. `.../<Feature>/<Feature>CommandResponse.cs` — `public sealed record <Feature>CommandResponse(...);`
  3. `.../<Feature>/<Feature>CommandHandler.cs` — `internal sealed class` implementing `ICommandHandler<TCommand, TResponse>`, primary-constructor-injects `I<Module>UnitOfWork` + the domain repository, calls a domain factory/behavior method, calls `unitOfWork.SaveChangesAsync`, returns the response.
  ```
  to:
  ```
  **Command** (state-changing):
  1. `src/Modules/<Module>/Sergin.<Module>.Application.Contracts/<Aggregate>/Commands/<Feature>/<Feature>Command.cs` — `public sealed record <Feature>Command(...) : ICommand<<Feature>CommandResponse>;`
  2. `.../<Feature>/<Feature>CommandResponse.cs` (same `.Application.Contracts` project) — `public sealed record <Feature>CommandResponse(...);`
  3. `src/Modules/<Module>/Sergin.<Module>.Application/<Aggregate>/Commands/<Feature>/<Feature>CommandHandler.cs` (note: this file — the handler — stays in `.Application`, not `.Application.Contracts`; only the request/response records from steps 1–2 live in `.Application.Contracts`) — `internal sealed class` implementing `ICommandHandler<TCommand, TResponse>`, primary-constructor-injects `I<Module>UnitOfWork` + the domain repository, calls a domain factory/behavior method, calls `unitOfWork.SaveChangesAsync`, returns the response.
  ```

- [ ] **Step 2: Update the "Query" paragraph's opening sentence**

  Change:
  ```
  **Query** (read-side, bypasses EF):
  Same shape but under `Commands/<Feature>/` still (this repo keeps queries in the `Commands` folder alongside commands — match that, don't invent a `Queries` folder), implementing `IQuery<TResponse>` / `IQueryHandler<TQuery, TResponse>` from `Sergin.SharedKernel.Application.Commands.Queries`. The handler depends on a dedicated `I<Feature>QueryRepository` interface (returns nullable response, handler maps null to `Error.NotFound()`). Implement that interface in `Sergin.<Module>.Infrastructure/<Aggregate>/Repositories/Queries/<Aggregate>QueryRepository.cs` using `IDbConnectionFactory` + raw SQL against the module's Postgres schema (see `UserQueryRepository.cs` for the `QuerySingleOrDefaultAsync` / `QueryMultipleAsync` Dapper-style pattern) — never use EF Core for reads. If the query needs authorization, add `[RequiredPermissions("permission.<schema>.<resource>.<action>")]` on the query record.
  ```
  to:
  ```
  **Query** (read-side, bypasses EF):
  Same shape but under `Commands/<Feature>/` still (this repo keeps queries in the `Commands` folder alongside commands — match that, don't invent a `Queries` folder), implementing `IQuery<TResponse>` / `IQueryHandler<TQuery, TResponse>` from `Sergin.SharedKernel.Application.Commands.Queries`. **The query request record and its response record go in `Sergin.<Module>.Application.Contracts/<Aggregate>/Commands/<Feature>/`, same as a command's records — only the `<Feature>QueryCommandHandler.cs` class and the `I<Feature>QueryRepository` interface stay in `Sergin.<Module>.Application`.** The handler depends on a dedicated `I<Feature>QueryRepository` interface (returns nullable response, handler maps null to `Error.NotFound()`). Implement that interface in `Sergin.<Module>.Infrastructure/<Aggregate>/Repositories/Queries/<Aggregate>QueryRepository.cs` using `IDbConnectionFactory` + raw SQL against the module's Postgres schema (see `UserQueryRepository.cs` for the `QuerySingleOrDefaultAsync` / `QueryMultipleAsync` Dapper-style pattern) — never use EF Core for reads. If the query needs authorization, add `[RequiredPermissions("permission.<schema>.<resource>.<action>")]` on the query record (which now lives in `.Application.Contracts`).
  ```

- [ ] **Step 3: Update the "After scaffolding" checklist's GlobalUsings reminder**

  Change:
  ```
  1. Check each new project's `GlobalUsings.cs` before adding `using` statements — many namespaces (`ErrorOr`, `Sergin.SharedKernel.*`) are already global. In `.Presentation.Blazor` check `_Imports.razor` as well — it covers the markup, `GlobalUsings.cs` covers the code-behind.
  ```
  to:
  ```
  1. Check each new project's `GlobalUsings.cs` before adding `using` statements — many namespaces (`ErrorOr`, `Sergin.SharedKernel.*`) are already global. In `.Presentation.Blazor` check `_Imports.razor` as well — it covers the markup, `GlobalUsings.cs` covers the code-behind. A brand-new feature's request/response records live in `.Application.Contracts`, so `.Presentation.WebApi`/`.Presentation.Blazor` reference that project, not `.Application` — don't add a `.Application` `ProjectReference` alongside it for a new feature; the handler-bearing `.Application` project is never a presentation dependency.
  ```

- [ ] **Step 4: Update `/add-module`'s project table**

  In `C:\@factory\Sergin\Sergin.MeterMinder\.claude\skills\add-module\SKILL.md`, change the table under "## 1. Create six projects...":
  ```
  | Project | References | GlobalUsings.cs |
  |---|---|---|
  | `Sergin.<Module>.Domain` | `SharedKernel.Domain` | `global using ErrorOr;` / `global using Ardalis.GuardClauses;` |
  | `Sergin.<Module>.Application` | `SharedKernel.Application`, `<Module>.Domain` | `global using ErrorOr;` / `Sergin.SharedKernel.Domain` / `Sergin.SharedKernel.Application` — **not** `Sergin.<Module>.Domain` yet (see note below) |
  | `Sergin.<Module>.Infrastructure` | `SharedKernel.Infrastructure`, `<Module>.Application`, `<Module>.Infrastructure.Data` | `global using Dapper;` / `global using static Dapper.SqlMapper;` |
  | `Sergin.<Module>.Infrastructure.Data` | `SharedKernel.Infrastructure.Data.EFCore`, `<Module>.Application` | (none needed yet — add if EF namespaces get noisy) |
  | `Sergin.<Module>.Presentation.WebApi` | `SharedKernel.Presentation.WebApi`, `<Module>.Application` | `global using ErrorOr;` / `MediatR` / `Sergin.SharedKernel.Presentation` / `Sergin.SharedKernel.Presentation.WebApi` / `Sergin.SharedKernel.Presentation.WebApi.Endpoints` |
  | `Sergin.<Module>` (composition root, no suffix) | `<Module>.Infrastructure`, `<Module>.Presentation.WebApi`, `SharedKernel.Modules` (+ `<Module>.Presentation.Blazor` if the module has a UI) | (none) |
  ```
  to:
  ```
  | Project | References | GlobalUsings.cs |
  |---|---|---|
  | `Sergin.<Module>.Domain` | `SharedKernel.Domain` | `global using ErrorOr;` / `global using Ardalis.GuardClauses;` |
  | `Sergin.<Module>.Application.Contracts` | `SharedKernel.Application`, `<Module>.Domain` | `global using ErrorOr;` / `Sergin.SharedKernel.Domain` / `Sergin.SharedKernel.Application` — **not** `Sergin.<Module>.Domain` yet (see note below) |
  | `Sergin.<Module>.Application` | `SharedKernel.Application`, `<Module>.Domain`, `<Module>.Application.Contracts` | same globals as `.Application.Contracts`, since handlers need the same domain/SharedKernel imports plus the request/response types `.Application.Contracts` now holds |
  | `Sergin.<Module>.Infrastructure` | `SharedKernel.Infrastructure`, `<Module>.Application`, `<Module>.Infrastructure.Data` | `global using Dapper;` / `global using static Dapper.SqlMapper;` |
  | `Sergin.<Module>.Infrastructure.Data` | `SharedKernel.Infrastructure.Data.EFCore`, `<Module>.Application` | (none needed yet — add if EF namespaces get noisy) |
  | `Sergin.<Module>.Presentation.WebApi` | `SharedKernel.Presentation.WebApi`, `<Module>.Application.Contracts` | `global using ErrorOr;` / `MediatR` / `Sergin.SharedKernel.Presentation` / `Sergin.SharedKernel.Presentation.WebApi` / `Sergin.SharedKernel.Presentation.WebApi.Endpoints` |
  | `Sergin.<Module>` (composition root, no suffix) | `<Module>.Infrastructure`, `<Module>.Presentation.WebApi`, `SharedKernel.Modules` (+ `<Module>.Presentation.Blazor` if the module has a UI) | (none) |
  ```

- [ ] **Step 5: Update `/add-module`'s Presentation.Blazor reference snippet**

  Change:
  ```xml
  	<ItemGroup>
  		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Modules\Sergin.SharedKernel.Modules.csproj" />
  		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Blazor\Sergin.SharedKernel.Presentation.Blazor.csproj" />
  		<ProjectReference Include="..\Sergin.<Module>.Application\Sergin.<Module>.Application.csproj" />
  	</ItemGroup>
  ```
  to:
  ```xml
  	<ItemGroup>
  		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Modules\Sergin.SharedKernel.Modules.csproj" />
  		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Blazor\Sergin.SharedKernel.Presentation.Blazor.csproj" />
  		<ProjectReference Include="..\Sergin.<Module>.Application.Contracts\Sergin.<Module>.Application.Contracts.csproj" />
  	</ItemGroup>
  ```
  and the sentence right after it, from:
  ```
  It references the module's `.Application` and **never** its `.Infrastructure` — pages reach handlers through MediatR only.
  ```
  to:
  ```
  It references the module's `.Application.Contracts` (never `.Application` directly, and never `.Infrastructure`) — pages reach handlers through MediatR only, and only need the request/response record shapes `.Application.Contracts` holds.
  ```

- [ ] **Step 6: Update `/add-module`'s "## 2. Application-layer plumbing" section**

  Change:
  ```
  ## 2. Application-layer plumbing (composition root of DI/MediatR)

  In `Sergin.<Module>.Application/`:
  - `<Module>AssemblyReference.cs` — note the actual class name is **`<Module>ApplicationAssemblyReference`** (matches `UserAccessApplicationAssemblyReference`, not just `UserAccessAssemblyReference`), wrapping `typeof(...).Assembly` for MediatR scanning.
  - `I<Module>UnitOfWork.cs` — `public interface I<Module>UnitOfWork : IUnitOfWork;` (from `Sergin.SharedKernel.Application`).
  ```
  to:
  ```
  ## 2. Application-layer plumbing (composition root of DI/MediatR)

  In `Sergin.<Module>.Application/`:
  - `<Module>AssemblyReference.cs` — note the actual class name is **`<Module>ApplicationAssemblyReference`** (matches `UserAccessApplicationAssemblyReference`, not just `UserAccessAssemblyReference`), wrapping `typeof(...).Assembly` for MediatR scanning.
  - `I<Module>UnitOfWork.cs` — `public interface I<Module>UnitOfWork : IUnitOfWork;` (from `Sergin.SharedKernel.Application`).

  In `Sergin.<Module>.Application.Contracts/`:
  - `<Module>ApplicationContractsAssemblyReference.cs` — `public static class <Module>ApplicationContractsAssemblyReference { public static readonly Assembly Assembly = typeof(<Module>ApplicationContractsAssemblyReference).Assembly; }`, wrapping `typeof(...).Assembly` for `ISerginModule.ContractsAssembly`. **Note this is a third, separate assembly-reference type from both `<Module>ApplicationAssemblyReference` and `<Module>BlazorAssemblyReference`** — don't conflate any of the three.
  ```

- [ ] **Step 7: Update `/add-module`'s `<Module>Module.cs` section**

  Change:
  ```
  - `Schema` → `<Module>DbContext.Schema`; `ApplicationAssembly` → `<Module>ApplicationAssemblyReference.Assembly`.
  ```
  to:
  ```
  - `Schema` → `<Module>DbContext.Schema`; `ApplicationAssembly` → `<Module>ApplicationAssemblyReference.Assembly`; `ContractsAssembly` → `<Module>ApplicationContractsAssemblyReference.Assembly`.
  ```

- [ ] **Step 8: Commit**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder
  git add .claude/skills/add-feature/SKILL.md .claude/skills/add-module/SKILL.md
  git commit -m "Update add-feature and add-module skills for the Application Contracts split"
  ```

### Task 22: Update this repo's own `.claude/CLAUDE.md`

**Files:**
- Modify: `.claude/CLAUDE.md`

**Interfaces:**
- Consumes: nothing (documentation only).
- Produces: nothing (documentation only).

- [ ] **Step 1: Update the `ISerginModule` core-contract bullet in "Host / module composition"**

  In `C:\@factory\Sergin\Sergin.MeterMinder\.claude\CLAUDE.md`, change:
  ```
  - **Modules** live under `src/Modules/<ModuleName>/`: currently **`DeviceManagement`** (schema `dm`) and **`UserAccess`** (schema `ua`). A module is wired into hosts through its **`<Module>Module` class** (in the `Sergin.<Module>` composition project, no suffix), implementing the contracts it exposes from `Sergin.SharedKernel.Modules`. `ISerginModule` is the core contract — `Schema`, `ApplicationAssembly`, `AddServices` (calls the generic `AddModuleDbContext<TContext, TIContext, TIUnitOfWork>` helper plus per-aggregate `Add<X>Dependencies()`), `MigrateAsync` — and two capability interfaces extend it:
  ```
  to:
  ```
  - **Modules** live under `src/Modules/<ModuleName>/`: currently **`DeviceManagement`** (schema `dm`) and **`UserAccess`** (schema `ua`). A module is wired into hosts through its **`<Module>Module` class** (in the `Sergin.<Module>` composition project, no suffix), implementing the contracts it exposes from `Sergin.SharedKernel.Modules`. `ISerginModule` is the core contract — `Schema`, `ApplicationAssembly`, `ContractsAssembly`, `AddServices` (calls the generic `AddModuleDbContext<TContext, TIContext, TIUnitOfWork>` helper plus per-aggregate `Add<X>Dependencies()`), `MigrateAsync` — and two capability interfaces extend it:
  ```

- [ ] **Step 2: Add a `ContractsAssembly` explanation right after the two capability-interface bullets**

  Change:
  ```
    - **`ISerginWebApiModule`** adds `MapEndpoints(RouteGroupBuilder)` (per-aggregate `Map<X>Endpoints()`).
    - **`ISerginWebUiModule`** adds `UiAssembly` (the assembly holding the module's routable Razor components — **never `ApplicationAssembly`**, which is deliberately UI-free) and `NavItems` (`IReadOnlyCollection<SerginNavItem>`; `SerginNavItem` is `(Label, Href, Icon, Order)`, with `Icon` a plain `string` so the contract leaf stays free of any UI library — the modules currently pass MudBlazor `Icons.Material.*` constants into it).

    **One class per module implements all its capabilities** — both `DeviceManagementModule` and `UserAccessModule` are declared `: ISerginWebApiModule, ISerginWebUiModule` — and which capabilities actually run is the host's choice: the UI host only ever reads `UiAssembly`/`NavItems`, and with no API host today nothing calls `MapEndpoints` at all. Keep both implemented anyway; that is exactly what makes re-adding an API host cheap. Each module has its own `CLAUDE.md` (`src/Modules/<Module>/CLAUDE.md`) covering aggregate-specific details (implemented feature slices, quirks, unfinished pieces) that don't belong here.
  ```
  to:
  ```
    - **`ISerginWebApiModule`** adds `MapEndpoints(RouteGroupBuilder)` (per-aggregate `Map<X>Endpoints()`).
    - **`ISerginWebUiModule`** adds `UiAssembly` (the assembly holding the module's routable Razor components — **never `ApplicationAssembly`**, which is deliberately UI-free) and `NavItems` (`IReadOnlyCollection<SerginNavItem>`; `SerginNavItem` is `(Label, Href, Icon, Order)`, with `Icon` a plain `string` so the contract leaf stays free of any UI library — the modules currently pass MudBlazor `Icons.Material.*` constants into it).

    **One class per module implements all its capabilities** — both `DeviceManagementModule` and `UserAccessModule` are declared `: ISerginWebApiModule, ISerginWebUiModule` — and which capabilities actually run is the host's choice: the UI host only ever reads `UiAssembly`/`NavItems`, and with no API host today nothing calls `MapEndpoints` at all. Keep both implemented anyway; that is exactly what makes re-adding an API host cheap. Each module has its own `CLAUDE.md` (`src/Modules/<Module>/CLAUDE.md`) covering aggregate-specific details (implemented feature slices, quirks, unfinished pieces) that don't belong here.

  **`ApplicationAssembly` vs. `ContractsAssembly`**: each module ships a separate `.Application.Contracts` project (`Sergin.MeterMinder.DeviceManagement.Application.Contracts`, `Sergin.UserAccess.Application.Contracts`) holding only its MediatR command/query request and response records — the shapes a presentation layer actually needs. `ApplicationAssembly` still points at the module's handler-bearing `.Application` project (unaffected — `AddSerginCore`'s MediatR scan targets `ApplicationAssembly` only, never `ContractsAssembly`, since Contracts has no handlers to find); `ContractsAssembly` points at the new thinner project. Every `.Presentation.WebApi`/`.Presentation.Blazor` project (and, for DeviceManagement, `.Presentation.Grpc`) references `.Application.Contracts` instead of `.Application`, so presentation no longer transitively pulls in handlers, repository interfaces, or `IUnitOfWork`. `ModuleDispatchRouteResolver`'s assembly→schema dictionary (built in `SerginWebUiExtensions.AddSerginBlazorApp`) maps **both** assemblies to the same module, since a dispatched request's record type may be declared in either one. See `docs/superpowers/specs/2026-08-22-application-contracts-split-design.md` for the full design and `docs/superpowers/plans/2026-08-22-application-contracts-split.md` for how it was rolled out.
  ```

- [ ] **Step 3: Update the "Per-module project layering" `.Application` bullet**

  Change:
  ```
  - **`.Application`** — MediatR commands/queries + handlers, `IUnitOfWork`, query repository interfaces. Feature folders hold the full slice under `<Aggregate>/Commands/<Feature>/...` — **queries live under `Commands/` too**, not a separate `Queries/` folder; don't invent one.
  ```
  to:
  ```
  - **`.Application.Contracts`** — *new*: the module's MediatR command/query request and response records only (e.g. `CreateDeviceCommand`, `CreateDeviceCommandResponse`, `GetDeviceByIdQueryCommand`, `DeviceQueryResponse`, `GetDeviceListItem`), moved verbatim out of `.Application` — same namespace, same domain-typed constructor arguments, same `[RequiredPermissions]` attributes. References only `SharedKernel.Application` and the module's own `.Domain` (command constructors stay domain-typed, per Approach B — no primitivization). Exists purely so `.Presentation.WebApi`/`.Presentation.Blazor`/`.Presentation.Grpc` can depend on request/response shapes without a transitive path to handlers, repository interfaces, or `IUnitOfWork`. Carries a `<Module>ApplicationContractsAssemblyReference` marker class exposing `typeof(...).Assembly` for `ISerginModule.ContractsAssembly`.
  - **`.Application`** — MediatR handlers, `IUnitOfWork`, query repository interfaces. References `.Application.Contracts` for the request/response types its handlers implement `ICommandHandler<TCommand, TResponse>`/`IQueryHandler<TQuery, TResponse>` against — that's a new `ProjectReference` this project gained as part of the split. Feature folders hold the full slice under `<Aggregate>/Commands/<Feature>/...` — **queries live under `Commands/` too**, not a separate `Queries/` folder; don't invent one. **The request/response record files themselves (`<Feature>Command.cs`, `<Feature>CommandResponse.cs`, `Get<Aggregate>ByIdQueryCommand.cs`, `<Aggregate>QueryResponse.cs`, `Get<Aggregate>ListItem.cs`) live in `.Application.Contracts`, not here** — only `<Feature>CommandHandler.cs`/`<Feature>QueryCommandHandler.cs` and the `I<Feature>QueryRepository`/`IUnitOfWork` interfaces stay in this project.
  ```

- [ ] **Step 4: Update the `.Presentation.Blazor` bullet's reference list**

  Change:
  ```
  - **`.Presentation.Blazor`** — *optional*; present for both modules today. A Razor Class Library (`Microsoft.NET.Sdk.Razor`, `FrameworkReference Microsoft.AspNetCore.App` + `PackageReference MudBlazor`) holding the module's routable pages, organized per aggregate as `<Aggregate>/Pages/*.razor` + `*.razor.cs` and `<Aggregate>/Models/*.cs`. It also carries a `<Module>BlazorAssemblyReference` (what `UiAssembly` returns) and a `<Module>Navigation` static class exposing `IReadOnlyCollection<SerginNavItem> Items` (what `NavItems` returns). It references `SharedKernel.Modules`, `SharedKernel.Presentation.Blazor`, and the module's own `.Application` — **not** `.Infrastructure`; pages reach handlers through MediatR, never a repository.
  ```
  to:
  ```
  - **`.Presentation.Blazor`** — *optional*; present for both modules today. A Razor Class Library (`Microsoft.NET.Sdk.Razor`, `FrameworkReference Microsoft.AspNetCore.App` + `PackageReference MudBlazor`) holding the module's routable pages, organized per aggregate as `<Aggregate>/Pages/*.razor` + `*.razor.cs` and `<Aggregate>/Models/*.cs`. It also carries a `<Module>BlazorAssemblyReference` (what `UiAssembly` returns) and a `<Module>Navigation` static class exposing `IReadOnlyCollection<SerginNavItem> Items` (what `NavItems` returns). It references `SharedKernel.Modules`, `SharedKernel.Presentation.Blazor`, and the module's own `.Application.Contracts` — **not** `.Application` and **not** `.Infrastructure`; pages reach handlers through MediatR, never a repository, and only need request/response record shapes, not handler-adjacent types.
  ```

- [ ] **Step 5: Update the `.Presentation.WebApi` bullet, if present, and the "CQRS structural gotchas"/"Cross-cutting conventions" prose that names `.Application`**

  Search this file for other bare mentions of `.Presentation.WebApi` referencing `.Application` and confirm none remain unaddressed; the codebase-wide convention section doesn't call out the WebApi project's specific `ProjectReference` target elsewhere, so no further edit is required there beyond Steps 3–4 above.

- [ ] **Step 6: Commit — this is Phase 3's final commit, and includes the staged submodule pointer bumps from Task 18**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder
  git add .claude/CLAUDE.md Sergin.MeterMinder.slnx src/SharedKernel src/Modules/UserAccess
  git status
  git commit -m "Split each module's Application project into Application + Application.Contracts

  Adds ISerginModule.ContractsAssembly (SharedKernel), a new
  Sergin.UserAccess.Application.Contracts project (UserAccess), and a new
  Sergin.MeterMinder.DeviceManagement.Application.Contracts project (this repo),
  each holding only MediatR command/query request and response records moved
  verbatim out of the module's .Application project. Presentation layers
  (.Presentation.WebApi, .Presentation.Blazor, and DeviceManagement's
  .Presentation.Grpc) now depend on the thinner .Contracts project instead of
  .Application, so they no longer transitively reference handlers, repository
  interfaces, or IUnitOfWork. ModuleDispatchRouteResolver's assembly-to-schema
  map now recognizes both ApplicationAssembly and ContractsAssembly per module."
  ```

  Note: `git status` before the commit should show the two submodule gitlinks plus `Sergin.MeterMinder.slnx` and `.claude/CLAUDE.md` staged — if any DeviceManagement source files from Tasks 11–17 show as unstaged at this point, they were already committed individually in those tasks' own commit steps and won't appear here; only the files this specific task's `git add` targets should be new to the index.

### Task 23: Full build + integration test — the plan's real end-to-end verification gate

**Files:**
- None (verification-only).

**Interfaces:**
- Consumes: everything from Tasks 1–22.
- Produces: the spec's §7 testing requirement satisfied — confirmation that the existing integration suite, unmodified, still passes after the structural move.

- [ ] **Step 1: Confirm submodules are checked out at the expected commits (sanity check after Task 18's pointer bump)**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder
  git submodule status
  ```
  Expected: no leading `-` (which would mean not initialized) or `+` (which would mean the working tree is ahead of/different from the committed pointer, expected transiently before Task 22's commit but not after it) on either submodule line, once Task 22's commit has landed.

- [ ] **Step 2: Build the full solution**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder
  dotnet build Sergin.MeterMinder.slnx
  ```
  Expected: succeeds with zero warnings (warnings are errors, per Global Constraints).

- [ ] **Step 3: Run the integration test suite (requires Docker — spins up a real `postgres:17` via Testcontainers, per this repo's CLAUDE.md)**

  ```bash
  cd C:\@factory\Sergin\Sergin.MeterMinder
  dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj
  ```
  Expected: all tests pass, specifically:
  - `Shell/ModulePageRenderingTests.cs` — every module page still dispatches successfully; this is what would have caught a resolver-fix omission, since every dispatch of a DeviceManagement or UserAccess request now resolves its module via a request type declared in `.Application.Contracts`, not `.Application`.
  - `Users/CreateAndGetUserTests.cs` — the write-path test, dispatching `CreateUserCommand` (now declared in `Sergin.UserAccess.Application.Contracts`, per Task 6) via `ISerginUiDispatcher` end to end through Postgres.
  - `DeviceGrpcRoundTripTests` — spins up a real loopback Kestrel gRPC server exercising `Sergin.MeterMinder.DeviceManagement.Presentation.Grpc`'s `GetDeviceByIdGrpcInvoker`/`DeviceGrpcService` against `GetDeviceByIdQueryCommand`/`DeviceQueryResponse` (now declared in `Sergin.MeterMinder.DeviceManagement.Application.Contracts`, per Task 12) — this is what would have caught a `.Presentation.Grpc` reference-swap mistake specifically.

- [ ] **Step 4: If any test fails, do not weaken or delete it to make the suite pass** — per this plan's framing, this is a structural move with no intended behavior change; a failing test means something in Tasks 1–22 deviated from the "verbatim move, same namespace, same code" contract this plan specifies throughout. Diff the failing area against the exact file contents shown in the relevant earlier task and correct the deviation.

This is the plan's last task. No further commit is made in this task — Task 22 is Phase 3's (and the whole plan's) final commit; this task only verifies it.

---

## Self-review

**1. Spec coverage.**
- §1 (new projects, references, marker classes) — Tasks 5, 11.
- §2 (what moves, verbatim, namespace-preserved) — Tasks 6, 12, 13; the spec's own open follow-up about the full Manufacturer set is resolved in Task 13, and the same "move everything presentation needs" principle is applied to UserAccess's `DeactivateUser` in Task 6, which the spec's illustrative list didn't name but which the same rule requires.
- §3 (dependency graph after the change: `.Application.Contracts` ← `.Application` ← composition root; presentation → `.Contracts`) — Tasks 7, 9 (UserAccess) and Tasks 14, 16 (DeviceManagement) implement the back-reference and composition-root reasoning; Tasks 8, 15 implement the presentation swaps.
- §4 (the resolver fix, "risky part," same-commit requirement) — Phase 1 Tasks 1–3, called out explicitly in Global Constraints and in Task 3's rationale for why landing it *before* any type moves is safe under this plan's sequencing.
- §5 (slnx entries, skill updates, CLAUDE.md updates) — Task 11 Step 4 + Task 19 (slnx, both entries — Task 19 corrects an assumption from the spec's own casual phrasing about only one submodule needing a slnx entry), Task 21 (skills), Tasks 4, 10, 22 (all three CLAUDE.md files).
- §6 (rollout sequencing, three phases) — the whole plan's Phase 1/2/3 structure, with the single-working-copy assumption stated explicitly in Global Constraints per the task instructions' guidance to "pick the simpler assumption."
- §7 (testing/verification, specific test files named) — Task 23, naming all three tests the spec calls out by file.
- Risks section — the "hard, all-or-nothing cutover" risk is directly addressed by bundling the resolver fix ahead of any type move (Phase 1 before Phase 2/3) plus the single-working-copy assumption removing the "partially-landed across repos" risk; the "scaffolding-skill drift" risk is addressed by Task 21; the "not fully dependency-light" caveat is preserved verbatim in Global Constraints' "verbatim move, no primitivization" bullet and not overstated anywhere in this plan.
- Open follow-ups — primitivization (Approach A) is explicitly out of scope and not attempted anywhere in this plan; the `ContractsAssembly` rollout-mechanism question is answered in Global Constraints with justification; the Manufacturer type-set confirmation is resolved in Task 13.

No gaps found; no additional tasks needed.

**2. Placeholder scan.** Searched this plan's own text for "TBD," "TODO," "implement later," "add appropriate," "add validation," "handle edge cases," "similar to Task," and unshown code steps. None found — every `.cs`/`.csproj`/`.md` change is shown as a full before/after content block or a full new-file content block; every `git`/`dotnet` command is copy-pasteable with concrete paths; Task 19's self-correcting structure ("confirm no further edit is needed" → "this task therefore corrects course") is the one place the plan visibly reasons about itself, but it still resolves to concrete, fully-specified XML, not a deferred decision.

**3. Type/signature consistency.** Verified across tasks:
- `ISerginModule.ContractsAssembly` (`Assembly`, get-only) — declared Task 1, implemented identically in Task 9 (`UserAccessModule`) and Task 16 (`DeviceManagementModule`), both as `public Assembly ContractsAssembly => <Module>ApplicationContractsAssemblyReference.Assembly;`.
- `UserAccessApplicationContractsAssemblyReference` (namespace `Sergin.UserAccess.Application.Contracts`) — declared Task 5 Step 2, consumed identically in Task 9.
- `DeviceManagementApplicationContractsAssemblyReference` (namespace `Sergin.MeterMinder.DeviceManagement.Application.Contracts`) — declared Task 11 Step 2, consumed identically in Task 16.
- `ModuleDispatchRouteResolver` constructor signature `(IReadOnlyDictionary<Assembly, string> schemaByAssembly, IOptions<DispatchModeOptions> options)` — unchanged from the file as read (Task 2's excerpt), and Task 3's replacement call site produces exactly an `IReadOnlyDictionary<Assembly, string>` typed local before passing it, matching the parameter type.
- Every moved record's exact signature (`CreateDeviceCommand(DeviceId DeviceId, ManufacturerId ManufacturerId)`, `GetUserByIdQueryCommand(Guid Id)` with `[RequiredPermissions("permission.ua.users.read")]`, etc.) is reproduced identically between its "Interfaces: Produces" line and its "Step" code block within the same task, and matches the original file content captured while researching this plan.
- Project names used in later `<ProjectReference>` paths (`Sergin.UserAccess.Application.Contracts.csproj`, `Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj`) match exactly the csproj filenames created in Tasks 5 and 11.

No inconsistencies found.
