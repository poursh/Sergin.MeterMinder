# Permission Catalogue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permissions become `PermissionDefinition` objects defined per aggregate, registered in each aggregate feature configuration, collected into a grouped `PermissionCatalog`, and referenced by every use site instead of a string name.

**Architecture:** Expand, migrate, contract. Tasks 1–3 add the new types and overloads next to the old ones (SharedKernel), tasks 4–5 move UserAccess and DeviceManagement onto them, task 6 removes the string/`Permission` overloads and switches on the two use-site refusals, task 7 updates docs. Every task leaves `dotnet build Sergin.MeterMinder.slnx` and the test suite green.

**Tech Stack:** .NET 10, MediatR, EF Core, MudBlazor 9.9.0, xUnit + Testcontainers.PostgreSql.

**Spec:** `docs/superpowers/specs/2026-10-20-permission-catalogue-design.md`

## Global Constraints

- `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer: every analyzer warning fails the build. File-scoped namespaces (IDE0161).
- Central Package Management: no `Version` on `PackageReference`. No new packages are needed.
- The only place a permission name is a string literal: `PermissionDefinition.Create(name, label)`. No implicit conversion from `string` to `PermissionDefinition`.
- Definitions: `public static class <Aggregate>Permissions` in `.Application.Contracts/<Aggregate>/`, members `public static PermissionDefinition X { get; } = PermissionDefinition.Create(...)`.
- Registration: `.Permissions(PermissionDefinition, params PermissionDefinition[])` on `AggregateFeatureBuilder<TAggregateRoot>`, in `.Application.Configurations`. Not an EF feature: no migration.
- `IScopedPermissionChecker`, stored grants, `GrantRolePermissionCommand`, `RevokeRolePermissionCommand`, `CreateRoleCommand` keep `Permission`.
- Schema `sys` is reserved for SharedKernel (`PermissionDefinition.AllPlatform`).
- Git: work in worktree `C:\@factory\Sergin\Sergin.MeterMinder-permission-catalogue`, branch `spec/permission-catalogue` (rename to `feature/permission-catalogue` before task 1). Run `git submodule update --init --recursive` there first. In each submodule create branch `feature/permission-catalogue`; SharedKernel and UserAccess changes are committed inside the submodule, then the host commits the pointer. **No `Co-Authored-By` trailer** (project CLAUDE.md). Push nothing without asking.
- Tests need Docker: launch Docker Desktop and poll `docker info` before `dotnet test`.
- Test command (all tasks): `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~<Class>"`; unit tests: `dotnet test tests/Sergin.UserAccess.UnitTests`.

## Review Focus

1. **A role holds a grant whose permission is no longer defined** (code removed it). The role detail page must still list it, under an "Unknown" group with the raw name, and it must be revocable. Owner: task 4 (`RoleDetailPage_ListsAGrantMissingFromTheCatalogue`, `Revoke_OfAPermissionMissingFromTheCatalogue_Succeeds`).
2. **Granting `permission.sys.platform-all` through the role editor** must keep working once the validator checks the catalogue. Owner: task 4 (`Grant_OfAllPlatform_Succeeds`).
3. **A malformed name in a definition** throws inside a static initializer, which surfaces as `TypeInitializationException`. Startup must fail with a message naming the definitions class and the bad value, not a bare type-initializer error. Owner: task 2 (`Catalog_WithAMalformedDefinition_FailsNamingTheClass`).
4. **A test host's `CommandConfigurationSource.FromTypes(...)`** carries no schema; the schema refusal must skip it, not fail every test host. Owner: task 2 (`Catalog_FromTypesSource_SkipsTheSchemaCheck`).
5. **A permission used by a handler but not registered** cannot be found at startup. It must be denied and logged, never passed. Owner: task 6 (`CurrentUserHasAsync_OfAnUnregisteredDefinition_Denies`).

---

### Task 1: `PermissionDefinition`

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Domain/Securities/PermissionDefinition.cs`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Permissions/PermissionDefinitionTests.cs` (plain `[Fact]`s, no collection fixture)

**Interfaces:**
- Produces: `public sealed class PermissionDefinition : IEquatable<PermissionDefinition>` with `Permission Permission { get; }`, `string Label { get; }`, `string Schema { get; }` (second segment of the name), `static PermissionDefinition Create(string name, string label)`, `static PermissionDefinition AllPlatform { get; }` (name `permission.sys.platform-all`, label `"Everything, everywhere"`), `ToString()` returns the name. Equality and hash by `Permission.Value`.

- [ ] **Step 1: Write failing tests**
  - `Create_KeepsTheNameAndLabel`: `Create("permission.dm.devices.read", "View devices")` → `Permission.Value == "permission.dm.devices.read"`, `Label == "View devices"`, `Schema == "dm"`.
  - `Create_WithAMalformedName_Throws`: `Create("devices.read", "x")` → `ArgumentException`.
  - `Create_WithABlankLabel_Throws`: `Create("permission.dm.devices.read", " ")` → `ArgumentException`.
  - `Equality_IsByName`: two `Create` calls with the same name, different labels → `Equals` true, same hash.
  - `AllPlatform_IsTheSysPermission`: `AllPlatform.Permission == Permission.AllPlatform`, `Schema == "sys"`.
- [ ] **Step 2:** Run `--filter FullyQualifiedName~PermissionDefinitionTests`. Expected: build fails, `PermissionDefinition` not found.
- [ ] **Step 3:** Implement. Name through `Permission.Create`; label through `Guard.Against.NullOrWhiteSpace`.
- [ ] **Step 4:** Run the same filter. Expected: 5 passed.
- [ ] **Step 5:** Commit in `src/SharedKernel` (`Add PermissionDefinition`), then the pointer in the host with the test.

### Task 2: Registration and `PermissionCatalog`

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Aggregates/AggregateFeatureBuilder.cs` (add `Permissions(...)`; the non-generic half gets `internal List<PermissionDefinition> PermissionDefinitions`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Aggregates/AggregateFeatureRegistry.cs` (`Declaration` carries the list; add `PermissionsOf`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/CommandConfigurationSource.cs` (add `string? Schema`; `FromAssembly(Assembly assembly, string? schema = null)`; `FromTypes` leaves it null)
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Securities/Authorization/PermissionCatalog.cs`, `PermissionCatalogEntry.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs:128-143` (pass `module.Schema`/`remoteModule.Schema` to `FromAssembly`; register `PermissionCatalog` singleton from all sources)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs:119` and the WebApi bootstrap's matching line (resolve `PermissionCatalog` at start beside `CommandConfigurationRegistry`)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Permissions/PermissionCatalogTests.cs`, `PermissionCatalogTestTypes.cs`

**Interfaces:**
- Consumes: `PermissionDefinition` (task 1).
- Produces:
  - `AggregateFeatureBuilder<T> Permissions(PermissionDefinition permission, params PermissionDefinition[] more)`; repeated definition counts once.
  - `IReadOnlyCollection<PermissionDefinition> AggregateFeatureRegistry.PermissionsOf(Type aggregateRoot)`; empty for an unconfigured root.
  - `public sealed record PermissionCatalogEntry(PermissionDefinition Definition, Type? AggregateRoot)`; `AggregateRoot` null only for `AllPlatform`.
  - `public sealed class PermissionCatalog` with `static PermissionCatalog FromSources(IEnumerable<CommandConfigurationSource> sources)`, `static PermissionCatalog Empty`, `IReadOnlyCollection<PermissionCatalogEntry> Entries` (always includes `AllPlatform`), `bool Contains(PermissionDefinition)`, `bool TryGet(Permission, [NotNullWhen(true)] out PermissionDefinition?)`, `IReadOnlyList<PermissionCatalogModule> Grouped()`.
  - `public sealed record PermissionCatalogModule(string Schema, IReadOnlyList<PermissionCatalogGroup> Groups)`; `public sealed record PermissionCatalogGroup(string Name, IReadOnlyList<PermissionDefinition> Permissions)`. `Name` is the root's `Type.Name`, `"Platform"` for `AllPlatform`. Ordering: schema ordinal, group name ordinal, permission name ordinal.
  - Refusals in `FromSources`, each an `InvalidOperationException`:
    1. same name from two roots, or from one root via two definition objects with different labels — message names both roots and the name;
    2. a source with non-null `Schema` registering a definition whose `Schema` differs, or any registration of schema `sys` — message names the root, the permission and the expected schema;
    3. a `TypeInitializationException` while running a configuration — rethrown naming the configuration type and the inner `ArgumentException` message.

- [ ] **Step 1: Write failing tests** (test types: `CatalogTestRoot`, `OtherCatalogTestRoot`, `CatalogTestPermissions` with `Read`/`Update` under schema `test`, and configurations for each case)
  - `Catalog_ListsWhatEachAggregateRegistered`: source `FromTypes(CatalogTestRootConfiguration)` → `Entries` holds `test` Read and Update with `AggregateRoot == typeof(CatalogTestRoot)`, plus `AllPlatform`.
  - `Catalog_GroupsByModuleThenAggregate`: `Grouped()` → modules `["sys", "test"]`; `test` has one group `"CatalogTestRoot"` with names ordered.
  - `Catalog_SamePermissionFromTwoAggregates_Fails`: message contains both root names and `permission.test.things.read`.
  - `Catalog_PermissionOutsideTheModuleSchema_Fails`: `FromAssembly`-shaped source built with `schema: "dm"` registering a `test` permission → message contains `permission.test.things.read` and `dm`.
  - `Catalog_FromTypesSource_SkipsTheSchemaCheck`: same registration via `FromTypes` → builds.
  - `Catalog_RegisteringSys_Fails`.
  - `Catalog_WithAMalformedDefinition_FailsNamingTheClass`: configuration touching `MalformedCatalogPermissions.Bad` (`Create("bad", "x")`) → message contains `MalformedCatalogPermissions` and the configuration name.
  - `TryGet_FindsByName_AndMissesAnUnknownOne`.
  - `RealHost_CatalogHoldsAllPlatform`: resolve `PermissionCatalog` from `SerginWebApiFactory<Program>` services → contains `PermissionDefinition.AllPlatform` (no module registrations yet).
- [ ] **Step 2:** Run `--filter FullyQualifiedName~PermissionCatalogTests`. Expected: build fails, `PermissionCatalog`/`Permissions` not found.
- [ ] **Step 3:** Implement. `FromSources` builds one `AggregateFeatureRegistry` per source (so each registration knows its source's schema) and unions them.
- [ ] **Step 4:** Run the filter, then the whole suite. Expected: all pass.
- [ ] **Step 5:** Commit in `src/SharedKernel` (`Register permissions per aggregate and build the permission catalogue`), then pointer + tests in host.

### Task 3: Definition overloads (additive)

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/CommandConfigurationBuilder.cs` (add overload; the builder stores `PermissionDefinition`s alongside `Permission`s)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Securities/Authorization/IScopeAuthorization.cs`, `ScopeAuthorization.cs` (add four definition overloads forwarding to the `Permission` ones)
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Authorization/ScopedRbacAcceptanceTests.cs` (its failing `IScopeAuthorization` fake implements the new members)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/CommandConfigurationRegistryTests.cs`

**Interfaces:**
- Consumes: `PermissionDefinition`.
- Produces: `CommandConfigurationBuilder<T> RequirePermissions(PermissionDefinition permission, params PermissionDefinition[] more)`; `IScopeAuthorization.CurrentUserHasAsync(PermissionDefinition, Guid, CancellationToken)`, `CurrentUserHasAsync(PermissionDefinition, CancellationToken)`, `CurrentUserReachAsync(PermissionDefinition, CancellationToken)`, `CurrentUserHoldsAnywhereAsync(PermissionDefinition, CancellationToken)`. `CommandSettings.RequiredPermissions` stays `IReadOnlyCollection<Permission>` until task 6 (definitions map through `.Permission`).

- [ ] **Step 1:** Test `RequirePermissions_WithDefinitions_IsReadAsTheirPermissions`: a configuration requiring `CatalogTestPermissions.Read` → `For(request).RequiredPermissions` equals `[Read.Permission]`.
- [ ] **Step 2:** Run `--filter FullyQualifiedName~CommandConfigurationRegistryTests`. Expected: build fails, no overload.
- [ ] **Step 3:** Implement both overload sets.
- [ ] **Step 4:** Run the filter and the whole suite. Expected: all pass.
- [ ] **Step 5:** Commit in `src/SharedKernel` (`Accept PermissionDefinition in configurations and scope checks`), then pointer in host.

### Task 4: UserAccess onto definitions

**Files:**
- Create in `src/Modules/UserAccess/Sergin.UserAccess.Application.Contracts/`: `Users/UserPermissions.cs` (`Read` "View users", `Manage` "Create and deactivate users", `AssignmentsManage` "Assign and revoke roles"; names unchanged from `UserAccessPermissions`), `Scopes/ScopePermissions.cs` (`Read` "View scopes", `Manage` "Create, rename, move and delete scopes"), `Roles/RolePermissions.cs` (`Read` "View roles", `Manage` "Create and edit roles").
- Delete: `Sergin.UserAccess.Application.Contracts/UserAccessPermissions.cs`.
- Create: `Sergin.UserAccess.Application.Configurations/Users/UserAggregateFeatureConfiguration.cs` (`builder.Permissions(UserPermissions.Read, UserPermissions.Manage, UserPermissions.AssignmentsManage)`).
- Modify: `Scopes/ScopeAggregateFeatureConfiguration.cs`, `Roles/RoleAggregateFeatureConfiguration.cs` (chain `.Permissions(...)`).
- Modify: every `Sergin.UserAccess.Application.Configurations/**/*Configuration.cs` using `UserAccessPermissions` (6 files); every handler listed by `grep -rl UserAccessPermissions src/Modules/UserAccess` (19 files) plus `Users/UserReachGuard.cs`, `Authorization/ScopeDirectory.cs`; `Presentation.Blazor/UserAccessNavigation.cs` keeps a string for now: `UserPermissions.Read.Permission.Value` (typed in task 6).
- Modify: `Sergin.UserAccess.Application/Roles/Commands/Create/CreateRoleCommandValidator.cs`, `GrantPermission/GrantRolePermissionCommandValidator.cs` (inject `PermissionCatalog`; rule: each permission `catalog.TryGet(...)`, message `'{PropertyName}' must be a permission the platform defines.`). `RevokeRolePermissionCommandValidator` unchanged.
- Modify: `Presentation.Blazor/Roles/Pages/CreateRolePage.razor(.cs)`, `RoleDetailPage.razor(.cs)`: inject `PermissionCatalog` instead of `CommandConfigurationRegistry`; `MudSelect` renders `MudListSubheader` per module (`Permissions.<schema>` through `ILocalizer`) and per group, items show `Label` with the name as secondary text. Granted permissions render with their label; one `catalog.TryGet` misses renders under an "Unknown" subheader with the raw name.
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Securities/Authorization/PermissionCatalog.cs` only if a test below needs it (it should not).
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Permissions/UserAccessPermissionCatalogTests.cs`, `Authorization/ScopedRbacRuleTests.cs`, `Shell/ScopedRbacPageRenderingTests.cs`, `Commands/ModuleCommandConfigurationTests.cs`.

**Interfaces:**
- Consumes: tasks 2–3 APIs.
- Produces: `UserPermissions`, `ScopePermissions`, `RolePermissions` (namespaces `Sergin.UserAccess.Application.Contracts.Users|Scopes|Roles`), used by task 6 and tests.

- [ ] **Step 1: Write failing tests**
  - `Catalog_HoldsEveryUserAccessPermission`: host catalogue contains all seven UA definitions, `ScopePermissions.Manage` among them, grouped under schema `ua`, groups `Role`, `Scope`, `User`.
  - `Grant_OfAPermissionMissingFromTheCatalogue_IsAValidationError`: grant `Permission.Create("permission.ua.nothing.here")` → `ErrorType.Validation`, code `Permission`.
  - `Grant_OfAllPlatform_Succeeds`.
  - `Revoke_OfAPermissionMissingFromTheCatalogue_Succeeds`: seed a role row with the unknown permission through the `DbContext`, revoke through `ISerginDispatcher` → success.
  - `RoleDetailPage_OffersScopePermissions`: prerendered `/ua/roles/{id}` HTML contains `Create, rename, move and delete scopes`.
  - `RoleDetailPage_ListsAGrantMissingFromTheCatalogue`: role seeded as above → HTML contains `Unknown` and `permission.ua.nothing.here`.
- [ ] **Step 2:** Run `--filter "FullyQualifiedName~UserAccessPermissionCatalogTests|FullyQualifiedName~ScopedRbacRuleTests|FullyQualifiedName~ScopedRbacPageRenderingTests"`. Expected: build fails (types missing).
- [ ] **Step 3:** Implement the definitions, registrations, call-site switch, validators and pages. Update tests that named `UserAccessPermissions` to the new classes.
- [ ] **Step 4:** Run the filter, `dotnet test tests/Sergin.UserAccess.UnitTests`, and the whole integration suite. Expected: all pass.
- [ ] **Step 5:** Commit in `src/Modules/UserAccess` (`Define UserAccess permissions per aggregate`), then pointer + tests in host.

### Task 5: DeviceManagement onto definitions

**Files:**
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Devices/DevicePermissions.cs` (`Read` "View devices", `Update` "Create and edit devices", `Delete` "Delete devices"), `Manufacturers/ManufacturerPermissions.cs` (`Read` "View manufacturers and models", `Update` "Create and edit manufacturers and models", `Delete` "Delete manufacturers").
- Modify: `...Application.Configurations/Devices/DeviceAggregateFeatureConfiguration.cs`, `Manufacturers/ManufacturerAggregateFeatureConfiguration.cs` (chain `.Permissions(...)`).
- Modify: the 15 command configurations under `...Application.Configurations/` (each `RequirePermissions("…")` → the matching definition).
- Modify: `...Presentation.Blazor/DeviceManagementNavigation.cs` (`DevicePermissions.Read.Permission.Value`, `ManufacturerPermissions.Read.Permission.Value` until task 6).
- Modify: `src/Modules/DeviceManagement/CLAUDE.md` permission mentions.
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Permissions/DeviceManagementPermissionCatalogTests.cs`; replace DM string literals in tests (`RbacScenario`, `CurrentScopeTests`, `RecordScopeTests`, `NavVisibilityTests`, `DeviceGrpcRoundTripTests`, `ExternalIdentityProvisioningTests`, `ModuleCommandConfigurationTests`) with `DevicePermissions.X.Permission` / `ManufacturerPermissions.X.Permission`.

**Interfaces:**
- Consumes: tasks 2–3 APIs.
- Produces: `DevicePermissions`, `ManufacturerPermissions` (namespaces `Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices|Manufacturers`).

- [ ] **Step 1:** Test `Catalog_HoldsEveryDeviceManagementPermission`: six definitions, schema `dm`, groups `Device`, `Manufacturer`; and `EveryDmConfiguration_RequiresOnlyRegisteredPermissions`: for each `ConfiguredTypes` entry of the host registry whose name starts with `Sergin.MeterMinder.DeviceManagement`, every required permission `catalog.TryGet` hits.
- [ ] **Step 2:** Run `--filter FullyQualifiedName~DeviceManagementPermissionCatalogTests`. Expected: build fails (types missing).
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the filter and the whole suite. Expected: all pass.
- [ ] **Step 5:** Commit in host (`Define DeviceManagement permissions per aggregate`).

### Task 6: Contract — definitions only, use-site refusals

**Files:**
- Modify: `CommandConfigurationBuilder.cs` (remove `RequirePermissions(Permission, ...)`; store definitions only), `CommandSettings.cs` (`IReadOnlyCollection<PermissionDefinition> RequiredPermissions`), `CommandConfigurationRegistry.cs` (remove `KnownPermissions`; refusal 3 in `Build`: every required definition is `AllPlatform` or in `aggregates.PermissionsOf(root)` for some configured root — message names the configuration, the request and the permission).
- Modify: `IScopeAuthorization.cs`, `ScopeAuthorization.cs` (remove `Permission` overloads; inject `PermissionCatalog`; an unregistered definition logs a warning naming it and returns false / `ScopeReach.Nothing`).
- Modify: `PermissionCheckPipelineBehavior.cs`, `RequiredPermissionsContext.cs` (`IReadOnlyCollection<PermissionDefinition>`), `ScopeFilter.cs`, `IAuthorizationScopeContext.cs` (doc refs), `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Interceptors/RecordScopeInterceptor.cs`.
- Modify: `src/SharedKernel/Sergin.SharedKernel.Modules/Sergin.SharedKernel.Modules.csproj` (add `ProjectReference` to `Sergin.SharedKernel.Domain`), `SerginNavItem.cs` (`PermissionDefinition? RequiredPermission`, doc updated), `Sergin.SharedKernel.Presentation.Blazor/Layout/SerginNavMenu.razor.cs` (drop `Permission.TryCreate`).
- Modify: `SerginWebUiExtensions.cs` (refusal 4: after `ValidateRoutePrefixes`, every `NavItems` `RequiredPermission` must be in `PermissionCatalog` — message names the module, the nav label and the permission).
- Modify: `UserAccessNavigation.cs`, `DeviceManagementNavigation.cs` (pass definitions).
- Modify tests: `Commands/CommandConfigurationTestTypes.cs` and `Events/OutboxTestTypes.cs` (test definitions classes `TestThingPermissions` (schema `test`), `TestEventPermissions` (schema `test-events`), plus a test aggregate configuration registering them, added to every `FromTypes(...)` that uses them: `CurrentScopeTests`, `RecordScopeTests`, `CommandConfigurationHostTests`, `CommandConfigurationRegistryTests`, `OutboxTestHost`). Remove `MalformedPermissionConfiguration` and its test (covered by task 1/2). Remaining `Permission`-typed role grants in tests use `X.Permission`.
- Test: `CommandConfigurationRegistryTests.cs`, `Authorization/ScopeAuthorizationTests.cs` (new), `Shell/NavVisibilityTests.cs`, `Permissions/PermissionCatalogHostTests.cs` (new).

**Interfaces:**
- Consumes: everything above.
- Produces: final public API as in the spec's "Use sites take definitions" table.

- [ ] **Step 1: Write failing tests**
  - `RequiredPermission_NotRegisteredByAnyAggregate_Fails`: `FromConfigurationTypes([ConfiguredTestQueryConfiguration])` without the test aggregate configuration → message contains `ConfiguredTestQueryConfiguration` and `permission.test.things.read`.
  - `RequiredAllPlatform_NeedsNoRegistration`.
  - `CurrentUserHasAsync_OfAnUnregisteredDefinition_Denies`: system identity excluded; a signed-in admin (holds `platform-all`) asked for an unregistered test definition → false.
  - `NavItem_WithAnUnregisteredPermission_FailsStart`: host built with a test `ISerginWebUiModule` whose nav item requires an unregistered definition → start throws naming the label.
  - Existing `NavVisibilityTests` still pass unchanged in assertions.
- [ ] **Step 2:** Run `--filter "FullyQualifiedName~CommandConfigurationRegistryTests|FullyQualifiedName~ScopeAuthorizationTests|FullyQualifiedName~PermissionCatalogHostTests"`. Expected: new tests fail (refusals absent; overload still accepts `Permission`).
- [ ] **Step 3:** Remove the old overloads and switch the types; fix every compile error the build lists. `grep -rn "\"permission\." src --include=*.cs` must return only `PermissionDefinition.Create(` lines and the migration seed.
- [ ] **Step 4:** Run `dotnet build Sergin.MeterMinder.slnx`, the whole integration suite and the UA unit tests. Expected: 0 warnings, all pass.
- [ ] **Step 5:** Commit in `src/SharedKernel` (`Require PermissionDefinition at every use site`), in `src/Modules/UserAccess` (nav), then host (DM nav, tests, pointers).

### Task 7: Documentation

**Files:**
- Modify: `.claude/CLAUDE.md` ("Request policy" bullet: definitions, registration, refusals; "The Blazor UI host" bullet naming `permission.dm.manufacturers.read` → `ManufacturerPermissions.Read`).
- Modify: `src/SharedKernel/.claude/CLAUDE.md` ("Scoped permission checks": replace `KnownPermissions` paragraph with `PermissionCatalog`; `Aggregates/` entry gains `Permissions(...)`; `Modules` entry notes the Domain reference).
- Modify: `src/Modules/UserAccess/.claude/CLAUDE.md` ("Permissions" bullet: three classes replace `UserAccessPermissions`; validators check the catalogue).
- Modify: `.claude/skills/add-feature/SKILL.md`, `.claude/skills/add-module/SKILL.md`, `src/Modules/UserAccess/.claude/skills/add-feature/SKILL.md` (a new aggregate defines `<Aggregate>Permissions` in Contracts and registers them; a configuration requires a definition).
- Modify: spec status line → `Status: implemented, 2026-10-20` (or the merge date).

- [ ] **Step 1:** Edit the files.
- [ ] **Step 2:** `grep -rn "UserAccessPermissions\|KnownPermissions" .claude src/*/.claude src/Modules/*/.claude src/Modules/*/CLAUDE.md` returns nothing.
- [ ] **Step 3:** Run `graphify update .` then `python .claude/skills/graphify/scripts/graphify_repair.py` (only if `graphify-out/graph.json` exists in the worktree).
- [ ] **Step 4:** Commit in each submodule touched, then host (`Document the permission catalogue`).
