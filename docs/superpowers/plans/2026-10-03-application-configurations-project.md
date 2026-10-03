# Application.Configurations Project Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move every command configuration and aggregate feature configuration into a new per-module `.Application.Configurations` project, named by `ConfigurationsAssembly` on the module contracts, so `.Application.Contracts` holds request/response shapes only.

**Architecture:** One new project per module (`Sergin.<Module>.Application.Configurations`, referencing `.Application.Contracts`). `ISerginModule` and `ISerginRemoteModule` gain `Assembly ConfigurationsAssembly`. `AddSerginCore` reads both registries from it, and one widened placement guard refuses a configuration found in any other module assembly. No behavior change: identical `Configure` bodies, no migration.

**Tech Stack:** .NET 10, C#, xUnit + Testcontainers integration tests, EF Core 10, MediatR, three git repos (host + two submodules).

**Spec:** `docs/superpowers/specs/2026-10-03-application-configurations-project-design.md`

## Global Constraints

- Work in the worktree `C:\@factory\Sergin\Sergin.MeterMinder-appconfig` (branch `feat/application-configurations`). Submodules are already initialised there.
- `src/SharedKernel` and `src/Modules/UserAccess` are separate repos. Commit their changes **inside** each submodule, on a branch `feat/application-configurations` in that submodule. Never commit submodule files from the host repo.
- `TreatWarningsAsErrors=true`, `AnalysisMode=All`, SonarAnalyzer: any warning fails the build. Run `dotnet build Sergin.MeterMinder.slnx` from the worktree root after every code step.
- Central Package Management: no `Version` attribute on any `PackageReference`. (No new packages are needed.)
- Configuration classes stay `internal sealed`, with a parameterless constructor, and keep identical `Configure` bodies.
- New project names: `Sergin.MeterMinder.DeviceManagement.Application.Configurations`, `Sergin.UserAccess.Application.Configurations`. Marker classes: `DeviceManagementApplicationConfigurationsAssemblyReference`, `UserAccessApplicationConfigurationsAssemblyReference`, each `public static class` with `public static readonly Assembly Assembly`.
- Namespaces in the new project replace `.Application.Contracts.` / `.Application.` with `.Application.Configurations.` and keep the rest of the folder path.
- The guard message must name every offending type's full name and contain `.Application.Configurations`.
- Commits: no `Co-Authored-By: Claude` trailer (repo rule in CLAUDE.md overrides).
- Docker must be running for integration tests that use `SerginWebApiFactory`. If `docker info` fails, launch Docker Desktop and poll `docker info` before `dotnet test`.

## Review Focus

- A remote module whose `ContractsAssembly` holds a configuration: host start must be refused even with zero local modules. Test in Task 4 (`RemoteModuleConfigurationInContracts_StopsComposition`).
- The same assembly scanned twice (a module whose Application and Contracts assembly are the same): each misplaced type is named once in the message. Test in Task 4 (`AnAssemblyScannedTwice_NamesEachTypeOnce`).
- A module whose `ConfigurationsAssembly` is also its `ContractsAssembly`: it composes and is not refused. Test in Task 4 (`ConfigurationsAssemblyAlsoContracts_Composes`).
- The EF design-time model must not change, or the next `migrations add` scaffolds column drops. Probe in Task 5, Step 2.
- The gateway pre-hop permission refusal must still fire when its registry comes from the Configurations assembly. Covered by `DeviceGrpcRoundTripTests` in Task 3.

---

### Task 1: Configurations projects and the `ConfigurationsAssembly` contract

Creates both empty projects, adds the property to both module interfaces, and implements it everywhere. Nothing reads it yet. Build stays green.

**Files:**
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Configurations/Sergin.MeterMinder.DeviceManagement.Application.Configurations.csproj`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Configurations/DeviceManagementApplicationConfigurationsAssemblyReference.cs`
- Create (UA submodule): `src/Modules/UserAccess/Sergin.UserAccess.Application.Configurations/Sergin.UserAccess.Application.Configurations.csproj`
- Create (UA submodule): `src/Modules/UserAccess/Sergin.UserAccess.Application.Configurations/UserAccessApplicationConfigurationsAssemblyReference.cs`
- Modify (SK submodule): `src/SharedKernel/Sergin.SharedKernel.Modules/ISerginModule.cs`, `src/SharedKernel/Sergin.SharedKernel.Modules/ISerginRemoteModule.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement/DeviceManagementModule.cs` and its `.csproj`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.csproj`
- Modify (UA submodule): `src/Modules/UserAccess/Sergin.UserAccess/UserAccessModule.cs` and its `.csproj`, `src/Modules/UserAccess/Sergin.UserAccess.Infrastructure.Data/Sergin.UserAccess.Infrastructure.Data.csproj`
- Modify: `Sergin.MeterMinder.slnx`
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/CommandConfigurationPlacementTests.cs` (its test module implements the property)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/ModuleConfigurationsAssemblyTests.cs`

**Interfaces:**
- Produces: `Assembly ISerginModule.ConfigurationsAssembly { get; }`, `Assembly ISerginRemoteModule.ConfigurationsAssembly { get; }`, `DeviceManagementApplicationConfigurationsAssemblyReference.Assembly`, `UserAccessApplicationConfigurationsAssemblyReference.Assembly`.

- [ ] **Step 1: Branch the submodules**

```bash
cd /c/@factory/Sergin/Sergin.MeterMinder-appconfig
git -C src/SharedKernel switch -c feat/application-configurations
git -C src/Modules/UserAccess switch -c feat/application-configurations
```

- [ ] **Step 2: Write the failing test**

Create `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/ModuleConfigurationsAssemblyTests.cs`:

```csharp
using Sergin.MeterMinder.DeviceManagement;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations;
using Sergin.SharedKernel.Modules;
using Sergin.UserAccess;
using Sergin.UserAccess.Application.Configurations;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands;

/// <summary>
/// Each module names its own .Application.Configurations assembly, distinct from its Application and
/// Contracts assemblies — the placement guard relies on the three being different.
/// </summary>
public sealed class ModuleConfigurationsAssemblyTests
{
    [Fact]
    public void DeviceManagement_NamesItsConfigurationsAssembly()
    {
        ISerginModule module = new DeviceManagementModule();

        Assert.Same(DeviceManagementApplicationConfigurationsAssemblyReference.Assembly, module.ConfigurationsAssembly);
        Assert.NotSame(module.ContractsAssembly, module.ConfigurationsAssembly);
        Assert.NotSame(module.ApplicationAssembly, module.ConfigurationsAssembly);
    }

    [Fact]
    public void UserAccess_NamesItsConfigurationsAssembly()
    {
        ISerginModule module = new UserAccessModule();

        Assert.Same(UserAccessApplicationConfigurationsAssemblyReference.Assembly, module.ConfigurationsAssembly);
        Assert.NotSame(module.ContractsAssembly, module.ConfigurationsAssembly);
        Assert.NotSame(module.ApplicationAssembly, module.ConfigurationsAssembly);
    }
}
```

Check the namespaces of `DeviceManagementModule` (`Sergin.MeterMinder.DeviceManagement`) and `UserAccessModule` (`Sergin.UserAccess`) against the source before running.

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: FAIL with CS0234/CS0246 on `Application.Configurations` and CS1061 on `ConfigurationsAssembly`.

- [ ] **Step 4: Create the DeviceManagement project**

`src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Configurations/Sergin.MeterMinder.DeviceManagement.Application.Configurations.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj" />
  </ItemGroup>
</Project>
```

`DeviceManagementApplicationConfigurationsAssemblyReference.cs` in the same folder:

```csharp
using System.Reflection;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations;

public static class DeviceManagementApplicationConfigurationsAssemblyReference
{
    public static readonly Assembly Assembly = typeof(DeviceManagementApplicationConfigurationsAssemblyReference).Assembly;
}
```

- [ ] **Step 5: Create the UserAccess project (inside the UA submodule)**

`src/Modules/UserAccess/Sergin.UserAccess.Application.Configurations/Sergin.UserAccess.Application.Configurations.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Sergin.UserAccess.Application.Contracts\Sergin.UserAccess.Application.Contracts.csproj" />
  </ItemGroup>
</Project>
```

`UserAccessApplicationConfigurationsAssemblyReference.cs`:

```csharp
using System.Reflection;

namespace Sergin.UserAccess.Application.Configurations;

public static class UserAccessApplicationConfigurationsAssemblyReference
{
    public static readonly Assembly Assembly = typeof(UserAccessApplicationConfigurationsAssemblyReference).Assembly;
}
```

- [ ] **Step 6: Add the project references**

Add to each of these `.csproj` files, inside the existing `ProjectReference` `ItemGroup`:

- `Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.csproj` and `Sergin.MeterMinder.DeviceManagement.csproj`:
  `<ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Application.Configurations\Sergin.MeterMinder.DeviceManagement.Application.Configurations.csproj" />`
- `Sergin.UserAccess.Infrastructure.Data.csproj` and `Sergin.UserAccess.csproj`:
  `<ProjectReference Include="..\Sergin.UserAccess.Application.Configurations\Sergin.UserAccess.Application.Configurations.csproj" />`

- [ ] **Step 7: Register both projects in the solution**

In `Sergin.MeterMinder.slnx`, directly after the `…DeviceManagement.Application.Contracts.csproj` line in `/src/Modules/DeviceManagement/`:

```xml
    <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Configurations/Sergin.MeterMinder.DeviceManagement.Application.Configurations.csproj" />
```

and directly after the `Sergin.UserAccess.Application.Contracts.csproj` line in `/src/Modules/UserAccess/`:

```xml
    <Project Path="src/Modules/UserAccess/Sergin.UserAccess.Application.Configurations/Sergin.UserAccess.Application.Configurations.csproj" />
```

- [ ] **Step 8: Add the property to both module contracts (SK submodule)**

`ISerginModule.cs`, after `ContractsAssembly`:

```csharp
    /// <summary>
    /// The module's .Application.Configurations assembly: every command configuration and aggregate feature
    /// configuration it declares. The only assembly either registry is built from.
    /// </summary>
    Assembly ConfigurationsAssembly { get; }
```

`ISerginRemoteModule.cs`, after `ContractsAssembly`:

```csharp
    /// <summary>
    /// The module's .Application.Configurations assembly. A gateway builds its command configuration registry
    /// from it, so a forbidden remote call is refused before the gRPC hop.
    /// </summary>
    Assembly ConfigurationsAssembly { get; }
```

- [ ] **Step 9: Implement it**

`DeviceManagementModule.cs`: add `using Sergin.MeterMinder.DeviceManagement.Application.Configurations;` (keep usings sorted) and, after `ContractsAssembly`:

```csharp
    public Assembly ConfigurationsAssembly => DeviceManagementApplicationConfigurationsAssemblyReference.Assembly;
```

`UserAccessModule.cs`: add `using Sergin.UserAccess.Application.Configurations;` and:

```csharp
    public Assembly ConfigurationsAssembly => UserAccessApplicationConfigurationsAssemblyReference.Assembly;
```

`CommandConfigurationPlacementTests.cs`, in `MisplacedConfigurationModule` after `ContractsAssembly` (Task 4 rewrites this class):

```csharp
        public Assembly ConfigurationsAssembly => typeof(DeleteDeviceCommand).Assembly;
```

- [ ] **Step 10: Build and run the test**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: `Build succeeded`, 0 warnings.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~ModuleConfigurationsAssemblyTests|FullyQualifiedName~CommandConfigurationPlacementTests"`
Expected: PASS (3 tests).

- [ ] **Step 11: Commit, per repo**

```bash
git -C src/SharedKernel add -A && git -C src/SharedKernel commit -m "Add ConfigurationsAssembly to ISerginModule and ISerginRemoteModule"
git -C src/Modules/UserAccess add -A && git -C src/Modules/UserAccess commit -m "Add Sergin.UserAccess.Application.Configurations project"
git add Sergin.MeterMinder.slnx src/Modules/DeviceManagement tests && git commit -m "Add DeviceManagement Application.Configurations project"
```

The host commit must not stage `src/SharedKernel` or `src/Modules/UserAccess` pointers yet; `git status` shows them modified, leave them.

---

### Task 2: `AggregateFeatureRegistry.ConfigurationTypesIn`

A public helper listing aggregate configuration types per assembly, so the guard can treat both kinds alike.

**Files:**
- Modify (SK): `src/SharedKernel/Sergin.SharedKernel.Application/Aggregates/AggregateFeatureRegistry.cs`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/AggregateFeatureRegistryTests.cs`

**Interfaces:**
- Produces: `public static IReadOnlyCollection<Type> AggregateFeatureRegistry.ConfigurationTypesIn(Assembly assembly)`.

- [ ] **Step 1: Write the failing test**

Add to `AggregateFeatureRegistryTests` (add `using Sergin.MeterMinder.IntegrationTests.All.Commands;` if `ConfiguredTestCommandConfiguration` is not in scope):

```csharp
    [Fact]
    public void ConfigurationTypesIn_ListsOnlyAggregateFeatureConfigurations()
    {
        IReadOnlyCollection<Type> types =
            AggregateFeatureRegistry.ConfigurationTypesIn(typeof(AggregateFeatureRegistryTests).Assembly);

        Assert.Contains(typeof(UnmappedEntityAggregateFeatureConfiguration), types);
        Assert.DoesNotContain(typeof(ConfiguredTestCommandConfiguration), types);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: FAIL, CS0117 `'AggregateFeatureRegistry' does not contain a definition for 'ConfigurationTypesIn'`.

- [ ] **Step 3: Implement**

In `AggregateFeatureRegistry`, replace `FromAssemblies` with:

```csharp
    public static AggregateFeatureRegistry FromAssemblies(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return FromConfigurationTypes(assemblies.SelectMany(ConfigurationTypesIn));
    }

    /// <summary>
    /// The aggregate feature configuration types declared in <paramref name="assembly"/>, the counterpart of
    /// <c>CommandConfigurationSource.FromAssembly(assembly).ConfigurationTypes</c>. AddSerginCore's placement
    /// guard uses both to refuse a configuration outside a module's ConfigurationsAssembly.
    /// </summary>
    public static IReadOnlyCollection<Type> ConfigurationTypesIn(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return [.. assembly.GetTypes().Where(IsConfigurationType)];
    }
```

Also update the class summary: "each module `DbContext` builds its own from its module's `.Application.Configurations` assembly" (was `.Application`).

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~AggregateFeatureRegistryTests"`
Expected: PASS, all tests in the class.

- [ ] **Step 5: Commit**

```bash
git -C src/SharedKernel add -A && git -C src/SharedKernel commit -m "Add AggregateFeatureRegistry.ConfigurationTypesIn"
git add tests && git commit -m "Test AggregateFeatureRegistry.ConfigurationTypesIn"
```

---

### Task 3: Move the configurations and read them from `ConfigurationsAssembly`

Moves 13 + 2 DeviceManagement and 2 UserAccess configurations, points the `DbContext` and `AddSerginCore` at the new assemblies, and updates the tests that read Contracts for configurations.

**Files:**
- Move: 13 files `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/**/*CommandConfiguration.cs` to the same relative path under `…Application.Configurations/`
- Move: `…DeviceManagement.Application/Devices/DeviceAggregateFeatureConfiguration.cs`, `…DeviceManagement.Application/Manufacturers/ManufacturerAggregateFeatureConfiguration.cs` to `…Application.Configurations/Devices/`, `…/Manufacturers/`
- Move (UA): `Sergin.UserAccess.Application.Contracts/Users/Commands/GetList/GetUserListQueryCommandConfiguration.cs`, `…/GetOne/GetUserByIdQueryCommandConfiguration.cs` to `Sergin.UserAccess.Application.Configurations/Users/Commands/{GetList,GetOne}/`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/DeviceManagementDbContext.cs`
- Modify (SK): `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs:137-150,189-195`
- Modify (SK): `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/CommandConfigurationSource.cs` (summary), `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/SerginDbContext.cs:12` (doc comment)
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/ModuleCommandConfigurationTests.cs`, `tests/Sergin.MeterMinder.IntegrationTests.All/Devices/DeviceGrpcRoundTripTests.cs:186-189`

**Interfaces:**
- Consumes: `ISerginModule.ConfigurationsAssembly`, `ISerginRemoteModule.ConfigurationsAssembly`, both marker classes (Task 1).

- [ ] **Step 1: Write the failing tests**

In `ModuleCommandConfigurationTests`, replace the `Registry` sources and add one test. Usings: add `using Sergin.MeterMinder.DeviceManagement.Application;`, `using Sergin.MeterMinder.DeviceManagement.Application.Configurations;`, `using Sergin.SharedKernel.Application.Aggregates;`, `using Sergin.UserAccess.Application.Configurations;`.

```csharp
    private static CommandConfigurationRegistry Registry { get; } = CommandConfigurationRegistry.FromSources(
    [
        CommandConfigurationSource.FromAssembly(DeviceManagementApplicationConfigurationsAssemblyReference.Assembly),
        CommandConfigurationSource.FromAssembly(UserAccessApplicationConfigurationsAssemblyReference.Assembly),
    ]);

    [Fact]
    public void ContractsAndApplicationAssemblies_HoldNoConfigurations()
    {
        Assert.Empty(CommandConfigurationSource.FromAssembly(typeof(DeleteDeviceCommand).Assembly).ConfigurationTypes);
        Assert.Empty(CommandConfigurationSource.FromAssembly(typeof(GetUserByIdQueryCommand).Assembly).ConfigurationTypes);
        Assert.Empty(AggregateFeatureRegistry.ConfigurationTypesIn(DeviceManagementApplicationAssemblyReference.Assembly));
        Assert.Equal(2, AggregateFeatureRegistry.ConfigurationTypesIn(
            DeviceManagementApplicationConfigurationsAssemblyReference.Assembly).Count);
    }
```

Also update the class summary: "read straight from their configurations assemblies".

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~ModuleCommandConfigurationTests"`
Expected: FAIL. `DeleteDevice_NeedsDeletePermission_AndAVersion` fails because the registry is empty; `ContractsAndApplicationAssemblies_HoldNoConfigurations` fails on the first `Assert.Empty`.

- [ ] **Step 3: Move the DeviceManagement command configurations**

From the worktree root, in Git Bash:

```bash
cd src/Modules/DeviceManagement
src=Sergin.MeterMinder.DeviceManagement.Application.Contracts
dst=Sergin.MeterMinder.DeviceManagement.Application.Configurations
for f in $(cd "$src" && find . -name "*CommandConfiguration.cs"); do
  mkdir -p "$dst/$(dirname "$f")"
  git mv "$src/$f" "$dst/$f"
  old_ns=$(grep -m1 '^namespace ' "$dst/$f" | sed -E 's/^namespace (.*);/\1/')
  new_ns=${old_ns/.Application.Contracts/.Application.Configurations}
  sed -i -E "s/^namespace .*;/namespace $new_ns;/" "$dst/$f"
  sed -i "1i using $old_ns;" "$dst/$f"
done
git status --short | grep -c Configuration   # expect 13
cd ../../..
```

Each file now starts with `using <record namespace>;` then `using Sergin.SharedKernel.Application.Commands.Configuration;`. That order is alphabetical (`MeterMinder` < `SharedKernel`).

- [ ] **Step 4: Move the DeviceManagement aggregate configurations**

```bash
cd src/Modules/DeviceManagement
app=Sergin.MeterMinder.DeviceManagement.Application
dst=Sergin.MeterMinder.DeviceManagement.Application.Configurations
for f in Devices/DeviceAggregateFeatureConfiguration.cs Manufacturers/ManufacturerAggregateFeatureConfiguration.cs; do
  mkdir -p "$dst/$(dirname "$f")"
  git mv "$app/$f" "$dst/$f"
  sed -i -E "s/^namespace Sergin\.MeterMinder\.DeviceManagement\.Application\./namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations./" "$dst/$f"
done
cd ../../..
```

Expected namespaces: `Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices` and `….Configurations.Manufacturers`.

- [ ] **Step 5: Move the UserAccess command configurations (inside the submodule)**

```bash
cd src/Modules/UserAccess
src=Sergin.UserAccess.Application.Contracts
dst=Sergin.UserAccess.Application.Configurations
for f in $(cd "$src" && find . -name "*CommandConfiguration.cs"); do
  mkdir -p "$dst/$(dirname "$f")"
  git mv "$src/$f" "$dst/$f"
  old_ns=$(grep -m1 '^namespace ' "$dst/$f" | sed -E 's/^namespace (.*);/\1/')
  new_ns=${old_ns/.Application.Contracts/.Application.Configurations}
  sed -i -E "s/^namespace .*;/namespace $new_ns;/" "$dst/$f"
  sed -i "1i using $old_ns;" "$dst/$f"
done
git status --short   # expect 2 renames
cd ../../..
```

If any moved file shows a `using` that sorts after `Sergin.SharedKernel…` (IDE0065 or Sonar complains), reorder alphabetically.

- [ ] **Step 6: Point the DbContext at the new marker**

`DeviceManagementDbContext.cs`: replace `using Sergin.MeterMinder.DeviceManagement.Application;` with `using Sergin.MeterMinder.DeviceManagement.Application.Configurations;` (keep the old using only if something else in the file still needs it; the build will say), and:

```csharp
    protected override AggregateFeatureRegistry AggregateFeatures =>
        AggregateFeatureRegistry.FromAssemblies([DeviceManagementApplicationConfigurationsAssemblyReference.Assembly]);
```

- [ ] **Step 7: Read both registries from `ConfigurationsAssembly` (SK)**

In `SerginCoreExtensions.AddSerginCore`, replace the command configuration source block (currently lines 137-150) with:

```csharp
        // Every request's declared policy (ICommandConfiguration<T>), read by the permission and expected-version
        // behaviors. ConfigurationsAssembly, for remote modules too: a gateway must refuse a forbidden remote call
        // before the gRPC hop, and a remote module ships its configurations alongside its contracts. Built from
        // sources rather than here, so a test host can add its own request types; the Use…Async bootstraps
        // resolve it so a bad declaration fails host start, not the first send.
        foreach (ISerginModule module in localModules)
        {
            builder.Services.AddSingleton(CommandConfigurationSource.FromAssembly(module.ConfigurationsAssembly));
        }

        foreach (ISerginRemoteModule remoteModule in remoteModules)
        {
            builder.Services.AddSingleton(CommandConfigurationSource.FromAssembly(remoteModule.ConfigurationsAssembly));
        }
```

and the aggregate registry registration (currently lines 194-195) with:

```csharp
        builder.Services.AddSingleton(
            AggregateFeatureRegistry.FromAssemblies(localModules.Select(module => module.ConfigurationsAssembly)));
```

Leave the old "found in ApplicationAssembly" guard in place for now; Task 4 replaces it. It still passes, because `.Application` no longer holds any configuration.

- [ ] **Step 8: Fix stale doc comments (SK)**

- `CommandConfigurationSource` summary: "`AddSerginCore` adds one per module ConfigurationsAssembly" (was ContractsAssembly).
- `SerginDbContext.cs` line 12: `AggregateFeatureRegistry.FromAssemblies([&lt;Module&gt;ApplicationConfigurationsAssemblyReference.Assembly])`.
- Run `grep -rn "ContractsAssembly\|ApplicationAssembly\|Application\.Contracts" src/SharedKernel --include=*.cs` and correct any remaining comment that says configurations live in Contracts or Application. Leave comments about integration events, MediatR, validators and translators alone.

- [ ] **Step 9: Point the gRPC gateway test at the Configurations assembly**

`DeviceGrpcRoundTripTests.cs`, in `BuildRemoteProvider`, add `using Sergin.MeterMinder.DeviceManagement.Application.Configurations;` and:

```csharp
        // What AddSerginCore registers for a remote module: its ConfigurationsAssembly's declarations, so the
        // gateway's permission check refuses before the gRPC hop.
        services.AddSingleton(CommandConfigurationRegistry.FromSources(
            [CommandConfigurationSource.FromAssembly(DeviceManagementApplicationConfigurationsAssemblyReference.Assembly)]));
```

- [ ] **Step 10: Build and run the affected tests**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: `Build succeeded`, 0 warnings.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~ModuleCommandConfigurationTests|FullyQualifiedName~DeviceGrpcRoundTripTests|FullyQualifiedName~Aggregates|FullyQualifiedName~Commands"`
Expected: PASS, all.

- [ ] **Step 11: Commit, per repo**

```bash
git -C src/SharedKernel add -A && git -C src/SharedKernel commit -m "Read command and aggregate configurations from ConfigurationsAssembly"
git -C src/Modules/UserAccess add -A && git -C src/Modules/UserAccess commit -m "Move command configurations to Application.Configurations"
git add src/Modules/DeviceManagement tests && git commit -m "Move DeviceManagement configurations to Application.Configurations"
```

---

### Task 4: One placement guard for both configuration kinds

Replaces the "command configuration in ApplicationAssembly" check with one that covers both kinds and every non-configurations module assembly, local and remote.

**Files:**
- Modify (SK): `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs:67-82` (and a new private method)
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/CommandConfigurationPlacementTests.cs` (rewrite)

**Interfaces:**
- Consumes: `AggregateFeatureRegistry.ConfigurationTypesIn(Assembly)` (Task 2), `CommandConfigurationSource.FromAssembly(Assembly).ConfigurationTypes`, `ISerginModule.ConfigurationsAssembly`, `ISerginRemoteModule.ConfigurationsAssembly` (Task 1).

- [ ] **Step 1: Write the failing tests**

Replace the whole of `CommandConfigurationPlacementTests.cs` with:

```csharp
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.MeterMinder.IntegrationTests.All.Aggregates;
using Sergin.SharedKernel.Modules;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands;

/// <summary>
/// Command and aggregate feature configurations are read from a module's ConfigurationsAssembly only, so one
/// written in its Application or Contracts assembly would be ignored. AddSerginCore refuses that, for local
/// and remote modules alike. This test assembly holds configurations of both kinds, which makes it the
/// misplaced assembly in every case below.
/// </summary>
public sealed class CommandConfigurationPlacementTests
{
    private static readonly Assembly MisplacedAssembly = typeof(CommandConfigurationPlacementTests).Assembly;
    private static readonly Assembly DmApplication = DeviceManagementApplicationAssemblyReference.Assembly;
    private static readonly Assembly DmContracts = typeof(DeleteDeviceCommand).Assembly;
    private static readonly Assembly DmConfigurations = DeviceManagementApplicationConfigurationsAssemblyReference.Assembly;

    private static readonly string CommandConfiguration = typeof(ConfiguredTestCommandConfiguration).FullName!;
    private static readonly string AggregateConfiguration = typeof(UnmappedEntityAggregateFeatureConfiguration).FullName!;

    [Fact]
    public void ConfigurationInTheApplicationAssembly_StopsComposition_NamingBothKinds()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CreateBuilder().AddSerginCore([new PlacementModule(MisplacedAssemblyAssembly, DmContracts, DmConfigurations)]));

        Assert.Contains(CommandConfiguration, error.Message, StringComparison.Ordinal);
        Assert.Contains(AggregateConfiguration, error.Message, StringComparison.Ordinal);
        Assert.Contains(".Application.Configurations", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationInTheContractsAssembly_StopsComposition()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CreateBuilder().AddSerginCore([new PlacementModule(DmApplication, MisplacedAssembly, DmConfigurations)]));

        Assert.Contains(CommandConfiguration, error.Message, StringComparison.Ordinal);
        Assert.Contains(AggregateConfiguration, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAssemblyScannedTwice_NamesEachTypeOnce()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CreateBuilder().AddSerginCore([new PlacementModule(MisplacedAssemblyAssembly, MisplacedAssembly, DmConfigurations)]));

        Assert.Equal(1, error.Message.Split(CommandConfiguration).Length - 1);
    }

    [Fact]
    public void RemoteModuleConfigurationInContracts_StopsComposition()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CreateBuilder().AddSerginCore([], [new PlacementRemoteModule(MisplacedAssemblyAssembly, DmConfigurations)]));

        Assert.Contains(CommandConfiguration, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationsAssemblyAlsoContracts_Composes()
    {
        IConfigurationSection section =
            CreateBuilder().AddSerginCore([new PlacementModule(DmApplication, Configurations, DmConfigurations)]);

        Assert.NotNull(section);
    }

    [Fact]
    public void TheRealModules_Compose() =>
        Assert.NotNull(CreateBuilder().AddSerginCore([new PlacementModule(DmApplication, DmContracts, DmConfigurations)]));

    private static HostApplicationBuilder CreateBuilder()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sergin:ApplicationName"] = "Test",
            ["Sergin:ConnectionStrings:Database"] = "Host=localhost;Database=placement",
        });
        return builder;
    }

    private sealed class PlacementModule(Assembly application, Assembly contracts, Assembly configurations) : ISerginModule
    {
        public string Schema => "placement";

        public Assembly ApplicationAssembly => application;

        public Assembly ContractsAssembly => contracts;

        public Assembly ConfigurationsAssembly => configurations;

        public void AddServices(IServiceCollection services, IConfigurationSection configuration)
        {
        }

        public Task MigrateAsync(IServiceProvider services) => Task.CompletedTask;
    }

    private sealed class PlacementRemoteModule(Assembly contracts, Assembly configurations) : ISerginRemoteModule
    {
        public string Schema => "placement";

        public Assembly ContractsAssembly => contracts;

        public Assembly ConfigurationsAssembly => configurations;

        public void AddRemoteServices(IServiceCollection services, IConfigurationSection configuration)
        {
        }
    }
}
```

Check that `UnmappedEntityAggregateFeatureConfiguration` lives in namespace `Sergin.MeterMinder.IntegrationTests.All.Aggregates` (`Aggregates/AggregateFeatureConfigurationTestTypes.cs`). The remote-module test passes an empty `localModules`; that is safe because the guard runs before `AddMediatR`.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~CommandConfigurationPlacementTests"`
Expected: FAIL in `ConfigurationInTheApplicationAssembly_StopsComposition_NamingBothKinds` (message lacks the aggregate configuration and says `.Application.Contracts`), `ConfigurationInTheContractsAssembly_StopsComposition` and `RemoteModuleConfigurationInContracts_StopsComposition` (no exception). The two `Compose` tests may already pass.

- [ ] **Step 3: Implement the guard (SK)**

In `SerginCoreExtensions.AddSerginCore`, replace the `misplacedConfigurations` block (the comment at line 67 through the closing brace of its `if`) with:

```csharp
        EnsureConfigurationsArePlaced(localModules, remoteModules);
```

Add this private method to the class, next to `AddClosedGenericImplementations`:

```csharp
    /// <summary>
    /// Command and aggregate feature configurations are read from a module's ConfigurationsAssembly only. One
    /// written in its Application or Contracts assembly would be ignored: its request would run with no
    /// permission or version check, or its aggregate would lose its features. An assembly that is also the
    /// module's ConfigurationsAssembly is not scanned, so such a module composes; it is not a supported shape.
    /// </summary>
    private static void EnsureConfigurationsArePlaced(
        IReadOnlyCollection<ISerginModule> localModules, IReadOnlyCollection<ISerginRemoteModule> remoteModules)
    {
        Assembly[] scanned =
        [
            .. localModules.SelectMany(module => new[] { module.ApplicationAssembly, module.ContractsAssembly }
                .Where(assembly => assembly != module.ConfigurationsAssembly)),
            .. remoteModules
                .Where(module => module.ContractsAssembly != module.ConfigurationsAssembly)
                .Select(module => module.ContractsAssembly),
        ];

        string[] misplaced =
        [
            .. scanned.Distinct()
                .SelectMany(assembly => CommandConfigurationSource.FromAssembly(assembly).ConfigurationTypes
                    .Concat(AggregateFeatureRegistry.ConfigurationTypesIn(assembly)))
                .Select(type => type.FullName!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        if (misplaced.Length > 0)
        {
            throw new InvalidOperationException(
                $"Configuration(s) found outside a module's ConfigurationsAssembly: {string.Join(", ", misplaced)}. "
                + "Only ConfigurationsAssembly is read, so these would be ignored: a request left unprotected, or an "
                + "aggregate without its features. Move each to the module's .Application.Configurations project.");
        }
    }
```

If an analyzer objects (for example to the `new[] { … }` array or `!=` on `Assembly`), keep the behavior and adapt the form: `[module.ApplicationAssembly, module.ContractsAssembly]` as a collection expression typed `Assembly[]`, or `!assembly.Equals(module.ConfigurationsAssembly)`.

- [ ] **Step 4: Run the tests**

Run: `dotnet build Sergin.MeterMinder.slnx` — expected `Build succeeded`, 0 warnings.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~CommandConfigurationPlacementTests|FullyQualifiedName~OutboxTransportSeamTests|FullyQualifiedName~CommandConfigurationHostTests"`
Expected: PASS, all.

- [ ] **Step 5: Commit**

```bash
git -C src/SharedKernel add -A && git -C src/SharedKernel commit -m "Refuse a configuration outside a module's ConfigurationsAssembly"
git add tests && git commit -m "Cover the configuration placement guard for both kinds and remote modules"
```

---

### Task 5: Full verification and documentation

**Files:**
- Modify: `.claude/CLAUDE.md` (root)
- Modify: `src/Modules/DeviceManagement/CLAUDE.md`
- Modify: `.claude/skills/add-feature/SKILL.md`, `.claude/skills/add-module/SKILL.md`
- Modify (SK): `src/SharedKernel/.claude/CLAUDE.md`, `src/SharedKernel/README.md`
- Modify (UA): `src/Modules/UserAccess/.claude/CLAUDE.md`, `src/Modules/UserAccess/.claude/skills/add-feature/SKILL.md`

- [ ] **Step 1: Run the whole suite**

Run: `docker info` (start Docker Desktop if it fails), then
`dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`
Expected: every test passes. Record the pass count in the final report.

- [ ] **Step 2: Probe the EF model for both modules**

```bash
dotnet ef migrations add Probe \
  --project src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data \
  --startup-project src/Hosts/Sergin.MeterMinder.Hosts.All
dotnet ef migrations add Probe \
  --project src/Modules/UserAccess/Sergin.UserAccess.Infrastructure.Data \
  --startup-project src/Hosts/Sergin.MeterMinder.Hosts.All
```

Expected: each `*_Probe.cs` has empty `Up` and `Down` bodies. If either body is not empty, stop and report: the model changed, which the spec forbids.
Then delete the generated `*_Probe.cs` and `*_Probe.Designer.cs` files in both `Migrations` folders and restore the snapshots:

```bash
git checkout -- src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data
git -C src/Modules/UserAccess checkout -- Sergin.UserAccess.Infrastructure.Data
git status --short   # no Probe files, no snapshot changes
```

- [ ] **Step 3: Update the root `.claude/CLAUDE.md`**

Make these edits; keep the surrounding prose as is:

- "Host / module composition", the `ISerginModule` core contract list: `Schema`, `ApplicationAssembly`, `ContractsAssembly`, **`ConfigurationsAssembly`**, `AddServices`, `MigrateAsync`.
- The `ApplicationAssembly` vs. `ContractsAssembly` paragraph: add one sentence: "`ConfigurationsAssembly` points at a third project, `.Application.Configurations`, holding the module's command configurations and aggregate feature configurations; `AddSerginCore` builds both registries from it, for remote modules too."
- "Per-module project layering": add a bullet after `.Application.Contracts`:
  "**`.Application.Configurations`** — every `<Record>Configuration : ICommandConfiguration<Record>` and every `<Root>AggregateFeatureConfiguration : IAggregateFeatureConfiguration<Root>` of the module, `internal sealed`, in folders mirroring the record's or root's own (`Devices/Commands/Delete/DeleteDeviceCommandConfiguration.cs`, `Devices/DeviceAggregateFeatureConfiguration.cs`). References `.Application.Contracts` only. Carries `<Module>ApplicationConfigurationsAssemblyReference` for `ISerginModule.ConfigurationsAssembly` and the `DbContext`'s `AggregateFeatures` override. `.Infrastructure.Data` and the composition root reference it; `.Application` and the presentation projects do not."
- `.Application.Contracts` bullet: replace "each record's policy sits beside it in a `<RecordName>Configuration` class" with "its policy lives in `.Application.Configurations`, never here".
- `.Application` bullet: delete the sentence "It also holds each configured aggregate's `<Root>AggregateFeatureConfiguration.cs`…".
- "Request policy" bullet: the configuration lives "in the module's `.Application.Configurations`, in the folder mirroring the record's"; `AddSerginCore` "builds one `CommandConfigurationRegistry` from every local and remote `ConfigurationsAssembly`"; replace "`AddSerginCore` refuses a configuration found in a module's `ApplicationAssembly` — only Contracts is read…" with "`AddSerginCore` refuses any command or aggregate configuration found in a module's Application or Contracts assembly (local or remote), naming each type".
- "Aggregate configuration" bullet: the configuration "lives in the module's `.Application.Configurations` project, in the root's folder"; the `DbContext` override becomes `AggregateFeatureRegistry.FromAssemblies([DeviceManagementApplicationConfigurationsAssemblyReference.Assembly])`.
- Test list under Commands: mention `CommandConfigurationPlacementTests` (both kinds, local and remote) and `ModuleConfigurationsAssemblyTests`.
- Design link: add `docs/superpowers/specs/2026-10-03-application-configurations-project-design.md` to the Request policy bullet.

- [ ] **Step 4: Update `src/Modules/DeviceManagement/CLAUDE.md`**

Run `grep -n "Configuration\|Contracts" src/Modules/DeviceManagement/CLAUDE.md`. Every statement that a configuration lives in `.Application.Contracts` or `.Application` now says `.Application.Configurations`, with the same relative folder.

- [ ] **Step 5: Update the skills**

- `.claude/skills/add-feature/SKILL.md`: in the query section (line ~32) and template (lines ~33-49), the configuration goes in `src/Modules/<Module>/Sergin.<Module>.Application.Configurations/<Aggregate>[/<ChildEntity>]/Commands/<Feature>/<RecordName>Configuration.cs`, namespace `Sergin.<Module>.Application.Configurations.<…>.Commands.<Feature>`, with a `using` of the record's Contracts namespace. Fix the template's namespace line to `namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices.Commands.Update;` and add `using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Update;` above the SharedKernel using.
- `.claude/skills/add-module/SKILL.md`: add the `.Application.Configurations` project to the project list it scaffolds (csproj referencing `.Application.Contracts`, marker class, `.slnx` line after Contracts, references from `.Infrastructure.Data` and the composition root) and `ConfigurationsAssembly` to the `<Module>Module` template.
- `src/Modules/UserAccess/.claude/skills/add-feature/SKILL.md` line ~27: same change as the root `add-feature`.

- [ ] **Step 6: Update SharedKernel docs**

`src/SharedKernel/.claude/CLAUDE.md`:
- `Sergin.SharedKernel.Modules` entry: `ISerginModule` is `Schema`, `ApplicationAssembly`, `ContractsAssembly`, `ConfigurationsAssembly`, `AddServices`, `MigrateAsync`; `ISerginRemoteModule` is `Schema`, `ContractsAssembly`, `ConfigurationsAssembly`, `AddRemoteServices`.
- Command configuration paragraph: "a module writes one `internal sealed class <RecordName>Configuration` in its `.Application.Configurations`"; "`AddSerginCore` first refuses any command or aggregate configuration found in a module's Application or Contracts assembly, then registers one source per local and remote module `ConfigurationsAssembly`".
- `Aggregates/` paragraph: discovery is "by scanning `ConfigurationsAssembly`"; add `ConfigurationTypesIn(Assembly)` to the registry's members.
- `Hosts` entry: same placement-guard and `ConfigurationsAssembly` wording.

`src/SharedKernel/README.md`: `grep -n "ContractsAssembly\|Configuration" src/SharedKernel/README.md` and apply the same wording where it describes where configurations are read from.

- [ ] **Step 7: Update UserAccess docs**

`src/Modules/UserAccess/.claude/CLAUDE.md`: `grep -n "Configuration\|Contracts"`; the two user query configurations live in `Sergin.UserAccess.Application.Configurations/Users/Commands/{GetOne,GetList}/`; the module's project list gains `.Application.Configurations`; `UserAccessModule` exposes `ConfigurationsAssembly`.

- [ ] **Step 8: Rebuild the graph (host)**

```bash
graphify update . && python .claude/skills/graphify/scripts/graphify_repair.py
```

Skip if `graphify` is not installed; say so in the report.

- [ ] **Step 9: Commit, per repo**

```bash
git -C src/SharedKernel add -A && git -C src/SharedKernel commit -m "Document ConfigurationsAssembly and the widened placement guard"
git -C src/Modules/UserAccess add -A && git -C src/Modules/UserAccess commit -m "Document Application.Configurations"
git add .claude src/Modules/DeviceManagement/CLAUDE.md && git commit -m "Document the Application.Configurations project"
```

---

### Task 6: Integrate across the three repos

Outward-facing: **ask the user before each push and each PR.**

- [ ] **Step 1: Final build and test from a clean state**

```bash
dotnet build Sergin.MeterMinder.slnx
dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj
```

Expected: `Build succeeded`, all tests pass.

- [ ] **Step 2: With the user's go-ahead, push and open the SharedKernel PR**

```bash
git -C src/SharedKernel push -u origin feat/application-configurations
gh pr create --repo poursh/Sergin.SharedKernel --head feat/application-configurations --title "ConfigurationsAssembly: read command and aggregate configurations from Application.Configurations" --body-file <body>
```

The body says the change is breaking for every `ISerginModule` implementer and links the UserAccess and MeterMinder PRs once they exist.

- [ ] **Step 3: With the user's go-ahead, push and open the UserAccess PR**

```bash
git -C src/Modules/UserAccess push -u origin feat/application-configurations
gh pr create --repo poursh/Sergin.UserAccess --head feat/application-configurations --title "Add Sergin.UserAccess.Application.Configurations" --body-file <body>
```

- [ ] **Step 4: After both merge, bump the submodule pointers to their merged `main`**

PRs in these repos are squash-merged, so the branch commits are not what `main` points to.

```bash
git -C src/SharedKernel fetch && git -C src/SharedKernel switch --detach origin/main
git -C src/Modules/UserAccess fetch && git -C src/Modules/UserAccess switch --detach origin/main
dotnet build Sergin.MeterMinder.slnx && dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj
git add src/SharedKernel src/Modules/UserAccess && git commit -m "Bump SharedKernel and UserAccess for Application.Configurations"
```

- [ ] **Step 5: With the user's go-ahead, push and open the MeterMinder PR**

```bash
git push -u origin feat/application-configurations
gh pr create --title "Application.Configurations project per module" --body-file <body>
```

After merge, remove the worktree and branch (`git worktree remove ../Sergin.MeterMinder-appconfig`, `git branch -d feat/application-configurations`).
