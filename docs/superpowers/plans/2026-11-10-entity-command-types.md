# Entity Command Types Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every request record a contract-level target and effect interface, derive the aggregate binding and
the expected-version rule from it, add the host-start guards, and move every existing record in `dm`, `ua` and
`st` onto it.

**Architecture:** The interfaces live in `Sergin.SharedKernel.Application` (`Commands/Types/`). The
`CommandConfigurationRegistry` reads each module's Contracts types as well as its configurations, so a typed record
gets its root, permission scope and version rule from its type. The old bound-configuration form and
`RequireExpectedVersion()` stay during the migration, so every task builds and tests green, and the last
SharedKernel task deletes them.

**Tech Stack:** .NET 10, C# records and generic interfaces, MediatR pipeline, EF Core model metadata, xUnit +
Testcontainers.

**Spec:** `docs/superpowers/specs/2026-11-10-entity-command-types-design.md`

## Global Constraints

- `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer. Generic interfaces whose type parameters no member
  uses take a scoped `#pragma warning disable S2326`, as `ICommandConfiguration.cs` does today.
- Namespace for the new interfaces: `Sergin.SharedKernel.Application.Commands.Types`.
- `ICreatedResponse.Id` is `Guid`. Typed ids change on requests only.
- No text key changes. Shown properties keep their names: `ParentId`, `NewParentId`, `OwnerScopeId`, `Permission`.
- The guarded set stays exactly these 20 commands: AddDeviceModel, ChangeTranslation, DeleteDevice, DeleteLanguage,
  DeleteManufacturer, DeleteRole, DeleteTranslation, GrantRolePermission, PublishLanguage, RemoveDeviceModel,
  RenameDeviceModel, RenameLanguage, RenameRole, RenameScope, RevokeRolePermission, SetLanguageDirection,
  SetRoleInheritable, UnpublishLanguage, UpdateDevice, UpdateManufacturer.
- `MoveScope` and `DeleteScope` opt out with `WithoutExpectedVersion()`.
- Unused types (criteria, import, clone, restore, purge, reorder, export) get interfaces and shape guards only. No
  execution rules.
- SharedKernel, UserAccess and Settings are submodules. Commit inside each submodule on its own
  `entity-command-types` branch. Pushing and opening PRs is outward-facing: ask the user first (Task 9).
- Commit under the user's git identity. No `Co-Authored-By` trailer (project CLAUDE.md).

## Review Focus

1. A record typed with one root and bound by its configuration to another (possible only during the migration)
   must fail host start naming both, not silently pick one. Test in Task 3.
2. An *unconfigured* typed command on a versioned root, sent with no version, must answer 428. Before this work it
   had no policy at all. Test in Task 3.
3. A typed *query* on a versioned root is never version-guarded, even with an entity target. Test in Task 3.
4. A guard over a remote module's types must not ask for a `DbContext` mapping the gateway does not have. The
   mapping guard takes only local Contracts assemblies. Test in Task 4.
5. A child command chains the root's version across a bulk run. After the renames, `DeviceModelListView`'s bulk
   Remove must still pass the manufacturer's version along. Covered by the existing device-model bulk test in
   Task 7, which must still pass unchanged.

---

### Task 0: Worktree and regression pins

**Files:**
- Create: `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/Types/MigrationPinTests.cs`
- Create: `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/Types/catalog-keys.txt` (copied to output)

**Interfaces:**
- Produces: two tests that pass on today's code and must keep passing through every later task.

- [ ] **Step 1: Create the worktree.** Use superpowers:using-git-worktrees with branch `entity-command-types`. Inside
  it run `git submodule update --init --recursive`, then
  `git -C src/SharedKernel switch -c entity-command-types`, and the same for `src/Modules/Administration/UserAccess`
  and `src/Modules/Administration/Settings`.
- [ ] **Step 2: Write `GuardedSet_IsExactlyTheTwentyCommands`.** Resolve `CommandConfigurationRegistry` from the
  shared factory. Over every type in the three modules' `ContractsAssembly`, collect `Name` where
  `registry.For(type).RequiresExpectedVersion`, minus the `Command` suffix. Assert it equals the 20 names in Global
  Constraints, order-insensitive.
- [ ] **Step 3: Write `CatalogKeys_AreUnchanged`.** Resolve `TextCatalog`, take every key, sort ordinally, and
  assert equality with the lines of `catalog-keys.txt`. Generate the file once from the current catalog (a
  temporary test run that writes it, then delete that code) and commit it.
- [ ] **Step 4: Run.** `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All --filter FullyQualifiedName~MigrationPinTests`
  Expected: 2 passed.
- [ ] **Step 5: Commit** in the root repo: `test: pin guarded commands and catalog keys before command types`.

### Task 1: Target and effect interfaces

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Types/Targets.cs`
- Create: `.../Commands/Types/CommandEffects.cs`
- Create: `.../Commands/Types/QueryEffects.cs`
- Create: `.../Commands/Types/UnusedEffects.cs`
- Create: `.../Commands/Types/CommandTarget.cs` (the `CommandTarget` record, the `CommandTargetKind` enum and the static `CommandTypes` reader)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/Types/CommandTypeTestTypes.cs`, `CommandTargetTests.cs`

**Interfaces:**
- Produces, every root constrained `where TRoot : class, IAggregateRoot<TId> where TId : notnull`, every child
  `where TChild : class, IEntity<TChildId>`:
  - Targets: `IEntityTarget<TRoot, TId> { TId Id { get; } }`,
    `IChildEntityTarget<TRoot, TRootId, TChild, TChildId> { TRootId RootId { get; } TChildId Id { get; } }`,
    `IRootTarget<TRoot, TRootId> { TRootId RootId { get; } }`, `ICollectionTarget<TRoot>` (no members,
    `TRoot : class, IAggregateRoot`), `IUnderParent<TParent, TParentId> { TParentId ParentId { get; } }`.
  - Command effects. Arity overloads cover "entity or child" (2 type params target an entity, 4 a child):
    `ICreateCommand<TRoot, TId> : ICollectionTarget<TRoot>`,
    `IAddChildCommand<TRoot, TRootId, TChild, TChildId> : IRootTarget<TRoot, TRootId>`,
    `IUpdateCommand<…2|4>`, `ISetPropertyCommand<…2|4>`, `ITransitionCommand<…2|4>`,
    `IMoveCommand<TRoot, TId> : IEntityTarget<TRoot, TId> { TId NewParentId { get; } }`,
    `IDeleteCommand<TRoot, TId>`, `IRemoveChildCommand<TRoot, TRootId, TChild, TChildId>`,
    `IAddValueCommand<TRoot, TId, TValue>`, `IRemoveValueCommand<TRoot, TId, TValue>` (both `: IEntityTarget<TRoot, TId>`),
    `ISystemCommand`, `ISelectable`, `IReverseOf<TOther> where TOther : IBaseCommand`,
    `ICreatedResponse { Guid Id { get; } }`.
  - Query effects: `IGetOneQuery<TRoot, TId>`, `IChildGetOneQuery<TRoot, TRootId, TChild, TChildId>`,
    `IAuditQuery<TRoot, TId>`, `IEntityOverviewQuery<TRoot, TId>`, `IEntityViewQuery<TRoot, TId>` (entity
    targets); `IListOf<TRoot>`, `ITreeQuery<TRoot>`, `IOverviewQuery<TRoot>` (collection);
    `IChildListOf<TRoot, TRootId, TChild> : IRootTarget<TRoot, TRootId>` (`TChild : class, IEntity`).
  - Unused: `ICriteriaCommand<TRoot, TListQuery> : ICollectionTarget<TRoot> { Term? Term; Filtering? Filtering; int ExpectedCount }`
    (`TListQuery : ListQuery`), `IImportCommand<TRoot, TItem> : ICollectionTarget<TRoot> { IReadOnlyList<TItem> Items }`,
    `ICloneCommand<TRoot, TId> : IEntityTarget<TRoot, TId>`, `IRestoreCommand<TRoot, TId>`, `IPurgeCommand<TRoot, TId>`,
    `IReorderCommand<TRoot, TRootId, TChild, TChildId> : IChildEntityTarget<…> { int Position }`,
    `IExportQuery<TRoot, TListQuery> : ICollectionTarget<TRoot> { Term? Term; Filtering? Filtering }`.
  - `public enum CommandTargetKind { Collection, Root, Entity, ChildEntity }`
  - `public sealed record CommandTarget(Type Root, CommandTargetKind Kind, Type? Child, Type? Parent)`
  - `public static class CommandTypes`: `CommandTarget? TargetOf(Type requestType)`,
    `IReadOnlyList<Type> EffectsOf(Type requestType)` (the open generic effect interfaces it implements, plus
    `ISystemCommand`), `bool IsSystem(Type)`, `bool IsSelectable(Type)`. `TargetOf` throws
    `InvalidOperationException` naming the type when the record implements two target interfaces with different roots.

- [ ] **Step 1: Write test types.** In `CommandTypeTestTypes.cs`: `TestTypedRoot : AggregateRoot<TestTypedRootId>`
  with a child `TestTypedChild : Entity<TestTypedChildId>`, and one record per effect interface (for example
  `TestDeleteCommand(TestTypedRootId Id) : IDeleteCommand<TestTypedRoot, TestTypedRootId>, ICommand<Deleted>`),
  including one record per unused interface.
- [ ] **Step 2: Write `CommandTargetTests`.**
  - `TargetOf_EntityCommand_IsEntityOnItsRoot`: `TargetOf(typeof(TestDeleteCommand))` equals `new(typeof(TestTypedRoot), Entity, null, null)`.
  - `TargetOf_ChildCommand_CarriesTheChild`: Kind `ChildEntity`, Child `typeof(TestTypedChild)`.
  - `TargetOf_AddChild_IsRoot`.
  - `TargetOf_CreateUnderParent_CarriesTheParent`: Kind `Collection`, Parent set.
  - `TargetOf_UntypedRecord_IsNull`.
  - `EffectsOf_ReturnsOpenGenericDefinitions`: for `TestDeleteCommand` the result is `[typeof(IDeleteCommand<,>)]`.
- [ ] **Step 3: Run.** `dotnet test … --filter FullyQualifiedName~CommandTargetTests`. Expected: FAIL to compile.
- [ ] **Step 4: Implement the interfaces and `CommandTypes`.** Every effect interface derives from its target
  interface, so `TargetOf` only walks the target definitions. Doc comments copy each interface's rule from the
  spec's tables. Unused interfaces carry a `<remarks>` with the spec's execution contract and the words "not built
  yet".
- [ ] **Step 5: Run** the tests, then `dotnet build Sergin.MeterMinder.slnx`. Expected: tests pass, build has 0 warnings.
- [ ] **Step 6: Commit** in `src/SharedKernel` (the interfaces) and in the root (the tests):
  `feat: entity command target and effect interfaces`.

### Task 2: The registry reads typed records

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/ModuleConfigurationSource.cs`
- Modify: `.../Commands/Configuration/CommandConfigurationRegistry.cs`
- Modify: `.../Commands/Configuration/CommandConfigurationBuilder.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs:153-161`
- Test: `tests/.../Commands/Types/CommandTypeRegistryTests.cs`

**Interfaces:**
- Consumes: `CommandTypes.TargetOf`, `CommandTargetKind` (Task 1).
- Produces:
  - `ModuleConfigurationSource.FromAssemblies(Assembly configurationsAssembly, Assembly contractsAssembly, string schema, string? moduleName = null)`,
    with a new property `IReadOnlyCollection<Type> RequestTypes` (the concrete `IBaseCommand` types of the contracts
    assembly). `FromAssembly` stays for now. `FromTypes` puts any `IBaseCommand` among its arguments into `RequestTypes`.
  - `CommandConfigurationBuilder<TCommand>.WithoutExpectedVersion()`, which sets `internal bool ExpectedVersionWaived`.
  - `CommandConfigurationRegistry.RequestTypes` (configured ∪ typed). `For(type)` answers derived settings for a
    typed record with no configuration.

- [ ] **Step 1: Write `CommandTypeRegistryTests`**, each building the registry with `FromTypes(...)`:
  - `TypedEntityCommand_OnVersionedRoot_RequiresVersion_WithoutConfiguration`: `For(...).RequiresExpectedVersion` is
    true and `AggregateRoot` is the root.
  - `TypedChildCommand_RequiresTheRootsVersion`.
  - `TypedCreateCommand_RequiresNoVersion`.
  - `TypedEntityQuery_OnVersionedRoot_RequiresNoVersion` (Review Focus 3).
  - `TypedEntityCommand_OnUnversionedRoot_RequiresNoVersion`.
  - `WithoutExpectedVersion_WaivesTheDefault`.
  - `WithoutExpectedVersion_WhereNothingIsRequired_Throws_NamingTheConfiguration`.
  - `TypedRecord_BoundToAnotherRoot_Throws_NamingBoth` (Review Focus 1).
  - `TypedRecordOnUnscopedRoot_IsCheckedOnAnyScope`, `TypedRecordOnScopedRoot_IsCheckedInTheCurrentScope`.
  - `CloneCommand_RequiresNoVersion`.
- [ ] **Step 2: Run them.** Expected: FAIL to compile (`WithoutExpectedVersion`, `RequestTypes` missing).
- [ ] **Step 3: Implement.** `Build` takes the request types from all sources. A typed record's root comes from
  `TargetOf`. When a bound configuration also names a root, the two must be the same type, or `Build` throws. The
  version rule is `IsCommand && Kind is Root or Entity or ChildEntity && !IsClone && features.Versioned && !waived`,
  OR-ed with an explicit `RequireExpectedVersion()` while that call still exists. The text category falls back to
  the derived root's, the same as for a bound root today.
- [ ] **Step 4: Switch `AddSerginCore`** to `FromAssemblies(module.ConfigurationsAssembly, module.ContractsAssembly, …)`
  for local and remote modules.
- [ ] **Step 5: Run** the new tests, `CommandConfiguration*Tests`, and `MigrationPinTests`. Expected: all pass.
- [ ] **Step 6: Commit** SharedKernel and root: `feat: derive aggregate binding and version rule from command types`.

### Task 3: Pipeline proof for derived settings

**Files:**
- Test: `tests/.../Concurrency/TypedExpectedVersionPipelineTests.cs`

**Interfaces:**
- Consumes: Task 2's derived settings. The existing `ExpectedVersionPipelineTests` host shape and `TestVersionedDbContext`.

- [ ] **Step 1: Write the tests**, copying the host setup of `ExpectedVersionPipelineTests` with a typed, unconfigured
  test command on a `Versioned()` test root:
  - `UnconfiguredTypedCommand_SentWithoutVersion_Is428`: the error equals `VersionErrors.Required` (Review Focus 2).
  - `TypedQuery_SentWithoutVersion_Runs`.
  - `TypedCommand_WithWaiver_SentWithoutVersion_Runs`.
- [ ] **Step 2: Run.** Expected: pass with Task 2 in place. If one fails, fix the registry, not the test.
- [ ] **Step 3: Commit** in the root: `test: pipeline honours version rule derived from command type`.

### Task 4: Shape and mapping guards, field-text auto-hide

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Hosts/Commands/CommandTypeGuard.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Aggregates/CommandTargetMappingGuard.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/Localizations/FieldTextGuard.cs` (`MissingFields`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs:174-180`, `src/SharedKernel/Sergin.SharedKernel.Hosts.WebApi/SerginWebApiExtensions.cs:57-65`
- Modify: `SerginCoreExtensions.cs`: register `CommandTypeGuard` beside `FieldTextGuard`, over the same Contracts
  assemblies, and record which of them are local.
- Test: `tests/.../Commands/Types/CommandTypeGuardTests.cs`, `CommandTargetMappingGuardTests.cs`, `FieldTextAutoHideTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class CommandTypeGuard(IEnumerable<Assembly> contractsAssemblies)`, with
    `void EnsureShaped(CommandConfigurationRegistry registry, AggregateFeatureRegistry aggregates)` and
    `static void Check(IEnumerable<Type> candidates, CommandConfigurationRegistry registry, AggregateFeatureRegistry aggregates, bool requireTyped)`.
    `requireTyped` is false until Task 8.
  - `public static class CommandTargetMappingGuard`, with
    `void EnsureMapped(IEnumerable<Type> localRequestTypes, IReadOnlyCollection<IModel> models)`.
- Rules, each a line in one `InvalidOperationException` that names the type:
  - CommandTypeGuard: two effect interfaces; `ISelectable` on a collection or root target; `ISetPropertyCommand`
    with other than exactly one shown non-id property; `ITransitionCommand` with any shown non-id property;
    restore or purge on a root that is not `SoftDeletable()`; an `IReverseOf<T>` whose `T` is not an
    `ITransitionCommand` on the same root; and, with `requireTyped`, an `ICommand<T>` that has no effect interface and
    is not `ISystemCommand`. "Shown" uses `registry.HiddenProperties` and the auto-hidden names below.
  - Mapping guard: `TRoot` not found by `model.FindEntityType` in any model; `TChild` not reachable from the root's
    entity type through `GetNavigations()`, recursively.
  - FieldTextGuard: a property named `Id`, `RootId` or `ExpectedCount` that the record's target interface declares
    is not shown.

- [ ] **Step 1: Write the guard tests.** One test per rule, each asserting the message contains the offending type's
  `FullName`. Add `RealHost_PassesBothGuards` against the shared factory. Mapping tests build an `IModel` from a tiny
  test `DbContext` configured with `UseNpgsql("Host=unused")`, which reads `.Model` without connecting. Add
  `MappingGuard_IgnoresTypesNotPassedAsLocal` (Review Focus 4).
- [ ] **Step 2: Write `FieldTextAutoHideTests.TargetIds_NeedNoFieldText`**: a configured typed record with `Id`, `RootId`
  and no `NotShown` passes `FieldTextGuard.Check`.
- [ ] **Step 3: Run.** Expected: FAIL to compile.
- [ ] **Step 4: Implement the guards and the auto-hide.** Wire them in `UseSerginWebUiAsync`/`UseSerginWebApiAsync`
  right after `AggregateFeatureGuard.EnsureApplied`. The mapping guard gets local modules' types only, and the same
  models `AggregateFeatureGuard` builds.
- [ ] **Step 5: Run** the new tests and the full suite. Expected: all pass. No module record is typed yet, so the
  real host passes trivially.
- [ ] **Step 6: Commit**: `feat: host-start guards for command types`.

### Task 5: Move UserAccess records

**Files (all under `src/Modules/Administration/UserAccess/`):**
- Modify: every record in `Sergin.Administration.UserAccess.Application.Contracts/**/Commands/**`, every matching
  configuration in `.Application.Configurations`, and the callers the renames break (handlers, validators,
  `.Presentation.WebApi` endpoints, `.Presentation.Blazor` views and form-to-command mappings).
- Modify: tests under `tests/` that build these records.

**Interfaces:**
- Consumes: Tasks 1, 2 and 4.
- Mapping (from the spec's migration table):

| Record | Type | Field changes |
|---|---|---|
| `CreateUser` | `ICreateCommand<User, UserInternalId>` | response `: ICreatedResponse` |
| `CreateRole` | `ICreateCommand<Role, RoleId>` | response; `OwnerScopeId` unchanged |
| `CreateScope` | `ICreateCommand<Scope, ScopeId>, IUnderParent<Scope, ScopeId>` | `ParentId` Guid → `ScopeId` |
| `AssignRole` | `IAddChildCommand<User, UserInternalId, RoleAssignment, RoleAssignmentId>` | `UserId` → `RootId` (typed); response |
| `RevokeAssignment` | `IRemoveChildCommand<User, UserInternalId, RoleAssignment, RoleAssignmentId>` | `UserId` → `RootId`, `AssignmentId` → `Id`, both typed |
| `RenameRole`, `SetRoleInheritable` | `ISetPropertyCommand<Role, RoleId>` | `Id` typed |
| `RenameScope` | `ISetPropertyCommand<Scope, ScopeId>` | `Id` typed |
| `DeactivateUser` | `ITransitionCommand<User, UserInternalId>` | `Id` typed |
| `MoveScope` | `IMoveCommand<Scope, ScopeId>` + `WithoutExpectedVersion()` | `Id`, `NewParentId` typed |
| `DeleteRole` | `IDeleteCommand<Role, RoleId>` | `Id` typed |
| `DeleteScope` | `IDeleteCommand<Scope, ScopeId>` + `WithoutExpectedVersion()` | `Id` typed |
| `GrantRolePermission`/`RevokeRolePermission` | `IAddValueCommand`/`IRemoveValueCommand<Role, RoleId, Permission>` | `Id` typed |
| `ProvisionExternalUser` | `ISystemCommand` | none |
| `GetUserById`, `GetRoleById`, `GetScopeById` | `IGetOneQuery<…>` | `Id` typed |
| `GetUserList` | `IListOf<User>` | none |
| `GetScopeTree` | `ITreeQuery<Scope>` | none |
| `GetUserAssignments` | `IChildListOf<User, UserInternalId, RoleAssignment>` | `UserId` → `RootId` |
| `GetUserEffectivePermissions` | `IEntityViewQuery<User, UserInternalId>` | `UserId` → `Id` |
| `GetAssignableRoles` | untyped | none |

- [ ] **Step 1: Retype and rename the records.** Switch each configuration from `ICommandConfiguration<TRecord, TRoot>`
  to `ICommandConfiguration<TRecord>`. Delete `RequireExpectedVersion()` and the `NotShown` calls for `Id`/`RootId`.
  Add `WithoutExpectedVersion()` to `MoveScope` and `DeleteScope`.
- [ ] **Step 2: Fix every caller until it builds.** `dotnet build Sergin.MeterMinder.slnx`. Expected: 0 warnings, 0 errors.
- [ ] **Step 3: Run the full integration suite.** Expected: all pass, `MigrationPinTests` included.
- [ ] **Step 4: Commit** in the UserAccess submodule (`refactor: move UserAccess requests onto command types`), then
  in the root (test updates and submodule pointer).

### Task 6: Move Settings records

**Files:** as in Task 5, under `src/Modules/Administration/Settings/`.

| Record | Type | Field changes |
|---|---|---|
| `AddLanguage` | `ICreateCommand<Language, LanguageId>` | response |
| `AddTranslation` | `ICreateCommand<Translation, TranslationId>, IUnderParent<Language, LanguageId>` | `LanguageId` → `ParentId`; keep `NotShown`; `TranslationTexts.LanguageId` stays declared |
| `RenameLanguage`, `SetLanguageDirection` | `ISetPropertyCommand<Language, LanguageId>` | `Id` typed |
| `ChangeTranslation` | `ISetPropertyCommand<Translation, TranslationId>` | none |
| `PublishLanguage` / `UnpublishLanguage` | `ITransitionCommand<Language, LanguageId>`; Unpublish also `IReverseOf<PublishLanguageCommand>` | `Id` typed |
| `DeleteLanguage` / `DeleteTranslation` | `IDeleteCommand<…>` | `Id` typed |
| `GetLanguageById`, `GetTranslationById` | `IGetOneQuery<…>` | `Id` typed |
| `GetLanguageAudit` | `IAuditQuery<Language, LanguageId>` | `Id` typed |
| `GetLanguageList` | `IListOf<Language>` | none |
| `GetTranslationList` | `IListOf<Translation>, IUnderParent<Language, LanguageId>` | constructor `languageId` → `parentId`, property `ParentId` |
| `GetLanguageProgress` | `IEntityViewQuery<Language, LanguageId>` | `LanguageId` → `Id` |

- [ ] **Step 1–4:** same as Task 5. Commit message: `refactor: move Settings requests onto command types`.

### Task 7: Move DeviceManagement records

**Files:** as in Task 5, under `src/Modules/DeviceManagement/`, plus the gRPC client and server
(`GetDeviceByIdGrpcInvoker`, `DeviceGrpcService`: the proto keeps a string GUID, only the mapping changes).

| Record | Type | Field changes |
|---|---|---|
| `CreateDevice` | `ICreateCommand<Device, DeviceIntenralId>` | response |
| `CreateManufacturer` | `ICreateCommand<Manufacturer, ManufacturerId>` | response |
| `AddDeviceModel` | `IAddChildCommand<Manufacturer, ManufacturerId, DeviceModel, DeviceModelInternalId>` | `ManufacturerId` → `RootId`; response |
| `UpdateDevice` / `UpdateManufacturer` | `IUpdateCommand<…>` | `Id` typed |
| `RenameDeviceModel` | `ISetPropertyCommand<Manufacturer, ManufacturerId, DeviceModel, DeviceModelInternalId>` | `ManufacturerId` → `RootId`, `Id` typed |
| `DeleteDevice` / `DeleteManufacturer` | `IDeleteCommand<…>, ISelectable` | `Id` typed |
| `RemoveDeviceModel` | `IRemoveChildCommand<…>, ISelectable` | `ManufacturerId` → `RootId`, `Id` typed |
| `GetDeviceById`, `GetManufacturerById` | `IGetOneQuery<…>` | `Id` typed |
| `GetDeviceModelById` | `IChildGetOneQuery<…>` | `ManufacturerId` → `RootId`, `Id` typed |
| `GetDeviceAudit`, `GetManufacturerAudit` | `IAuditQuery<…>` | `Id` typed |
| `GetDeviceList`, `GetManufacturerList` | `IListOf<…>` | none |
| `GetDeviceModelList` | `IChildListOf<Manufacturer, ManufacturerId, DeviceModel>` | constructor `manufacturerId` → `rootId`, property `RootId` |
| `GetDeviceOverview` | `IOverviewQuery<Device>` | none |
| `GetManufacturerOverview` | `IEntityOverviewQuery<Manufacturer, ManufacturerId>` | `Id` typed |

`ISelectable` goes exactly on the three records whose views call `SerginBulk.RunAsync` today: `DeviceListView`,
`ManufacturerListView`, `DeviceModelListView`.

- [ ] **Step 1–4:** same as Task 5, committed in the root repo only. The device-model bulk Remove test must pass
  unchanged (Review Focus 5). Commit message: `refactor: move DeviceManagement requests onto command types`.

### Task 8: Remove the old forms, require every command typed

**Files:**
- Modify: `ICommandConfiguration.cs` (delete `ICommandConfiguration<TCommand, TAggregateRoot>` and its pragma),
  `CommandConfigurationBuilder.cs` (delete `RequireExpectedVersion`), `CommandConfigurationRegistry.cs` (drop the
  bound-root path and the OR-ed explicit flag), `CommandSettings.cs` (doc comment), `ModuleConfigurationSource.cs`
  (delete `FromAssembly`).
- Modify: the `requireTyped` call site, now `true`.
- Modify tests: `Commands/CommandConfigurationTestTypes.cs`, `Concurrency/ExpectedVersionPipelineTests.cs`,
  `Authorization/CurrentScopeTests.cs`, `Localization/ValidationTextTests.cs`, `Scoping/RecordScopeTests.cs`, and
  `CommandConfigurationRegistryTests`. Their bound or version-marked test records become typed test records on a
  test root with the matching features. A test that covered a removed refusal (expected version on an unversioned
  bound root) is deleted, and the dead-opt-out test from Task 2 replaces it.

- [ ] **Step 1: Add `CommandTypeGuardTests.UntypedCommand_FailsStart_NamingIt`** with `requireTyped: true`.
- [ ] **Step 2: Delete the old API and fix the tests.** `dotnet build Sergin.MeterMinder.slnx`. Expected: 0 warnings.
- [ ] **Step 3: Run the full suite, plus the UserAccess unit tests.** Expected: all pass, `MigrationPinTests` included.
- [ ] **Step 4: Commit** SharedKernel and root: `refactor!: drop bound command configurations and RequireExpectedVersion`.

### Task 9: Docs, then land the four repos

**Files:**
- Modify: `.claude/CLAUDE.md`. Request policy bullet: a type replaces binding, and the version check is derived with
  `WithoutExpectedVersion()` as the opt-out. Concurrency bullet: the guarded list is unchanged, and a type now
  decides it. Naming conventions: typed ids on requests, `RootId` on child requests. Test inventory: new `Commands/Types/` folder.
- Modify: `src/SharedKernel/.claude/CLAUDE.md`, and the UserAccess and Settings CLAUDE.md files (their
  `MoveScope`/`DeleteScope` notes now name `WithoutExpectedVersion()`).
- Modify: `.claude/skills/add-feature/SKILL.md`. The "Request policy" paragraph and the template at lines 34–46 change:
  pick the effect interface, use an unbound configuration, and no `RequireExpectedVersion()`. Line 76 changes the same way.

- [ ] **Step 1: Edit the docs.** Then `grep -rn "RequireExpectedVersion\|ICommandConfiguration<[A-Za-z]*, " .claude src --include=*.md`.
  Expected: no hits outside dated `docs/superpowers` files.
- [ ] **Step 2: Rebuild the graph:** `graphify update .` then `python .claude/skills/graphify/scripts/graphify_repair.py`.
- [ ] **Step 3: Commit** the docs in each repo.
- [ ] **Step 4: Ask the user** before pushing the three submodule branches and opening their PRs (outward-facing).
  Merge order once approved: SharedKernel, then UserAccess and Settings, then this repo with the submodule pointers
  on the merged commits. Then remove the worktree and its branch.
