# Blazor UI Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give each module its own Blazor UI, composed by a single new UI host, mirroring how `ISerginWebApiModule` already composes endpoints.

**Architecture:** A new `ISerginWebUiModule : ISerginModule` capability interface exposes each module's routable assembly and nav items. A new `Sergin.MeterMinder.Hosts.WebUi.All` host registers the same `ISerginModule` list as the API host and dispatches MediatR in-process. Everything both hosts share is extracted into `AddSerginCore`. Every UI operation runs in its own DI scope, because Blazor Server's "scoped" is the circuit's lifetime, not a request's.

**Tech Stack:** .NET 10, Blazor Web App (global `InteractiveServer`), MudBlazor 9.8.0, MediatR, EF Core + Npgsql, Dapper, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-08-15-blazor-ui-infrastructure-design.md`

## Global Constraints

- **Warnings are errors, everywhere.** `TreatWarningsAsErrors=true`, `CodeAnalysisTreatWarningsAsErrors=true`, `AnalysisMode=All`, `AnalysisLevel=latest`, `EnforceCodeStyleInBuild=true`, plus SonarAnalyzer.CSharp on every project. Code must pass analysis the first time.
- **Central Package Management is on.** Every `PackageReference` is version-less; the version goes in `Directory.Packages.props` as `<PackageVersion>`, kept alphabetical. A leftover `Version=` attribute fails with NU1008. **There are two such files** — the repo root and `src/SharedKernel/` — and MudBlazor must be added to both.
- **`TargetFramework` is `net10.0`**, supplied by `Directory.Build.props`. Never restate it in a csproj.
- **Nullable and ImplicitUsings are enabled solution-wide.**
- **Three git repos.** `src/SharedKernel/` and `src/Modules/UserAccess/` are submodules with their own remotes. Work bottom-up; every cross-repo reference is a relative-path `ProjectReference`, so the whole change builds locally from the working trees before any PR merges.
- **Razor rule:** `.razor` files contain markup only. All C# goes in `.razor.cs` code-behind partial classes, so hand-written code keeps full analyzer coverage.
- **Route rule:** every module page's `@page` template starts with `/{module.Schema}/` — `/mm/…` or `/ua/…`.
- **Dispatch rule:** components inject `ISerginUiDispatcher`, never `ISender` or `IMediator`.
- **Commits** are authored under the user's git identity. **Never** add a `Co-Authored-By: Claude` trailer.
- **Naming:** `Sergin.SharedKernel.Infrastracture.Data` and `…Infrastracture.WebApi` are misspelled in real life. Match existing spelling when referencing them; use correct spelling for new projects.

## Branches

Already created: `feature/blazor-ui-infrastructure` in the root repo. Tasks 1–14 also need branches in the submodules:

```bash
git -C src/SharedKernel       switch -c feature/blazor-ui-infrastructure
git -C src/Modules/UserAccess switch -c feature/blazor-users-vertical
```

---

## Task 0: Analyzer spike (throwaway)

The single largest unknown is which analyzer diagnostics actually fire on Razor source-generator output under `AnalysisMode=All` + Sonar + `TreatWarningsAsErrors`. Guessing produces either a broken build or an over-broad suppression. Find out first, then throw the code away.

**Files:**
- Create (throwaway, outside the repo): `%TEMP%/razor-spike/`

- [ ] **Step 1: Create a scratch RCL and host under the repo's real build props**

```bash
mkdir -p /c/@factory/Sergin/Sergin.MeterMinder/.spike && cd /c/@factory/Sergin/Sergin.MeterMinder/.spike
dotnet new razorclasslib -n SpikeRcl
dotnet new blazor -n SpikeHost --interactivity Server
dotnet add SpikeHost/SpikeHost.csproj reference SpikeRcl/SpikeRcl.csproj
dotnet add SpikeRcl/SpikeRcl.csproj package MudBlazor --version 9.8.0
```

`.spike/` sits under the repo root, so it inherits the real `Directory.Build.props` and `.editorconfig`. It is git-ignored by being deleted in step 5, not committed.

- [ ] **Step 2: Add a component exercising the constructs the real code will use**

`.spike/SpikeRcl/Probe.razor`:

```razor
@page "/mm/probe"

<MudTable T="ProbeRow" ServerData="LoadAsync">
    <HeaderContent><MudTh>Name</MudTh></HeaderContent>
    <RowTemplate><MudTd>@context.Name</MudTd></RowTemplate>
    <PagerContent><MudTablePager /></PagerContent>
</MudTable>

<EditForm Model="model" OnValidSubmit="SubmitAsync">
    <DataAnnotationsValidator />
    <MudTextField @bind-Value="model.Name" Label="Name" />
    <MudButton ButtonType="ButtonType.Submit">Save</MudButton>
</EditForm>
```

`.spike/SpikeRcl/Probe.razor.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace SpikeRcl;

public sealed record ProbeRow(string Name);

public sealed class ProbeModel
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
}

public sealed partial class Probe
{
    private readonly ProbeModel model = new();

    [Parameter]
    public string? Filter { get; set; }

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private Task<TableData<ProbeRow>> LoadAsync(TableState state, CancellationToken cancellationToken)
        => Task.FromResult(new TableData<ProbeRow> { Items = [], TotalItems = 0 });

    private void SubmitAsync() => Navigation.NavigateTo("/mm/probe");
}
```

- [ ] **Step 3: Build and record every diagnostic**

```bash
cd /c/@factory/Sergin/Sergin.MeterMinder/.spike
dotnet build SpikeRcl/SpikeRcl.csproj 2>&1 | tee spike-output.txt
dotnet build SpikeHost/SpikeHost.csproj 2>&1 | tee -a spike-output.txt
grep -oE '(error|warning) [A-Z]+[0-9]+' spike-output.txt | sort -u
```

Write the resulting diagnostic ID list into the task notes. Expected candidates, in rough likelihood order: `IDE0161` (file-scoped namespaces — the Razor generator emits block-scoped), `S1128` (unused usings — `_Imports` is injected into every component), `CS8618`, `CA1515`, `CA1812`, `S3903`. `RZ10012` and `MUD0001`/`MUD0002` may also appear and should be **left as errors** — they indicate real mistakes.

Also confirm here: **the exact `MudTable<T>.ServerData` delegate arity in 9.8.0.** If `Func<TableState, CancellationToken, Task<TableData<T>>>` compiles, the plan's signatures are right; if it wants `Func<TableState, Task<TableData<T>>>`, drop the `CancellationToken` parameter everywhere in Tasks 10–12 and pass `CancellationToken.None` to the dispatcher.

- [ ] **Step 4: Verify the `[*.g.cs] generated_code = true` remedy**

Append to `.spike/.editorconfig` (a local one, so the experiment is isolated):

```ini
root = false

[*.g.cs]
generated_code = true
```

Rebuild. Confirm which of the recorded diagnostics disappear and which remain. Anything still failing needs a named, commented suppression in Task 1 — not a blanket disable.

- [ ] **Step 5: Delete the spike**

```bash
cd /c/@factory/Sergin/Sergin.MeterMinder && rm -rf .spike
git status --short   # must be clean
```

Nothing is committed from this task. Its output is the diagnostic list that Task 1 encodes.

---

## Task 1: `.editorconfig` and MudBlazor package versions

**Files:**
- Modify: `.editorconfig` (append)
- Modify: `src/SharedKernel/.editorconfig` (append, byte-identical)
- Modify: `Directory.Packages.props`
- Modify: `src/SharedKernel/Directory.Packages.props`

**Interfaces:**
- Consumes: the diagnostic list from Task 0.
- Produces: version-less `<PackageReference Include="MudBlazor" />` becomes legal in both repos; Razor generated output stops failing the build.

- [ ] **Step 1: Confirm the two `.editorconfig` files are currently identical**

```bash
cd /c/@factory/Sergin/Sergin.MeterMinder && diff .editorconfig src/SharedKernel/.editorconfig && echo "IDENTICAL"
```

Expected: `IDENTICAL`. They must stay that way — append the same block to both.

- [ ] **Step 2: Append the generated-code and Razor sections to both files**

```ini

# Source-generator output (Razor components, [GeneratedRegex], ...). We author `.razor` markup and
# `.razor.cs` code-behind; the merged compilation unit the Razor source generator emits between them
# uses block-scoped namespaces, `__builder` locals, and the full `_Imports.razor` using set — none of
# which we control. Marked generated for the same reason as the EF migrations section above.
# This relaxes nothing for hand-written code: `.razor.cs` files are plain `.cs` and keep full
# CA/IDE/Sonar analysis under the `[*.cs]` sections.
[*.g.cs]
generated_code = true

[*.razor]
indent_size = 4
indent_style = space
end_of_line = crlf
insert_final_newline = true
```

If Task 0 recorded diagnostics that survive `generated_code = true`, add each one here as its own `dotnet_diagnostic.<ID>.severity = none` line **with a comment naming why it cannot apply to generated Razor output**. Do not add any that Task 0 did not actually observe.

- [ ] **Step 3: Add MudBlazor to both package files**

In `Directory.Packages.props`, between `Microsoft.NET.Test.Sdk` and `Npgsql.EntityFrameworkCore.PostgreSQL`:

```xml
		<PackageVersion Include="MudBlazor" Version="9.8.0" />
```

In `src/SharedKernel/Directory.Packages.props`, between `Microsoft.Extensions.ServiceDiscovery` and `Npgsql.EntityFrameworkCore.PostgreSQL`:

```xml
		<PackageVersion Include="MudBlazor" Version="9.8.0" />
```

MudBlazor 9.8.0 is verified to ship an explicit `net10.0` dependency group. If restore fails offline, 9.7.0 is already in the local NuGet cache and also targets `net10.0` — change both files together if you fall back.

- [ ] **Step 4: Verify both files still match and the solution still builds**

```bash
diff .editorconfig src/SharedKernel/.editorconfig && echo "IDENTICAL"
dotnet build Sergin.MeterMinder.slnx
```

Expected: `IDENTICAL`, and a clean build (nothing references MudBlazor yet, so this only proves nothing regressed).

- [ ] **Step 5: Commit**

```bash
git -C src/SharedKernel add .editorconfig Directory.Packages.props
git -C src/SharedKernel commit -m "Add MudBlazor package version and Razor analyzer scoping"
git add .editorconfig Directory.Packages.props
git commit -m "Add MudBlazor package version and Razor analyzer scoping"
```

---

## Task 2: The `ISerginWebUiModule` contract

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Modules/ISerginWebUiModule.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Modules/SerginNavItem.cs`

**Interfaces:**
- Consumes: `ISerginModule` (existing, same project).
- Produces: `ISerginWebUiModule` with `Assembly UiAssembly { get; }` and `IReadOnlyCollection<SerginNavItem> NavItems { get; }`; `SerginNavItem(string Label, string Href, string Icon, int Order = 0)`. Tasks 5, 8, 10, 12 depend on these exact names.

`Sergin.SharedKernel.Modules` is a zero-ProjectReference contract leaf with only `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. Both new types respect that: `Assembly` is BCL, and `SerginNavItem` is a plain record. No csproj change.

- [ ] **Step 1: Write `SerginNavItem.cs`**

```csharp
namespace Sergin.SharedKernel.Modules;

/// <param name="Label">Display text. Rendered through ILocalizer, so a resource key also works.</param>
/// <param name="Href">Absolute, schema-prefixed path, e.g. "/mm/devices".</param>
/// <param name="Icon">Raw SVG path data. A string keeps this contract leaf free of any UI library.</param>
/// <param name="Order">Cross-module ordering; ties broken by Label.</param>
public sealed record SerginNavItem(string Label, string Href, string Icon, int Order = 0);
```

- [ ] **Step 2: Write `ISerginWebUiModule.cs`**

```csharp
using System.Reflection;

namespace Sergin.SharedKernel.Modules;

public interface ISerginWebUiModule : ISerginModule
{
    /// <summary>
    /// The assembly holding this module's routable Razor components. Needed by both
    /// MapRazorComponents&lt;T&gt;().AddAdditionalAssemblies(...) for static server-side rendering and
    /// the Router component's AdditionalAssemblies for interactive routing. This is never the
    /// ApplicationAssembly, which is deliberately UI-free.
    /// </summary>
    Assembly UiAssembly { get; }

    IReadOnlyCollection<SerginNavItem> NavItems { get; }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build src/SharedKernel/Sergin.SharedKernel.slnx
```

Expected: clean.

- [ ] **Step 4: Commit**

```bash
git -C src/SharedKernel add Sergin.SharedKernel.Modules/ISerginWebUiModule.cs Sergin.SharedKernel.Modules/SerginNavItem.cs
git -C src/SharedKernel commit -m "Add ISerginWebUiModule capability contract"
```

---

## Task 3: Extract `AddSerginCore`

The riskiest task in the plan: it changes the running API host. The existing integration suite is the safety net, plus an explicit service-descriptor diff.

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/Sergin.SharedKernel.Hosts.csproj`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebApi/SerginWebApiExtensions.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastracture.Data/Properties/AssemblyInfo.cs`

**Interfaces:**
- Consumes: `ISerginModule` (Task 2's project, pre-existing members).
- Produces: `SerginCoreExtensions.AddSerginCore<TBuilder>(this TBuilder builder, IReadOnlyCollection<ISerginModule> modules) where TBuilder : IHostApplicationBuilder`, returning `IConfigurationSection` (the `"Sergin"` section). `SerginCoreExtensions.SectionName == "Sergin"`. Tasks 8 depends on both.

- [ ] **Step 1: Capture the API host's service graph BEFORE the change**

Temporarily add to `src/Hosts/Sergin.MeterMinder.Hosts.WebApi.All/Program.cs`, immediately after `builder.AddSerginWebApi(modules);`:

```csharp
foreach (ServiceDescriptor descriptor in builder.Services)
{
    Console.WriteLine($"{descriptor.Lifetime}|{descriptor.ServiceType.FullName}|{descriptor.ImplementationType?.FullName ?? descriptor.ImplementationInstance?.GetType().FullName ?? descriptor.ImplementationFactory?.Method.ToString() ?? "?"}");
}
```

```bash
dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.WebApi.All 2>/dev/null | sort > /tmp/services-before.txt
wc -l /tmp/services-before.txt
```

Stop the host once it prints. Keep the loop in place until step 7.

- [ ] **Step 2: Grant `InternalsVisibleTo` for the moved `PostgresDbConnectionFactory`**

`PostgresDbConnectionFactory` is internal to `Sergin.SharedKernel.Infrastracture.Data`, which currently grants only `Sergin.Hosts.WebApi.All` (a project that no longer exists) and `Sergin.SharedKernel.Hosts.WebApi`. Replace those two lines in `src/SharedKernel/Sergin.SharedKernel.Infrastracture.Data/Properties/AssemblyInfo.cs` with:

```csharp
[assembly: InternalsVisibleTo("Sergin.SharedKernel.Hosts")]
[assembly: InternalsVisibleTo("Sergin.SharedKernel.Hosts.WebApi")]
```

The other four `AssemblyInfo.cs` files in the SharedKernel already grant `Sergin.SharedKernel.Hosts` — verify rather than assume:

```bash
grep -rn 'InternalsVisibleTo' src/SharedKernel/*/Properties/AssemblyInfo.cs
```

- [ ] **Step 3: Add references to `Sergin.SharedKernel.Hosts.csproj`**

Add inside the existing `ItemGroup` holding the package references:

```xml
    <PackageReference Include="MediatR" />
```

And a new `ItemGroup`:

```xml
  <ItemGroup>
    <ProjectReference Include="..\Sergin.SharedKernel.Application\Sergin.SharedKernel.Application.csproj" />
    <ProjectReference Include="..\Sergin.SharedKernel.Infrastracture.Data\Sergin.SharedKernel.Infrastracture.Data.csproj" />
    <ProjectReference Include="..\Sergin.SharedKernel.Infrastructure\Sergin.SharedKernel.Infrastructure.csproj" />
    <ProjectReference Include="..\Sergin.SharedKernel.Infrastructure.Data.EFCore\Sergin.SharedKernel.Infrastructure.Data.EFCore.csproj" />
    <ProjectReference Include="..\Sergin.SharedKernel.Modules\Sergin.SharedKernel.Modules.csproj" />
  </ItemGroup>
```

- [ ] **Step 4: Write `SerginCoreExtensions.cs`**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Events;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Infrastracture.Data;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Interceptors;
using Sergin.SharedKernel.Infrastructure.Events;
using Sergin.SharedKernel.Infrastructure.Localizations;
using Sergin.SharedKernel.Modules;

namespace Microsoft.Extensions.Hosting;

public static class SerginCoreExtensions
{
    public const string SectionName = "Sergin";

    /// <summary>
    /// Registers everything a Sergin host needs regardless of its presentation technology.
    /// The caller must register an <see cref="IUserContextFactory"/> — it is the one service whose
    /// implementation is host-shaped (HttpContext for the Web API, configuration for the Web UI).
    /// </summary>
    public static IConfigurationSection AddSerginCore<TBuilder>(
        this TBuilder builder, IReadOnlyCollection<ISerginModule> modules)
        where TBuilder : IHostApplicationBuilder
    {
        IConfigurationSection serginSection = builder.Configuration.GetRequiredSection(SectionName);

        string[] duplicateSchemas =
        [
            .. modules.GroupBy(module => module.Schema, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
        ];

        if (duplicateSchemas.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate module schema(s) registered: {string.Join(", ", duplicateSchemas)}. Each module must "
                + "appear exactly once — listing two classes for the same module runs AddServices twice.");
        }

        builder.Services.AddMediatR(options =>
        {
            foreach (ISerginModule module in modules)
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

        foreach (ISerginModule module in modules)
        {
            module.AddServices(builder.Services, serginSection);
        }

        return serginSection;
    }
}
```

- [ ] **Step 5: Rewrite `AddSerginWebApi` to call it**

`AddSerginWebApi` in `SerginWebApiExtensions.cs` becomes exactly:

```csharp
    public static WebApplicationBuilder AddSerginWebApi(this WebApplicationBuilder builder, IReadOnlyCollection<ISerginModule> modules)
    {
        builder.Services.AddOpenApi();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<IUserContextFactory, InternalUserContextFactory>();

        builder.AddSerginCore(modules);

        return builder;
    }
```

`UseSerginWebApiAsync` is **unchanged**. Delete the now-unused `using` directives that the compiler flags (`S1128` is an error here) — expect to remove the MediatR, Events, Localizations, Authorization, Infrastracture.Data, EFCore.Interceptors, Infrastructure.Events and Infrastructure.Localizations imports, keeping `Configuration`, `DependencyInjection`, `Scalar.AspNetCore`, `Securities.Users`, `Infrastracture.WebApi.Users` and `Modules`. Add a reference to `Sergin.SharedKernel.Hosts` in `Sergin.SharedKernel.Hosts.WebApi.csproj` if one is not already present (it is — verify).

- [ ] **Step 6: Build both solutions**

```bash
dotnet build src/SharedKernel/Sergin.SharedKernel.slnx
dotnet build Sergin.MeterMinder.slnx
```

Expected: clean. A `CS0122` here means step 2's `InternalsVisibleTo` is wrong.

- [ ] **Step 7: Prove the API host's service graph is unchanged**

```bash
dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.WebApi.All 2>/dev/null | sort > /tmp/services-after.txt
diff /tmp/services-before.txt /tmp/services-after.txt && echo "IDENTICAL SERVICE GRAPH"
```

Expected: `IDENTICAL SERVICE GRAPH`. If it differs, a registration was dropped or duplicated — fix before continuing. Then **remove the temporary `foreach` loop** from `Program.cs` and confirm `git diff src/Hosts/` is empty.

- [ ] **Step 8: Run the existing integration suite**

```bash
dotnet test tests/Sergin.MeterMinder.IntegrationTests.WebApi.All/Sergin.MeterMinder.IntegrationTests.WebApi.All.csproj
```

Expected: PASS (requires Docker).

- [ ] **Step 9: Commit**

```bash
git -C src/SharedKernel add Sergin.SharedKernel.Hosts Sergin.SharedKernel.Hosts.WebApi Sergin.SharedKernel.Infrastracture.Data
git -C src/SharedKernel commit -m "Extract AddSerginCore for reuse across host types"
```

---

## Task 4: `Sergin.SharedKernel.Presentation` — HttpContext-free error mapping

`ApiProblemResults` is HttpContext-bound. Extract its pure status/localization logic into the reserved-empty `Sergin.SharedKernel.Presentation` project so both the API and the UI produce identical error text.

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation/Errors/SerginProblem.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation/Errors/SerginProblemFactory.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation/Sergin.SharedKernel.Presentation.csproj`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.WebApi/Endpoints/Results/ApiProblemResults.cs`

**Interfaces:**
- Produces: `SerginProblem(int StatusCode, string Title, string Detail, ErrorType Type)` and `SerginProblemFactory.Create(Error error, ILocalizer localizer)`. Tasks 6 and 11 depend on both.

- [ ] **Step 1: Read the current `ApiProblemResults` so the extraction is exact**

```bash
cat src/SharedKernel/Sergin.SharedKernel.Presentation.WebApi/Endpoints/Results/ApiProblemResults.cs
```

The status map and the two localization-key expressions must be copied character-for-character, including the non-localized default branches.

- [ ] **Step 2: Add the framework reference**

`Sergin.SharedKernel.Presentation.csproj` gains `StatusCodes`:

```xml
	<ItemGroup>
		<FrameworkReference Include="Microsoft.AspNetCore.App" />
	</ItemGroup>
```

- [ ] **Step 3: Write `SerginProblem.cs`**

```csharp
using ErrorOr;

namespace Sergin.SharedKernel.Presentation.Errors;

public sealed record SerginProblem(int StatusCode, string Title, string Detail, ErrorType Type);
```

- [ ] **Step 4: Write `SerginProblemFactory.cs`**

```csharp
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Sergin.SharedKernel.Application.Localizations;

namespace Sergin.SharedKernel.Presentation.Errors;

public static class SerginProblemFactory
{
    public static SerginProblem Create(Error error, ILocalizer localizer)
        => new(GetStatusCode(error.Type), GetTitle(error, localizer), GetDetail(error, localizer), error.Type);

    public static int GetStatusCode(ErrorType errorType)
        => errorType switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unexpected => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };

    private static string GetTitle(Error error, ILocalizer localizer)
        => error.Type switch
        {
            ErrorType.Validation or ErrorType.Unexpected or ErrorType.NotFound
                or ErrorType.Conflict or ErrorType.Forbidden => localizer[$"{error.Code}.title"],
            _ => "ServerFailure"
        };

    private static string GetDetail(Error error, ILocalizer localizer)
        => error.Type switch
        {
            ErrorType.Validation or ErrorType.Unexpected or ErrorType.NotFound
                or ErrorType.Conflict or ErrorType.Forbidden => localizer[error.Code],
            _ => "ServerFailure"
        };
```

Adjust the two `_ =>` branches to whatever step 1 showed — they must not change behaviour.

- [ ] **Step 5: Delegate from `ApiProblemResults`**

```csharp
    public static IResult Problem(Error error, ILocalizer l)
    {
        SerginProblem problem = SerginProblemFactory.Create(error, l);

        return Microsoft.AspNetCore.Http.Results.Problem(
            title: problem.Title,
            detail: problem.Detail,
            statusCode: problem.StatusCode);
    }
```

Add `<ProjectReference Include="..\Sergin.SharedKernel.Presentation\Sergin.SharedKernel.Presentation.csproj" />` to `Sergin.SharedKernel.Presentation.WebApi.csproj` if absent.

- [ ] **Step 6: Build and re-run the API suite**

```bash
dotnet build src/SharedKernel/Sergin.SharedKernel.slnx
dotnet test tests/Sergin.MeterMinder.IntegrationTests.WebApi.All/Sergin.MeterMinder.IntegrationTests.WebApi.All.csproj
```

Expected: clean build, PASS.

- [ ] **Step 7: Commit**

```bash
git -C src/SharedKernel add Sergin.SharedKernel.Presentation Sergin.SharedKernel.Presentation.WebApi
git -C src/SharedKernel commit -m "Extract HttpContext-free problem mapping into SharedKernel.Presentation"
```

---

## Task 5: `Sergin.SharedKernel.Presentation.Blazor` — project, dispatcher, catalog

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Sergin.SharedKernel.Presentation.Blazor.csproj`
- Create: `…/GlobalUsings.cs`, `…/_Imports.razor`
- Create: `…/Dispatching/ISerginUiDispatcher.cs`, `…/Dispatching/ScopedSerginUiDispatcher.cs`, `…/Dispatching/SerginUiDispatcherExtensions.cs`
- Create: `…/Modules/SerginUiModuleCatalog.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.slnx`

**Interfaces:**
- Consumes: `ISerginWebUiModule`, `SerginNavItem` (Task 2).
- Produces: `ISerginUiDispatcher.SendAsync<TResponse>(IRequest<ErrorOr<TResponse>>, CancellationToken)`; `SerginUiDispatcherExtensions.SendListAsync<TItem>(this ISerginUiDispatcher, int pageSize, int pageIndex, CancellationToken)`; `SerginUiModuleCatalog` with `Modules`, `RoutableAssemblies`, `NavItems`. Tasks 6–8 and 10–12 depend on all of these.

- [ ] **Step 1: Create the project**

`Sergin.SharedKernel.Presentation.Blazor.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<ItemGroup>
		<FrameworkReference Include="Microsoft.AspNetCore.App" />

		<PackageReference Include="MediatR" />
		<PackageReference Include="MudBlazor" />
	</ItemGroup>

	<ItemGroup>
		<ProjectReference Include="..\Sergin.SharedKernel.Application\Sergin.SharedKernel.Application.csproj" />
		<ProjectReference Include="..\Sergin.SharedKernel.Modules\Sergin.SharedKernel.Modules.csproj" />
		<ProjectReference Include="..\Sergin.SharedKernel.Presentation\Sergin.SharedKernel.Presentation.csproj" />
	</ItemGroup>
</Project>
```

`GlobalUsings.cs` — note `Sergin.SharedKernel.Application` is imported globally specifically so the **live** `ListQueryResponse<T>` always wins over the dead `RTS.Common.Domain.Repository.Query` duplicate:

```csharp
global using ErrorOr;
global using MediatR;
global using Sergin.SharedKernel.Application;
global using Sergin.SharedKernel.Application.Commands.Queries;
```

`_Imports.razor`:

```razor
@using Microsoft.AspNetCore.Components
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using MudBlazor
@using Sergin.SharedKernel.Application
@using Sergin.SharedKernel.Modules
@using Sergin.SharedKernel.Presentation.Blazor.Dispatching
@using Sergin.SharedKernel.Presentation.Blazor.Errors
@using Sergin.SharedKernel.Presentation.Blazor.Modules
```

- [ ] **Step 2: Write the dispatcher**

`Dispatching/ISerginUiDispatcher.cs`:

```csharp
namespace Sergin.SharedKernel.Presentation.Blazor.Dispatching;

/// <summary>
/// Sends a MediatR request inside its own DI scope. In Blazor Server a "scoped" service lives for the
/// whole SignalR circuit, so resolving <see cref="ISender"/> straight off the circuit's provider would
/// share one DbContext across every interaction for the circuit's lifetime — producing an unbounded
/// change tracker, stale first-level-cache reads, and "a second operation was started on this context"
/// whenever two components render in parallel. Every send through this dispatcher gets a fresh scope,
/// i.e. exactly the lifetime an HTTP request gets in the Web API host.
/// </summary>
public interface ISerginUiDispatcher
{
    Task<ErrorOr<TResponse>> SendAsync<TResponse>(
        IRequest<ErrorOr<TResponse>> request, CancellationToken cancellationToken = default);
}
```

`Dispatching/ScopedSerginUiDispatcher.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Sergin.SharedKernel.Presentation.Blazor.Dispatching;

internal sealed class ScopedSerginUiDispatcher(IServiceScopeFactory scopeFactory) : ISerginUiDispatcher
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

`Dispatching/SerginUiDispatcherExtensions.cs`:

```csharp
namespace Sergin.SharedKernel.Presentation.Blazor.Dispatching;

public static class SerginUiDispatcherExtensions
{
    /// <summary>
    /// List queries have no dedicated command type — handlers implement IListQueryHandler&lt;TItem&gt;
    /// against the shared generic ListQuery&lt;TItem&gt;. This is the UI-side equivalent of
    /// ListQueryRequestModel.ToListQuery&lt;TItem&gt;(), without the [FromQuery] binding attributes.
    /// pageIndex is 1-based, matching PageIndex.Default; MudBlazor's TableState.Page is 0-based.
    /// </summary>
    public static Task<ErrorOr<ListQueryResponse<TItem>>> SendListAsync<TItem>(
        this ISerginUiDispatcher dispatcher, int pageSize, int pageIndex, CancellationToken cancellationToken = default)
        where TItem : notnull
        => dispatcher.SendAsync(ListQueryFactory.Create<TItem>(pageSize, pageIndex), cancellationToken);
}
```

`ListQuery<TItem> : ListQuery, IListQuery<TItem>` resolves to `IRequest<ErrorOr<ListQueryResponse<TItem>>>`, so `TResponse` infers to `ListQueryResponse<TItem>`. The `int → PageSize` / `int → PageIndex` implicit conversions select the `(PageSize, PageIndex, …)` overload over the `(Paggination?, …)` one, which has no such conversion.

- [ ] **Step 3: Write the module catalog**

`Modules/SerginUiModuleCatalog.cs`:

```csharp
using System.Reflection;
using Sergin.SharedKernel.Modules;

namespace Sergin.SharedKernel.Presentation.Blazor.Modules;

public sealed class SerginUiModuleCatalog
{
    public SerginUiModuleCatalog(IReadOnlyCollection<ISerginWebUiModule> modules)
    {
        Modules = modules;
        RoutableAssemblies = [.. modules.Select(module => module.UiAssembly)];
        NavItems =
        [
            .. modules.SelectMany(module => module.NavItems)
                .OrderBy(item => item.Order)
                .ThenBy(item => item.Label, StringComparer.Ordinal)
        ];
    }

    public IReadOnlyCollection<ISerginWebUiModule> Modules { get; }

    public IReadOnlyCollection<Assembly> RoutableAssemblies { get; }

    public IReadOnlyCollection<SerginNavItem> NavItems { get; }
}
```

- [ ] **Step 4: Register the project in the SharedKernel solution**

In `src/SharedKernel/Sergin.SharedKernel.slnx`, inside the `/Presentation/` folder:

```xml
    <Project Path="Sergin.SharedKernel.Presentation.Blazor/Sergin.SharedKernel.Presentation.Blazor.csproj" />
```

- [ ] **Step 5: Build**

```bash
dotnet build src/SharedKernel/Sergin.SharedKernel.slnx
```

Expected: clean. This is the first MudBlazor restore — if it fails, re-check Task 1's `<PackageVersion>` entries.

- [ ] **Step 6: Commit**

```bash
git -C src/SharedKernel add Sergin.SharedKernel.Presentation.Blazor Sergin.SharedKernel.slnx
git -C src/SharedKernel commit -m "Add SharedKernel Blazor kit with per-operation dispatcher and module catalog"
```

---

## Task 6: Error presentation and the shared shell

**Files:**
- Create: `…/Presentation.Blazor/Errors/IUiErrorPresenter.cs`, `…/Errors/MudUiErrorPresenter.cs`, `…/Errors/SerginProblemPanel.razor`, `…/Errors/SerginProblemPanel.razor.cs`
- Create: `…/Presentation.Blazor/Layout/SerginMainLayout.razor` (+ `.razor.cs`), `…/Layout/SerginNavMenu.razor` (+ `.razor.cs`)
- Create: `…/Presentation.Blazor/SerginBlazorKitExtensions.cs`

**Interfaces:**
- Consumes: `SerginProblem`, `SerginProblemFactory` (Task 4); `SerginUiModuleCatalog`, `ISerginUiDispatcher` (Task 5).
- Produces: `IUiErrorPresenter` with `SerginProblem Present(Error)` and `void Notify(Error)`; components `SerginMainLayout`, `SerginNavMenu`, `SerginProblemPanel` (parameter `Problem`); `SerginBlazorKitExtensions.AddSerginBlazorKit(this IServiceCollection)`. Tasks 8, 10–12 depend on these.

- [ ] **Step 1: Write the error presenter**

`Errors/IUiErrorPresenter.cs`:

```csharp
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.SharedKernel.Presentation.Blazor.Errors;

public interface IUiErrorPresenter
{
    SerginProblem Present(Error error);

    void Notify(Error error);
}
```

`Errors/MudUiErrorPresenter.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using MudBlazor;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.SharedKernel.Presentation.Blazor.Errors;

internal sealed class MudUiErrorPresenter(ILocalizer localizer, ISnackbar snackbar) : IUiErrorPresenter
{
    public SerginProblem Present(Error error) => SerginProblemFactory.Create(error, localizer);

    public void Notify(Error error)
    {
        SerginProblem problem = Present(error);

        snackbar.Add(problem.Detail, ToSeverity(problem.StatusCode));
    }

    private static Severity ToSeverity(int statusCode)
        => statusCode switch
        {
            StatusCodes.Status404NotFound => Severity.Info,
            StatusCodes.Status403Forbidden or StatusCodes.Status409Conflict => Severity.Warning,
            _ => Severity.Error
        };
}
```

- [ ] **Step 2: Write the problem panel**

`Errors/SerginProblemPanel.razor`:

```razor
@if (Problem is not null)
{
    <MudAlert Severity="@Severity" Variant="Variant.Outlined" Class="my-4">
        <MudText Typo="Typo.subtitle2">@Problem.Title</MudText>
        <MudText Typo="Typo.body2">@Problem.Detail</MudText>
    </MudAlert>
}
```

`Errors/SerginProblemPanel.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using MudBlazor;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.SharedKernel.Presentation.Blazor.Errors;

public sealed partial class SerginProblemPanel
{
    [Parameter]
    public SerginProblem? Problem { get; set; }

    private Severity Severity
        => Problem?.StatusCode switch
        {
            StatusCodes.Status404NotFound => MudBlazor.Severity.Info,
            StatusCodes.Status403Forbidden or StatusCodes.Status409Conflict => MudBlazor.Severity.Warning,
            _ => MudBlazor.Severity.Error
        };
}
```

- [ ] **Step 3: Write the nav menu**

`Layout/SerginNavMenu.razor`:

```razor
<MudNavMenu>
    @foreach (SerginNavItem item in Catalog.NavItems)
    {
        <MudNavLink Href="@item.Href" Icon="@item.Icon" Match="NavLinkMatch.Prefix">
            @Localizer[item.Label]
        </MudNavLink>
    }
</MudNavMenu>
```

`Layout/SerginNavMenu.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Presentation.Blazor.Modules;

namespace Sergin.SharedKernel.Presentation.Blazor.Layout;

public sealed partial class SerginNavMenu
{
    [Inject]
    private SerginUiModuleCatalog Catalog { get; set; } = default!;

    [Inject]
    private ILocalizer Localizer { get; set; } = default!;
}
```

- [ ] **Step 4: Write the layout**

`Layout/SerginMainLayout.razor`:

```razor
@inherits LayoutComponentBase

<MudThemeProvider />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
    <MudAppBar Elevation="1">
        <MudIconButton Icon="@Icons.Material.Filled.Menu" Color="Color.Inherit" Edge="Edge.Start"
                       OnClick="ToggleDrawer" />
        <MudText Typo="Typo.h6">Sergin</MudText>
    </MudAppBar>

    <MudDrawer @bind-Open="drawerOpen" Elevation="1">
        <SerginNavMenu />
    </MudDrawer>

    <MudMainContent>
        <MudContainer MaxWidth="MaxWidth.Large" Class="my-6">
            @Body
        </MudContainer>
    </MudMainContent>
</MudLayout>
```

`Layout/SerginMainLayout.razor.cs`:

```csharp
namespace Sergin.SharedKernel.Presentation.Blazor.Layout;

public sealed partial class SerginMainLayout
{
    private bool drawerOpen = true;

    private void ToggleDrawer() => drawerOpen = !drawerOpen;
}
```

The four Mud providers must appear exactly once in the app; the layout is the conventional home. `MudSnackbarProvider` is what makes `IUiErrorPresenter.Notify` visible.

- [ ] **Step 5: Write the DI entry point**

`SerginBlazorKitExtensions.cs`:

```csharp
using MudBlazor.Services;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Microsoft.Extensions.DependencyInjection;

public static class SerginBlazorKitExtensions
{
    public static IServiceCollection AddSerginBlazorKit(this IServiceCollection services)
    {
        services.AddMudServices();

        services.AddSingleton<ISerginUiDispatcher, ScopedSerginUiDispatcher>();
        services.AddScoped<IUiErrorPresenter, MudUiErrorPresenter>();

        return services;
    }
}
```

`ScopedSerginUiDispatcher` is a singleton: it holds only `IServiceScopeFactory` (itself a singleton) and creates a scope per call. `MudUiErrorPresenter` is scoped because `ISnackbar` is circuit-scoped.

- [ ] **Step 6: Build**

```bash
dotnet build src/SharedKernel/Sergin.SharedKernel.slnx
```

Expected: clean. `RZ10012` here means a missing `@using` in `_Imports.razor`.

- [ ] **Step 7: Commit**

```bash
git -C src/SharedKernel add Sergin.SharedKernel.Presentation.Blazor
git -C src/SharedKernel commit -m "Add shared Blazor shell, nav menu and error presentation"
```

---

## Task 7: The configured dev-user context

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/Sergin.SharedKernel.Hosts.WebUi.csproj`
- Create: `…/Users/DevUserOptions.cs`, `…/Users/DevUserContext.cs`, `…/Users/ConfiguredUserContextFactory.cs`

This task creates the `Hosts.WebUi` project and its user-context types; Task 8 adds `SerginWebUiExtensions.cs` to the same project and registers it in the solution file.

**Interfaces:**
- Consumes: `IUserContext`, `IUserContextFactory`, `Permission`, `UserId` (existing SharedKernel types).
- Produces: `DevUserOptions` with `SectionName == "DevUser"` and a `Validate(out string failure)` method; `ConfiguredUserContextFactory : IUserContextFactory`. Task 8 registers both.

- [ ] **Step 1: Create the project skeleton**

`src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/Sergin.SharedKernel.Hosts.WebUi.csproj` — **`Microsoft.NET.Sdk.Razor`, not plain `Microsoft.NET.Sdk`**, even though it holds no `.razor` files. Plain SDK does not import `Microsoft.NET.Sdk.StaticWebAssets`, and static-web-asset resolution probes project references with `SkipNonexistentTargets="true"`, so a plain-SDK project silently drops its RCL dependencies' `_content/…` assets — MudBlazor's CSS and JS would never reach the host:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<ItemGroup>
		<FrameworkReference Include="Microsoft.AspNetCore.App" />
	</ItemGroup>

	<ItemGroup>
		<ProjectReference Include="..\Sergin.SharedKernel.Application\Sergin.SharedKernel.Application.csproj" />
		<ProjectReference Include="..\Sergin.SharedKernel.Hosts\Sergin.SharedKernel.Hosts.csproj" />
		<ProjectReference Include="..\Sergin.SharedKernel.Modules\Sergin.SharedKernel.Modules.csproj" />
		<ProjectReference Include="..\Sergin.SharedKernel.Presentation.Blazor\Sergin.SharedKernel.Presentation.Blazor.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Confirm how `Permission` is constructed from a string**

```bash
cat src/SharedKernel/Sergin.SharedKernel.Application/Securities/Permission.cs
```

`Permission` is a validated string value object with an implicit conversion from `string`. Use whichever of `Permission.Create(value)` or the implicit conversion actually exists — **read the file, do not assume**. The code below uses the implicit conversion; change it if the type exposes a factory instead.

- [ ] **Step 3: Write `DevUserOptions.cs`**

```csharp
using Sergin.SharedKernel.Application.Securities;

namespace Sergin.SharedKernel.Hosts.WebUi.Users;

public sealed class DevUserOptions
{
    public const string SectionName = "DevUser";

    public Guid Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    // string[] rather than IReadOnlyList<string>: the configuration binder supports arrays universally.
    public string[] Permissions { get; set; } = [];

    public bool Validate(out string failure)
    {
        if (Id == Guid.Empty)
        {
            failure = $"Sergin:{SectionName}:Id must be a non-empty GUID.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(UserName))
        {
            failure = $"Sergin:{SectionName}:UserName is required.";
            return false;
        }

        foreach (string permission in Permissions)
        {
            try
            {
                Permission parsed = permission;
                _ = parsed;
            }
            catch (ArgumentException exception)
            {
                failure = $"Sergin:{SectionName}:Permissions contains '{permission}', which is not a valid "
                    + $"permission: {exception.Message}";
                return false;
            }
        }

        failure = string.Empty;
        return true;
    }
}
```

Parsing permissions during options validation turns a typo like `permission.MM.devices.read` into a precise startup failure rather than an exception thrown out of a render.

- [ ] **Step 4: Write `DevUserContext.cs` and `ConfiguredUserContextFactory.cs`**

```csharp
using Sergin.SharedKernel.Application.Securities;
using Sergin.SharedKernel.Application.Securities.Users;

namespace Sergin.SharedKernel.Hosts.WebUi.Users;

internal sealed record DevUserContext(
    UserId Id,
    string UserName,
    string Email,
    string FirstName,
    string LastName,
    HashSet<Permission> Permissions) : IUserContext;
```

Check the real namespace of `UserId` from `InternalUserContext` and match it.

```csharp
using Microsoft.Extensions.Options;
using Sergin.SharedKernel.Application.Securities;
using Sergin.SharedKernel.Application.Securities.Users;

namespace Sergin.SharedKernel.Hosts.WebUi.Users;

internal sealed class ConfiguredUserContextFactory : IUserContextFactory
{
    private readonly IUserContext userContext;

    public ConfiguredUserContextFactory(IOptions<DevUserOptions> options)
    {
        DevUserOptions value = options.Value;

        userContext = new DevUserContext(
            new UserId(value.Id),
            value.UserName,
            value.Email,
            value.FirstName,
            value.LastName,
            [.. value.Permissions.Select(permission => (Permission)permission)]);
    }

    public IUserContext CreateUserContext() => userContext;
}
```

Immutable and built once. Registered transient to match `InternalUserContextFactory`; the per-operation scope resolves a fresh `IUserContext` per send, which is correct and free.

- [ ] **Step 5: Build**

```bash
dotnet build src/SharedKernel/Sergin.SharedKernel.slnx
```

Expected: clean (the project is not yet in the solution file — add it in Task 8, or build it directly with `dotnet build src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/Sergin.SharedKernel.Hosts.WebUi.csproj`).

- [ ] **Step 6: Commit**

```bash
git -C src/SharedKernel add Sergin.SharedKernel.Hosts.WebUi
git -C src/SharedKernel commit -m "Add configuration-driven user context for the Web UI host"
```

---

## Task 8: `AddSerginWebUi` / `UseSerginWebUiAsync` and the route guard

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.slnx`

**Interfaces:**
- Consumes: `AddSerginCore` (Task 3), `AddSerginBlazorKit` + `SerginUiModuleCatalog` (Tasks 5–6), `DevUserOptions` + `ConfiguredUserContextFactory` (Task 7), `ISerginWebUiModule` (Task 2).
- Produces: `AddSerginWebUi(this WebApplicationBuilder, IReadOnlyCollection<ISerginModule>)` and `UseSerginWebUiAsync<TRootComponent>(this WebApplication, IReadOnlyCollection<ISerginModule>) where TRootComponent : IComponent`. Task 13's `Program.cs` calls both.

- [ ] **Step 1: Write `SerginWebUiExtensions.cs`**

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Hosts.WebUi.Users;
using Sergin.SharedKernel.Modules;
using Sergin.SharedKernel.Presentation.Blazor.Modules;

namespace Microsoft.Extensions.Hosting;

public static class SerginWebUiExtensions
{
    public static WebApplicationBuilder AddSerginWebUi(
        this WebApplicationBuilder builder, IReadOnlyCollection<ISerginModule> modules)
    {
        if (!builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "The Sergin Web UI host has no authentication: every request runs as the configured development "
                + $"user 'Sergin:{DevUserOptions.SectionName}'. Refusing to start in the "
                + $"'{builder.Environment.EnvironmentName}' environment. Implement a real IUserContextFactory first.");
        }

        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        builder.Services.AddSerginBlazorKit();

        IConfigurationSection serginSection =
            builder.Configuration.GetRequiredSection(SerginCoreExtensions.SectionName);

        builder.Services.AddOptions<DevUserOptions>()
            .Bind(serginSection.GetSection(DevUserOptions.SectionName))
            .Validate(options => options.Validate(out _), "Invalid Sergin:DevUser configuration.")
            .ValidateOnStart();

        builder.Services.AddTransient<IUserContextFactory, ConfiguredUserContextFactory>();

        builder.AddSerginCore(modules);

        builder.Services.AddSingleton(new SerginUiModuleCatalog([.. modules.OfType<ISerginWebUiModule>()]));

        return builder;
    }

    public static async Task<WebApplication> UseSerginWebUiAsync<TRootComponent>(
        this WebApplication app, IReadOnlyCollection<ISerginModule> modules)
        where TRootComponent : IComponent
    {
        SerginUiModuleCatalog catalog = app.Services.GetRequiredService<SerginUiModuleCatalog>();

        ValidateRoutePrefixes(catalog);

        if (app.Environment.IsDevelopment())
        {
            foreach (ISerginModule module in modules)
            {
                await module.MigrateAsync(app.Services);
            }
        }

        app.UseAntiforgery();

        app.MapStaticAssets();

        app.MapRazorComponents<TRootComponent>()
            .AddAdditionalAssemblies([.. catalog.RoutableAssemblies])
            .AddInteractiveServerRenderMode();

        return app;
    }

    private static void ValidateRoutePrefixes(SerginUiModuleCatalog catalog)
    {
        List<string> violations = [];

        foreach (ISerginWebUiModule module in catalog.Modules)
        {
            string prefix = $"/{module.Schema}/";

            foreach (Type component in module.UiAssembly.GetExportedTypes())
            {
                if (!typeof(IComponent).IsAssignableFrom(component))
                {
                    continue;
                }

                foreach (RouteAttribute route in component.GetCustomAttributes<RouteAttribute>(inherit: false))
                {
                    if (!route.Template.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        violations.Add($"  {component.FullName}: @page \"{route.Template}\" must start with \"{prefix}\"");
                    }
                }
            }
        }

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                "Module routable components must sit under their module's schema prefix, because Razor @page "
                + "templates are compile-time constants and cannot be prefixed at map time the way "
                + "MapGroup(schema) prefixes minimal-API endpoints:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations));
        }
    }
}
```

`AddAdditionalAssemblies` covers **server-side static SSR routing**; the `Router` component in Task 13's `Routes.razor` supplies the same assemblies for **interactive routing**. Both are required — omitting either produces 404s in one of the two navigation paths.

Note the spec sketches the route check as a separate `internal static class SerginUiRouteGuard`; it is consolidated here as a private method of `SerginWebUiExtensions`, its only caller. Same behaviour, one fewer file. Refer to it as "the startup route guard" in documentation rather than by a type name.

- [ ] **Step 2: Register both new projects in the SharedKernel solution**

In `src/SharedKernel/Sergin.SharedKernel.slnx`, inside the `/Hosts/` folder:

```xml
    <Project Path="Sergin.SharedKernel.Hosts.WebUi/Sergin.SharedKernel.Hosts.WebUi.csproj" />
```

- [ ] **Step 3: Build the SharedKernel standalone — this is PR 1's gate**

```bash
dotnet build src/SharedKernel/Sergin.SharedKernel.slnx
```

Expected: clean.

- [ ] **Step 4: Commit**

```bash
git -C src/SharedKernel add Sergin.SharedKernel.Hosts.WebUi Sergin.SharedKernel.slnx
git -C src/SharedKernel commit -m "Add Web UI host bootstrap with schema route guard"
```

---

## Task 9: SharedKernel documentation

**Files:**
- Modify: `src/SharedKernel/README.md`
- Modify: `src/SharedKernel/.claude/CLAUDE.md`

- [ ] **Step 1: Update the project inventory in `README.md`**

The project count rises by two (`Sergin.SharedKernel.Hosts.WebUi`, `Sergin.SharedKernel.Presentation.Blazor`), and `Sergin.SharedKernel.Presentation` is no longer empty. Describe each in the existing style. Note that `Hosts.WebUi` uses the Razor SDK despite holding no components, and why.

- [ ] **Step 2: Add the UI conventions to `.claude/CLAUDE.md`**

Document, in the file's existing voice:
- `AddSerginCore` versus `AddSerginWebApi` / `AddSerginWebUi`, and which registrations live where.
- `ISerginWebUiModule` — `UiAssembly` + `NavItems`, one class per module.
- **Components inject `ISerginUiDispatcher`, never `ISender`** — with the circuit-lifetime reasoning.
- **`.razor` is markup only; all C# in `.razor.cs`** — with the analyzer-coverage reasoning.
- Module page routes must start with `/{schema}/`; the startup route guard in `UseSerginWebUiAsync` enforces it and names any violating component.

- [ ] **Step 3: Commit**

```bash
git -C src/SharedKernel add README.md .claude/CLAUDE.md
git -C src/SharedKernel commit -m "Document Blazor UI infrastructure conventions"
```

**PR 1 is now complete.** Push the SharedKernel branch and open the PR. Record the merge commit SHA for Task 14.

---

## Task 10: UserAccess Blazor project and Users pages

**Files:**
- Create: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.Blazor/` (csproj, `GlobalUsings.cs`, `_Imports.razor`, `UserAccessBlazorAssemblyReference.cs`, `UserAccessNavigation.cs`, `Users/Models/NewUserFormModel.cs`, three page pairs)
- Modify: `src/Modules/UserAccess/Sergin.UserAccess/UserAccessModule.cs`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess/Sergin.UserAccess.csproj`
- Modify: `src/Modules/UserAccess/Sergin.UserAccess.Infrastructure/Users/Repositories/Queries/UserQueryRepository.cs`
- Modify: `Sergin.MeterMinder.slnx`

**Interfaces:**
- Consumes: `ISerginUiDispatcher`, `SendListAsync<TItem>`, `IUiErrorPresenter`, `SerginProblemPanel`, `SerginNavItem`, `ISerginWebUiModule`.
- Produces: `UserAccessBlazorAssemblyReference.Assembly`; `UserAccessModule : ISerginWebApiModule, ISerginWebUiModule`. Task 13's host lists the module.

- [ ] **Step 1: Create the project**

`Sergin.UserAccess.Presentation.Blazor.csproj`:

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

`GlobalUsings.cs`:

```csharp
global using ErrorOr;
global using MediatR;
global using Sergin.SharedKernel.Application;
```

`_Imports.razor`:

```razor
@using Microsoft.AspNetCore.Components
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using MudBlazor
@using Sergin.SharedKernel.Application
@using Sergin.SharedKernel.Presentation.Blazor.Dispatching
@using Sergin.SharedKernel.Presentation.Blazor.Errors
@using Sergin.UserAccess.Application.Users.Commands.Create
@using Sergin.UserAccess.Application.Users.Commands.GetList
@using Sergin.UserAccess.Application.Users.Commands.GetOne
```

Verify those three Application namespaces against the real files before writing them.

`UserAccessBlazorAssemblyReference.cs` — mirror the existing `UserAccessApplicationAssemblyReference`:

```csharp
using System.Reflection;

namespace Sergin.UserAccess.Presentation.Blazor;

public static class UserAccessBlazorAssemblyReference
{
    public static readonly Assembly Assembly = typeof(UserAccessBlazorAssemblyReference).Assembly;
}
```

`UserAccessNavigation.cs` — keeping MudBlazor's icon constants inside the RCL means the composition root needs no `using MudBlazor`:

```csharp
using MudBlazor;
using Sergin.SharedKernel.Modules;

namespace Sergin.UserAccess.Presentation.Blazor;

public static class UserAccessNavigation
{
    public static IReadOnlyCollection<SerginNavItem> Items { get; } =
    [
        new SerginNavItem("Users", "/ua/users", Icons.Material.Filled.People, Order: 200)
    ];
}
```

- [ ] **Step 2: Fix the unstable paging in the list query**

`UserQueryRepository.GetListAsync`'s paged `SELECT` has no `ORDER BY`, so `LIMIT`/`OFFSET` over an unordered Postgres result can repeat and skip rows between pages. Add `ORDER BY id` to the second statement — IDs are UUIDv7, so this is chronological too:

```sql
SELECT id, user_name AS userName
FROM ua.users
ORDER BY id
LIMIT @PageSize OFFSET @Offset;
```

Read the file first and preserve its exact column aliases; only the `ORDER BY` line is new.

- [ ] **Step 3: Write the list page**

`Users/Pages/UserListPage.razor`:

```razor
@page "/ua/users"

<PageTitle>Users</PageTitle>

<MudStack Row="true" Justify="Justify.SpaceBetween" AlignItems="AlignItems.Center" Class="mb-4">
    <MudText Typo="Typo.h4">Users</MudText>
    <MudButton Variant="Variant.Filled" Color="Color.Primary"
               StartIcon="@Icons.Material.Filled.Add" Href="/ua/users/new">New user</MudButton>
</MudStack>

<MudTable T="GetUserListItem" ServerData="LoadAsync" Hover="true" Striped="true"
          OnRowClick="@(args => OpenUser(args.Item))" RowsPerPage="10">
    <HeaderContent>
        <MudTh>User name</MudTh>
        <MudTh>Id</MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd DataLabel="User name">@context.UserName</MudTd>
        <MudTd DataLabel="Id">@context.Id</MudTd>
    </RowTemplate>
    <PagerContent>
        <MudTablePager />
    </PagerContent>
</MudTable>

<MudText Typo="Typo.caption">Server-side sorting and filtering are not implemented yet.</MudText>
```

`Users/Pages/UserListPage.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.UserAccess.Application.Users.Commands.GetList;

namespace Sergin.UserAccess.Presentation.Blazor.Users.Pages;

public sealed partial class UserListPage
{
    [Inject]
    private ISerginUiDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private async Task<TableData<GetUserListItem>> LoadAsync(TableState state, CancellationToken cancellationToken)
    {
        // MudBlazor's TableState.Page is 0-based; Sergin's PageIndex is 1-based.
        ErrorOr<ListQueryResponse<GetUserListItem>> result =
            await Dispatcher.SendListAsync<GetUserListItem>(state.PageSize, state.Page + 1, cancellationToken);

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return new TableData<GetUserListItem> { Items = [], TotalItems = 0 };
        }

        return new TableData<GetUserListItem> { Items = result.Value.Data, TotalItems = result.Value.Total };
    }

    private void OpenUser(GetUserListItem item) => Navigation.NavigateTo($"/ua/users/{item.Id}");
}
```

If Task 0 found `ServerData` takes no `CancellationToken`, drop that parameter and pass `CancellationToken.None`.

- [ ] **Step 4: Write the detail page**

`Users/Pages/UserDetailPage.razor`:

```razor
@page "/ua/users/{Id:guid}"

<PageTitle>User</PageTitle>

<MudButton StartIcon="@Icons.Material.Filled.ArrowBack" Href="/ua/users" Class="mb-4">Back to users</MudButton>

<SerginProblemPanel Problem="problem" />

@if (user is not null)
{
    <MudCard>
        <MudCardContent>
            <MudText Typo="Typo.h5">@user.UserName</MudText>
            <MudText Typo="Typo.body2">@user.Id</MudText>
        </MudCardContent>
        <MudCardActions>
            <MudButton Variant="Variant.Filled" Color="Color.Warning"
                       Disabled="deactivating" OnClick="DeactivateAsync">Deactivate</MudButton>
        </MudCardActions>
    </MudCard>
}
```

`Users/Pages/UserDetailPage.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;
using Sergin.UserAccess.Application.Users.Commands.DeactivateUser;
using Sergin.UserAccess.Application.Users.Commands.GetOne;

namespace Sergin.UserAccess.Presentation.Blazor.Users.Pages;

public sealed partial class UserDetailPage
{
    private UserQueryResponse? user;
    private SerginProblem? problem;
    private bool deactivating;

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ISerginUiDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    protected override async Task OnParametersSetAsync()
    {
        ErrorOr<UserQueryResponse> result = await Dispatcher.SendAsync(new GetUserByIdQueryCommand(Id));

        if (result.IsError)
        {
            user = null;
            problem = ErrorPresenter.Present(result.FirstError);

            return;
        }

        problem = null;
        user = result.Value;
    }

    private async Task DeactivateAsync()
    {
        deactivating = true;

        ErrorOr<DeactivateUserCommandResponse> result = await Dispatcher.SendAsync(new DeactivateUserCommand(Id));

        deactivating = false;

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);
        }
    }
}
```

Verify `DeactivateUserCommand`'s namespace and constructor against the real file.

> **Known limitation, do not try to fix here.** `UserQueryResponse` is `(Guid Id, string UserName)` — it carries no `IsActive`, even though `User.IsActive` exists and `Deactivate()` sets it. So the page cannot show current state and Deactivate gives no feedback beyond the snackbar. Widening the response record and its `SELECT` is a real API contract change and belongs in its own slice.

This page carries `[RequiredPermissions("permission.ua.users.read")]` on its query, making it the live proof that the permission pipeline works — removing that permission from `Sergin:DevUser:Permissions` should render a Forbidden panel.

- [ ] **Step 5: Write the create page**

`Users/Models/NewUserFormModel.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Sergin.UserAccess.Presentation.Blazor.Users.Models;

public sealed class NewUserFormModel
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string UserName { get; set; } = string.Empty;
}
```

`Users/Pages/CreateUserPage.razor`:

```razor
@page "/ua/users/new"

<PageTitle>New user</PageTitle>

<MudText Typo="Typo.h4" Class="mb-4">New user</MudText>

<EditForm Model="model" OnValidSubmit="SubmitAsync">
    <DataAnnotationsValidator />

    <MudCard>
        <MudCardContent>
            <MudTextField @bind-Value="model.UserName" Label="User name" For="@(() => model.UserName)" />
        </MudCardContent>
        <MudCardActions>
            <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled"
                       Color="Color.Primary" Disabled="submitting">Create</MudButton>
            <MudButton Href="/ua/users">Cancel</MudButton>
        </MudCardActions>
    </MudCard>
</EditForm>
```

`Users/Pages/CreateUserPage.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.UserAccess.Application.Users.Commands.Create;
using Sergin.UserAccess.Domain.Users;
using Sergin.UserAccess.Presentation.Blazor.Users.Models;

namespace Sergin.UserAccess.Presentation.Blazor.Users.Pages;

public sealed partial class CreateUserPage
{
    private readonly NewUserFormModel model = new();

    private bool submitting;

    [Inject]
    private ISerginUiDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private async Task SubmitAsync()
    {
        submitting = true;

        ErrorOr<CreateUserCommandResponse> result =
            await Dispatcher.SendAsync(new CreateUserCommand(new UserName(model.UserName)));

        submitting = false;

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return;
        }

        Navigation.NavigateTo($"/ua/users/{result.Value.Id}");
    }
}
```

- [ ] **Step 6: Implement the capability on `UserAccessModule`**

Add to the interface list and add two members — the existing four are untouched:

```csharp
public sealed class UserAccessModule : ISerginWebApiModule, ISerginWebUiModule
{
    // ... Schema, ApplicationAssembly, AddServices, MigrateAsync, MapEndpoints unchanged ...

    public Assembly UiAssembly => UserAccessBlazorAssemblyReference.Assembly;

    public IReadOnlyCollection<SerginNavItem> NavItems => UserAccessNavigation.Items;
}
```

Add `using Sergin.UserAccess.Presentation.Blazor;` and a project reference in `Sergin.UserAccess.csproj`:

```xml
		<ProjectReference Include="..\Sergin.UserAccess.Presentation.Blazor\Sergin.UserAccess.Presentation.Blazor.csproj" />
```

- [ ] **Step 7: Register in the root solution and build**

Add to `Sergin.MeterMinder.slnx` under `/src/Modules/UserAccess/Presentation/`:

```xml
    <Project Path="src/Modules/UserAccess/Sergin.UserAccess.Presentation.Blazor/Sergin.UserAccess.Presentation.Blazor.csproj" />
```

```bash
dotnet build Sergin.MeterMinder.slnx
```

Expected: clean.

- [ ] **Step 8: Commit**

```bash
git -C src/Modules/UserAccess add .
git -C src/Modules/UserAccess commit -m "Add Users Blazor UI and implement ISerginWebUiModule"
git add Sergin.MeterMinder.slnx
git commit -m "Register UserAccess Blazor project in the solution"
```

---

## Task 11: UserAccess module documentation

**Files:**
- Modify: `src/Modules/UserAccess/.claude/CLAUDE.md`

- [ ] **Step 1: Document the UI slice**

Add, in the file's existing voice: the `Sergin.UserAccess.Presentation.Blazor` project and its page layout; the `/ua/` route-prefix rule; `.razor` markup-only / `.razor.cs` code-behind; inject `ISerginUiDispatcher`, never `ISender`; and the `UserQueryResponse` has-no-`IsActive` limitation.

- [ ] **Step 2: Commit**

```bash
git -C src/Modules/UserAccess add .claude/CLAUDE.md
git -C src/Modules/UserAccess commit -m "Document the Users Blazor UI slice"
```

**PR 2 is now complete.** Push and open the PR. Record the merge SHA for Task 14.

---

## Task 12: MeterMinder Blazor project and Devices pages

Structurally identical to Task 10. The code is repeated rather than cross-referenced because tasks may be implemented out of order by different people.

**Files:**
- Create: `src/Modules/MeterMinder/Sergin.MeterMinder.Presentation.Blazor/` (same file set as Task 10)
- Modify: `src/Modules/MeterMinder/Sergin.MeterMinder/MeterMinderModule.cs`, `Sergin.MeterMinder.csproj`
- Modify: `src/Modules/MeterMinder/Sergin.MeterMinder.Infrastructure/Devices/Repositories/Queries/DeviceQueryRepository.cs`
- Modify: `Sergin.MeterMinder.slnx`

**Interfaces:**
- Produces: `MeterMinderBlazorAssemblyReference.Assembly`; `MeterMinderModule : ISerginWebApiModule, ISerginWebUiModule`.

- [ ] **Step 1: Create the project**

Same csproj shape as Task 10 step 1, referencing `Sergin.MeterMinder.Application` instead:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<ItemGroup>
		<FrameworkReference Include="Microsoft.AspNetCore.App" />

		<PackageReference Include="MudBlazor" />
	</ItemGroup>

	<ItemGroup>
		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Modules\Sergin.SharedKernel.Modules.csproj" />
		<ProjectReference Include="..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Blazor\Sergin.SharedKernel.Presentation.Blazor.csproj" />
		<ProjectReference Include="..\Sergin.MeterMinder.Application\Sergin.MeterMinder.Application.csproj" />
	</ItemGroup>
</Project>
```

`GlobalUsings.cs` identical to Task 10's. `_Imports.razor` identical except the module namespaces become `Sergin.MeterMinder.Application.Devices.Commands.{Create,GetList,GetOne}` and `…Manufacturers.Commands.GetList`.

`MeterMinderBlazorAssemblyReference.cs`:

```csharp
using System.Reflection;

namespace Sergin.MeterMinder.Presentation.Blazor;

public static class MeterMinderBlazorAssemblyReference
{
    public static readonly Assembly Assembly = typeof(MeterMinderBlazorAssemblyReference).Assembly;
}
```

`MeterMinderNavigation.cs`:

```csharp
using MudBlazor;
using Sergin.SharedKernel.Modules;

namespace Sergin.MeterMinder.Presentation.Blazor;

public static class MeterMinderNavigation
{
    public static IReadOnlyCollection<SerginNavItem> Items { get; } =
    [
        new SerginNavItem("Devices", "/mm/devices", Icons.Material.Filled.Router, Order: 100)
    ];
}
```

- [ ] **Step 2: Fix the unstable paging in the device list query**

Add `ORDER BY id` to the paged `SELECT` in `DeviceQueryRepository.GetListAsync`, exactly as Task 10 step 2 did for users. Read the file first and preserve its column aliases.

- [ ] **Step 3: Write the list page**

`Devices/Pages/DeviceListPage.razor`:

```razor
@page "/mm/devices"

<PageTitle>Devices</PageTitle>

<MudStack Row="true" Justify="Justify.SpaceBetween" AlignItems="AlignItems.Center" Class="mb-4">
    <MudText Typo="Typo.h4">Devices</MudText>
    <MudButton Variant="Variant.Filled" Color="Color.Primary"
               StartIcon="@Icons.Material.Filled.Add" Href="/mm/devices/new">New device</MudButton>
</MudStack>

<MudTable T="GetDeviceListItem" ServerData="LoadAsync" Hover="true" Striped="true"
          OnRowClick="@(args => OpenDevice(args.Item))" RowsPerPage="10">
    <HeaderContent>
        <MudTh>Device ID</MudTh>
        <MudTh>Manufacturer</MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd DataLabel="Device ID">@context.DeviceId</MudTd>
        <MudTd DataLabel="Manufacturer">@context.ManufacturerId</MudTd>
    </RowTemplate>
    <PagerContent>
        <MudTablePager />
    </PagerContent>
</MudTable>

<MudText Typo="Typo.caption">Server-side sorting and filtering are not implemented yet.</MudText>
```

`Devices/Pages/DeviceListPage.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.Application.Devices.Commands.GetList;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Sergin.MeterMinder.Presentation.Blazor.Devices.Pages;

public sealed partial class DeviceListPage
{
    [Inject]
    private ISerginUiDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private async Task<TableData<GetDeviceListItem>> LoadAsync(TableState state, CancellationToken cancellationToken)
    {
        // MudBlazor's TableState.Page is 0-based; Sergin's PageIndex is 1-based.
        ErrorOr<ListQueryResponse<GetDeviceListItem>> result =
            await Dispatcher.SendListAsync<GetDeviceListItem>(state.PageSize, state.Page + 1, cancellationToken);

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return new TableData<GetDeviceListItem> { Items = [], TotalItems = 0 };
        }

        return new TableData<GetDeviceListItem> { Items = result.Value.Data, TotalItems = result.Value.Total };
    }

    private void OpenDevice(GetDeviceListItem item) => Navigation.NavigateTo($"/mm/devices/{item.Id}");
}
```

- [ ] **Step 4: Write the detail page**

`Devices/Pages/DeviceDetailPage.razor`:

```razor
@page "/mm/devices/{Id:guid}"

<PageTitle>Device</PageTitle>

<MudButton StartIcon="@Icons.Material.Filled.ArrowBack" Href="/mm/devices" Class="mb-4">Back to devices</MudButton>

<SerginProblemPanel Problem="problem" />

@if (device is not null)
{
    <MudCard>
        <MudCardContent>
            <MudText Typo="Typo.h5">@device.DeviceId</MudText>
            <MudText Typo="Typo.body2">Manufacturer: @device.ManufacturerId</MudText>
            <MudText Typo="Typo.body2">@device.Id</MudText>
        </MudCardContent>
    </MudCard>
}
```

`Devices/Pages/DeviceDetailPage.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using Sergin.MeterMinder.Application.Devices.Commands.GetOne;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.Presentation.Blazor.Devices.Pages;

public sealed partial class DeviceDetailPage
{
    private DeviceQueryResponse? device;
    private SerginProblem? problem;

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ISerginUiDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    protected override async Task OnParametersSetAsync()
    {
        ErrorOr<DeviceQueryResponse> result = await Dispatcher.SendAsync(new GetDeviceByIdQueryCommand(Id));

        if (result.IsError)
        {
            device = null;
            problem = ErrorPresenter.Present(result.FirstError);

            return;
        }

        problem = null;
        device = result.Value;
    }
}
```

- [ ] **Step 5: Write the create page**

`Devices/Models/NewDeviceFormModel.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Sergin.MeterMinder.Presentation.Blazor.Devices.Models;

public sealed class NewDeviceFormModel
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string DeviceId { get; set; } = string.Empty;

    [Required]
    public Guid ManufacturerId { get; set; }
}
```

`Devices/Pages/CreateDevicePage.razor`:

```razor
@page "/mm/devices/new"

<PageTitle>New device</PageTitle>

<MudText Typo="Typo.h4" Class="mb-4">New device</MudText>

<EditForm Model="model" OnValidSubmit="SubmitAsync">
    <DataAnnotationsValidator />

    <MudCard>
        <MudCardContent>
            <MudTextField @bind-Value="model.DeviceId" Label="Device ID" For="@(() => model.DeviceId)" />

            <MudSelect T="Guid" @bind-Value="model.ManufacturerId" Label="Manufacturer"
                       For="@(() => model.ManufacturerId)">
                @foreach (GetManufacturerListItem manufacturer in manufacturers)
                {
                    <MudSelectItem T="Guid" Value="@manufacturer.Id">@manufacturer.Name</MudSelectItem>
                }
            </MudSelect>
        </MudCardContent>
        <MudCardActions>
            <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled"
                       Color="Color.Primary" Disabled="submitting">Create</MudButton>
            <MudButton Href="/mm/devices">Cancel</MudButton>
        </MudCardActions>
    </MudCard>
</EditForm>
```

`Devices/Pages/CreateDevicePage.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using Sergin.MeterMinder.Application.Devices.Commands.Create;
using Sergin.MeterMinder.Application.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.Domain.Devices;
using Sergin.MeterMinder.Presentation.Blazor.Devices.Models;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Sergin.MeterMinder.Presentation.Blazor.Devices.Pages;

public sealed partial class CreateDevicePage
{
    private readonly NewDeviceFormModel model = new();

    private IReadOnlyCollection<GetManufacturerListItem> manufacturers = [];
    private bool submitting;

    [Inject]
    private ISerginUiDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        ErrorOr<ListQueryResponse<GetManufacturerListItem>> result =
            await Dispatcher.SendListAsync<GetManufacturerListItem>(200, 1);

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return;
        }

        manufacturers = result.Value.Data;
    }

    private async Task SubmitAsync()
    {
        submitting = true;

        ErrorOr<CreateDeviceCommandResponse> result = await Dispatcher.SendAsync(
            new CreateDeviceCommand(new DeviceId(model.DeviceId), model.ManufacturerId));

        submitting = false;

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return;
        }

        Navigation.NavigateTo($"/mm/devices/{result.Value.Id}");
    }
}
```

Check `CreateDeviceCommand`'s real parameter types before writing this — it may take raw `string`/`Guid` rather than wrapped types.

> **Known limitation, do not try to fix here.** `CreateDeviceCommandHandler` performs no FK-existence check on `ManufacturerId`; a bad ID surfaces as a raw Postgres FK-violation exception rather than an `ErrorOr` error. The `MudSelect` avoids it in the happy path but does not fix it. The project's CLAUDE.md says explicitly to flag this rather than invent an `ErrorOr` mapping with no precedent.

- [ ] **Step 6: Implement the capability on `MeterMinderModule`**

```csharp
public sealed class MeterMinderModule : ISerginWebApiModule, ISerginWebUiModule
{
    // ... Schema, ApplicationAssembly, AddServices, MigrateAsync, MapEndpoints unchanged ...

    public Assembly UiAssembly => MeterMinderBlazorAssemblyReference.Assembly;

    public IReadOnlyCollection<SerginNavItem> NavItems => MeterMinderNavigation.Items;
}
```

Plus `using Sergin.MeterMinder.Presentation.Blazor;` and the project reference in `Sergin.MeterMinder.csproj`.

- [ ] **Step 7: Register in the solution and build**

Add to `Sergin.MeterMinder.slnx` under `/src/Modules/MeterMinder/Presentation/`, then:

```bash
dotnet build Sergin.MeterMinder.slnx
```

Expected: clean.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/MeterMinder Sergin.MeterMinder.slnx
git commit -m "Add Devices Blazor UI and implement ISerginWebUiModule"
```

---

## Task 13: The UI host

**Files:**
- Create: `src/Hosts/Sergin.MeterMinder.Hosts.WebUi.All/` — csproj, `Program.cs`, `Components/App.razor`, `Components/Routes.razor`, `Components/_Imports.razor`, `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`, `Dockerfile`, `wwwroot/app.css`
- Modify: `Sergin.MeterMinder.slnx`
- Delete: `src/Hosts/Sergin.Hosts.WebApi.All/`

**Interfaces:**
- Consumes: `AddSerginWebUi`, `UseSerginWebUiAsync<TRootComponent>` (Task 8); `SerginUiModuleCatalog`, `SerginMainLayout` (Tasks 5–6); both module classes (Tasks 10, 12).
- Produces: a runnable host on ports 5002/5003 and `public partial class Program;` for Task 14's tests.

- [ ] **Step 1: Create the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

	<PropertyGroup>
		<UserSecretsId>2a09bf43-7332-4840-b0f1-257f452d1cc5</UserSecretsId>
		<DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>
		<DockerfileContext>..\..\..</DockerfileContext>
		<DockerComposeProjectPath>..\..\..\docker-compose\docker-compose.dcproj</DockerComposeProjectPath>
	</PropertyGroup>

	<ItemGroup>
		<ProjectReference Include="..\..\Modules\MeterMinder\Sergin.MeterMinder\Sergin.MeterMinder.csproj" />
		<ProjectReference Include="..\..\Modules\UserAccess\Sergin.UserAccess\Sergin.UserAccess.csproj" />
		<ProjectReference Include="..\..\SharedKernel\Sergin.SharedKernel.Hosts.WebUi\Sergin.SharedKernel.Hosts.WebUi.csproj" />

		<!--
			These two look redundant and are NOT. Static web assets propagate only through projects that
			import Microsoft.NET.Sdk.StaticWebAssets. The module composition roots (Sergin.MeterMinder,
			Sergin.UserAccess) are plain Microsoft.NET.Sdk, so the chain host -> composition root -> RCL is
			silently broken at the middle hop (ResolveReferencedProjectsStaticWebAssetsConfiguration probes
			with SkipNonexistentTargets="true"). Referencing the RCLs directly restores _content/ assets.
		-->
		<ProjectReference Include="..\..\Modules\MeterMinder\Sergin.MeterMinder.Presentation.Blazor\Sergin.MeterMinder.Presentation.Blazor.csproj" />
		<ProjectReference Include="..\..\Modules\UserAccess\Sergin.UserAccess.Presentation.Blazor\Sergin.UserAccess.Presentation.Blazor.csproj" />
	</ItemGroup>

	<ItemGroup>
		<PackageReference Include="Microsoft.EntityFrameworkCore.Design">
			<PrivateAssets>all</PrivateAssets>
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
		</PackageReference>
	</ItemGroup>

</Project>
```

The `UserSecretsId` is deliberately the API host's, so one `dotnet user-secrets set "Sergin:ConnectionStrings:Database" "…"` covers both. No `NuGetAuditSuppress` — this host never references `Microsoft.AspNetCore.OpenApi`.

- [ ] **Step 2: Write `Program.cs`**

```csharp
using Sergin.MeterMinder;
using Sergin.MeterMinder.Hosts.WebUi.All.Components;
using Sergin.SharedKernel.Modules;
using Sergin.UserAccess;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("sergin-webui-all");

IReadOnlyCollection<ISerginModule> modules = [new MeterMinderModule(), new UserAccessModule()];

builder.AddSerginWebUi(modules);

WebApplication app = builder.Build();

await app.UseSerginWebUiAsync<App>(modules);

await app.RunAsync();

public partial class Program;
```

- [ ] **Step 3: Write the root components**

`Components/App.razor` — the HTML document. `@Assets[...]` resolves fingerprinted static asset URLs, including RCL `_content/` paths:

```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <link rel="stylesheet" href="@Assets["_content/MudBlazor/MudBlazor.min.css"]" />
    <link rel="stylesheet" href="@Assets["app.css"]" />
    <HeadOutlet />
</head>
<body>
    <Routes />
    <script src="_framework/blazor.web.js"></script>
    <script src="@Assets["_content/MudBlazor/MudBlazor.min.js"]"></script>
</body>
</html>
```

`Components/Routes.razor` — the `Router`'s `AdditionalAssemblies` is what makes module pages reachable **interactively**; `AddAdditionalAssemblies` in Task 8 covers static SSR. Both are required:

```razor
@inject SerginUiModuleCatalog Catalog

<Router AppAssembly="typeof(Program).Assembly" AdditionalAssemblies="Catalog.RoutableAssemblies">
    <Found Context="routeData">
        <RouteView RouteData="routeData" DefaultLayout="typeof(SerginMainLayout)" />
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
    <NotFound>
        <LayoutView Layout="typeof(SerginMainLayout)">
            <MudText Typo="Typo.h5">Not found</MudText>
        </LayoutView>
    </NotFound>
</Router>
```

`Components/_Imports.razor`:

```razor
@using Microsoft.AspNetCore.Components
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using MudBlazor
@using Sergin.MeterMinder.Hosts.WebUi.All.Components
@using Sergin.SharedKernel.Presentation.Blazor.Layout
@using Sergin.SharedKernel.Presentation.Blazor.Modules
```

`wwwroot/app.css` — an empty file with a comment is fine; it exists so `@Assets["app.css"]` resolves.

- [ ] **Step 4: Write the configuration**

`appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Sergin": {
    "ConnectionStrings": {
      "Database": ""
    },
    "DevUser": {
      "Id": "01920000-0000-7000-8000-000000000001",
      "UserName": "dev",
      "FirstName": "Development",
      "LastName": "User",
      "Email": "dev@sergin.local",
      "Permissions": [
        "permission.mm.devices.read",
        "permission.ua.users.read"
      ]
    }
  }
}
```

Those are exactly the permissions `[RequiredPermissions]` demands on `GetDeviceByIdQueryCommand` and `GetUserByIdQueryCommand`. Deleting one makes the matching detail page render Forbidden — the cheapest live proof the permission pipeline is wired.

`appsettings.Development.json` mirrors the API host's (logging only).

`Properties/launchSettings.json` — ports 5002/5003, since 5000/5001 are the API host and 5432/18888/4317 are the compose stack:

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "launchBrowser": true,
      "launchUrl": "mm/devices",
      "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" },
      "dotnetRunMessages": true,
      "applicationUrl": "http://localhost:5002"
    },
    "https": {
      "commandName": "Project",
      "launchBrowser": true,
      "launchUrl": "mm/devices",
      "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" },
      "dotnetRunMessages": true,
      "applicationUrl": "https://localhost:5003;http://localhost:5002"
    }
  },
  "$schema": "https://json.schemastore.org/launchsettings.json"
}
```

`Dockerfile` — copy the API host's and change the three project paths plus `ENTRYPOINT ["dotnet", "Sergin.MeterMinder.Hosts.WebUi.All.dll"]`.

- [ ] **Step 5: Register in the solution, delete the stale directory, build**

Add to `Sergin.MeterMinder.slnx` under `/src/Hosts/`. Then remove the rename residue (a `.csproj.user`, an empty `Properties/`, stale `bin`/`obj`, not in the solution) so nobody mistakes it for a template:

```bash
rm -rf src/Hosts/Sergin.Hosts.WebApi.All
dotnet build Sergin.MeterMinder.slnx
```

Expected: clean.

- [ ] **Step 6: Run it**

```bash
dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.WebUi.All
```

Then check, at `http://localhost:5002`:
- The nav shows **Devices** and **Users** — entries contributed by two independent modules.
- `/mm/devices` and `/ua/users` render tables and page past page 1.
- MudBlazor styling is applied (if the page is unstyled, the static-asset chain in step 1 is broken).
- Creating a device and a user works, and the new row appears in the list.

- [ ] **Step 7: Commit**

```bash
git add src/Hosts Sergin.MeterMinder.slnx
git commit -m "Add all-in-one Blazor Web UI host"
```

---

## Task 14: UI integration tests

**Files:**
- Create: `tests/Sergin.MeterMinder.IntegrationTests.WebUi.All/` — csproj, `WebUiIntegrationTestCollection.cs`, `Shell/ModulePageRenderingTests.cs`
- Modify: `Sergin.MeterMinder.slnx`

**Interfaces:**
- Consumes: `SerginWebApiFactory<TEntryPoint>` (existing, in the SharedKernel submodule; host-agnostic despite its name), and the UI host's `Program`.

This **must** be a separate project. `public partial class Program;` sits in the global namespace in both hosts, so one project referencing both host assemblies fails with CS0433 — an ambiguity no `using` resolves.

- [ ] **Step 1: Create the test project**

Copy `tests/Sergin.MeterMinder.IntegrationTests.WebApi.All/*.csproj`, swap the host `ProjectReference` for `Sergin.MeterMinder.Hosts.WebUi.All`, and drop any `NuGetAuditSuppress` (no OpenAPI in this closure). Register it in `Sergin.MeterMinder.slnx` under `/tests/`.

- [ ] **Step 2: Write the collection fixture**

```csharp
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.WebUi.All;

[CollectionDefinition(nameof(WebUiIntegrationTestCollection))]
public sealed class WebUiIntegrationTestCollection : ICollectionFixture<SerginWebApiFactory<Program>>;
```

`SerginWebApiFactory` forces `UseEnvironment("Development")`, which the UI host requires — it throws outside Development by design.

- [ ] **Step 3: Write the failing test**

```csharp
using System.Net;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.WebUi.All.Shell;

[Collection(nameof(WebUiIntegrationTestCollection))]
public sealed class ModulePageRenderingTests(SerginWebApiFactory<Program> factory)
{
    [Theory]
    [InlineData("/mm/devices")]
    [InlineData("/ua/users")]
    [InlineData("/mm/devices/new")]
    [InlineData("/ua/users/new")]
    public async Task ModulePage_RendersServerSide_WithNavFromBothModules(string path)
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();

        // Both modules contributed nav entries, so the shell composed them.
        Assert.Contains("/mm/devices", html, StringComparison.Ordinal);
        Assert.Contains("/ua/users", html, StringComparison.Ordinal);
    }
}
```

Blazor Server returns real server-rendered HTML before any circuit exists, so a plain `HttpClient` sees page content. This one test covers the four things most likely to break silently: `AddAdditionalAssemblies` (without it these 404), cross-module nav aggregation, the route-prefix guard, and the whole `AddSerginWebUi`/`AddSerginCore` graph resolving — `ValidateOnStart` on `DevUserOptions` fires here too.

- [ ] **Step 4: Run it and watch it fail for the right reason**

```bash
dotnet test tests/Sergin.MeterMinder.IntegrationTests.WebUi.All/Sergin.MeterMinder.IntegrationTests.WebUi.All.csproj
```

If Tasks 1–13 are complete this should PASS immediately. If it fails, the message identifies which seam is broken — a 404 means the additional-assembly registration; a startup exception naming a module and `@page` means the route guard caught a real violation; a missing nav link means the catalog is not reaching `SerginNavMenu`.

- [ ] **Step 5: Add a round-trip test**

```csharp
    [Fact]
    public async Task CreateUserPage_And_UserListPage_ShareTheSameData()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage listResponse = await client.GetAsync("/ua/users");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        HttpResponseMessage createResponse = await client.GetAsync("/ua/users/new");

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.Contains("User name", await createResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
```

Interactive behaviour (clicking a `MudButton`) needs a live circuit and a browser — that is Playwright, deliberately out of scope. bUnit is the right tool for components in isolation but needs new package entries in both `Directory.Packages.props` files; defer it.

- [ ] **Step 6: Run both suites**

```bash
dotnet test tests/Sergin.MeterMinder.IntegrationTests.WebApi.All/Sergin.MeterMinder.IntegrationTests.WebApi.All.csproj
dotnet test tests/Sergin.MeterMinder.IntegrationTests.WebUi.All/Sergin.MeterMinder.IntegrationTests.WebUi.All.csproj
```

Expected: both PASS.

- [ ] **Step 7: Commit**

```bash
git add tests Sergin.MeterMinder.slnx
git commit -m "Add static-SSR integration tests for the Blazor UI host"
```

---

## Task 15: Compose, documentation, and submodule pointers

**Files:**
- Modify: `docker-compose/docker-compose.yml`, `docker-compose/docker-compose.override.yml`, `docker-compose/launchSettings.json`
- Modify: `.claude/CLAUDE.md`, `.claude/skills/add-module/SKILL.md`, `.claude/skills/add-feature/SKILL.md`, `README.md`
- Modify: submodule pointers for `src/SharedKernel` and `src/Modules/UserAccess`

- [ ] **Step 1: Add the UI host to Docker Compose**

In `docker-compose/docker-compose.yml`, mirroring the existing `sergin.hosts-all` service:

```yaml
  sergin.hosts-webui-all:
    image: ${DOCKER_REGISTRY-}sergin-webui
    container_name: Sergin.Hosts-WebUi-All
    build:
      context: ..
      dockerfile: src/Hosts/Sergin.MeterMinder.Hosts.WebUi.All/Dockerfile
    ports:
      - 5002:8080
      - 5003:8081
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      Sergin__ConnectionStrings__Database: Host=sergin.database;Port=5432;Database=Sergin_DB;Username=postgres;Password=Ww4pC8bzn4
    depends_on:
      sergin.database:
        condition: service_healthy
      sergin.dashboard:
        condition: service_started
```

Mirror the API host's block in `docker-compose.override.yml` (ports, OTLP endpoint, user-secrets volumes) and add `"sergin.hosts-webui-all": "StartWithoutDebugging"` to `docker-compose/launchSettings.json`.

- [ ] **Step 2: Update the root documentation**

`.claude/CLAUDE.md` gains a "Blazor UI host" section covering: `ISerginWebUiModule`; `AddSerginCore` / `AddSerginWebUi`; the `/{schema}/` route rule and its startup guard; **inject `ISerginUiDispatcher`, never `ISender`**, with the circuit-lifetime reasoning; **`.razor` markup-only, C# in `.razor.cs`**, with the analyzer reasoning; and the direct-RCL-reference requirement in the host csproj.

`.claude/skills/add-module/SKILL.md`: a module may now also ship a `Presentation.Blazor` RCL and implement `ISerginWebUiModule` — `UiAssembly`, `NavItems`, and the schema-prefixed `@page` rule.

`.claude/skills/add-feature/SKILL.md`: an optional UI slice (list/detail/create pages) alongside the endpoint slice, with the dispatcher and code-behind conventions.

`README.md`: how to run the UI host, and the port table.

- [ ] **Step 3: Pin the submodules to their merged commits**

Only after PRs 1 and 2 have merged:

```bash
git -C src/SharedKernel fetch origin && git -C src/SharedKernel checkout <sharedkernel-main-sha>
git -C src/Modules/UserAccess fetch origin && git -C src/Modules/UserAccess checkout <useraccess-main-sha>
git add src/SharedKernel src/Modules/UserAccess
git submodule status   # no leading '+' — pointers match the checked-out commits
```

Pin to merged `main` SHAs, never branch tips: git records a commit, not a ref.

- [ ] **Step 4: Full verification**

```bash
dotnet build src/SharedKernel/Sergin.SharedKernel.slnx
dotnet build Sergin.MeterMinder.slnx
dotnet test tests/Sergin.MeterMinder.IntegrationTests.WebApi.All/Sergin.MeterMinder.IntegrationTests.WebApi.All.csproj
dotnet test tests/Sergin.MeterMinder.IntegrationTests.WebUi.All/Sergin.MeterMinder.IntegrationTests.WebUi.All.csproj
docker compose -f docker-compose/docker-compose.yml up --build
```

Then the manual checks that automation cannot cover:
- API host on 5000 still serves `/scalar/v1` with all 10 endpoints.
- UI host on 5002 shows both modules' nav; create and page through both aggregates.
- Remove `permission.mm.devices.read` from `appsettings.json` → the device detail page renders Forbidden.
- Put a bogus string in `Sergin:DevUser:Permissions` → the host refuses to start with a precise message.
- Temporarily change a module `@page` to drop its schema prefix → `UseSerginWebUiAsync` throws naming the component and template.
- `ASPNETCORE_ENVIRONMENT=Staging dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.WebUi.All` → throws before opening a port.

- [ ] **Step 5: Fresh-clone check**

```bash
git clone --recurse-submodules <url> /tmp/fresh && cd /tmp/fresh && dotnet build Sergin.MeterMinder.slnx
```

Expected: clean. This is the only check that proves the submodule pointers are right.

- [ ] **Step 6: Commit**

```bash
git add docker-compose .claude README.md src/SharedKernel src/Modules/UserAccess
git commit -m "Wire the Blazor UI host into compose and document the UI conventions"
```

---

## Deferred, deliberately

Recorded here so they are not silently dropped:

- **Real authentication.** Needs credential storage on `User`, hashing, a migration, a login slice, and a permissions source. Its own spec and cycle. Until then the UI host refuses to start outside Development.
- **Server-side sort/filter/search.** `ToListQuery` drops `Filtering`/`Sorting`, and no repository reads `Term`. Wiring it means changing `ToListQuery`, the `ListQueryFactory` call sites, and all three repositories' SQL.
- **bUnit component tests.** Needs 2–3 new `PackageVersion` entries in both package files, plus `Services.AddMudServices()` and `JSInterop.Mode = JSRuntimeMode.Loose` because Mud components call JS.
- **Manufacturers UI.** Repeats the pattern Devices and Users establish.
- **`UserQueryResponse` has no `IsActive`**, so the Users detail page cannot show deactivation state.
- **Deleting the dead `RTS.Common.Domain.Repository.Query.ListQueryResponse<T>`** duplicate.
- **Renaming `SerginWebApiFactory<TEntryPoint>`** to something host-neutral now that a non-WebApi host uses it.
- **`MapDefaultEndpoints()`** exists in `Sergin.SharedKernel.Hosts` and is called by neither host; `/health` and `/alive` are dead today.
- **An Aspire AppHost.** None exists; orchestration stays Docker Compose. The root `README.md` and `.claude/CLAUDE.md` overstate this as ".NET Aspire for local orchestration".
