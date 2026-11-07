# UI Texts in Presentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move every text only the Blazor UI reads out of each module's `.Application.Configurations` (and SharedKernel's `ShellTexts` out of `SharedKernel.Application`) into the Blazor RCLs, with every key unchanged.

**Architecture:** Each RCL gets `public static class <Aggregate>UiTexts` classes built on the Application `TextCategory`, so keys stay identical. `AddSerginBlazorApp` registers one `TextSource.FromAssembly(module.UiAssembly, module.Name)` per `ISerginWebUiModule`; `AddSerginBlazorKit` registers `ShellTexts` beside `MudBlazorTexts.Source`. The catalog's existing refusals (duplicate key, module segment) police the move.

**Tech Stack:** .NET 10, Blazor Server RCLs, xUnit + Testcontainers integration tests.

**Spec:** `docs/superpowers/specs/2026-11-07-ui-texts-in-presentation-design.md`

## Global Constraints

- **No key changes.** Every moved member keeps its name argument, English default and description argument verbatim, and builds on the same `TextCategory` it used before.
- A member leaves the Application class in the same commit it appears in the RCL class (a key from two declaring types fails host start).
- RCL text classes are `public static`; `TextSource` skips anything else silently.
- RCL text classes declare no `FieldText`, no `ErrorTextPair`, no `TextCategory` of their own — except `DeviceManagementUiTexts`, which moves whole with its module-level category.
- Stays in Application: `Category`, every `FieldText`, `Singular`, every error text, anything `.Application`/`.Configurations`/`.Infrastructure` reads.
- No text classes for WebApi or gRPC. `CommonTexts` is not touched.
- Build is warnings-as-errors: `dotnet build Sergin.MeterMinder.slnx` must be clean after every task.
- Commits go under the user's git identity, **no `Co-Authored-By` trailer** (root CLAUDE.md).
- Four repos: SharedKernel, UserAccess and Settings changes are committed inside their submodule, on a branch named `ui-texts-in-presentation` in each; MeterMinder changes on the worktree branch of the same name.

## Review Focus

1. **A moved text renders as its raw key** because its class is `internal` or not `static` — the catalog test in Task 1 (`EveryUiAssemblyText_IsInCatalog`) catches it.
2. **A stored translation stops applying** because a name argument changed during the move (`"DeleteConfirm"` retyped as `"ConfirmDelete"`) — each module task's `UiText_KeepsItsKey` row pins one moved key per class to its literal string.
3. **The role page's permission headings lose the "Device Management" title** after `DeviceManagementTexts` moves — `PermissionModuleLabelTests` must still pass in Task 4, unchanged except the type name.
4. **A field label drifts into an RCL class** — `UiAssemblies_DeclareNoFieldOrErrorText` (Task 1).
5. **The shell renders English under a translation** after `ShellTexts` moves — existing `ShellTextTests.Shell_RendersTranslatedChrome` must pass in Task 1.

---

### Task 0: Worktree and branches

**Files:** none.

- [ ] **Step 1:** Create worktree `../Sergin.MeterMinder-ui-texts` on new branch `ui-texts-in-presentation` from `main`.
- [ ] **Step 2:** In the worktree run `git submodule update --init --recursive`.
- [ ] **Step 3:** In each of `src/SharedKernel`, `src/Modules/Administration/UserAccess`, `src/Modules/Administration/Settings`: `git checkout -b ui-texts-in-presentation`.
- [ ] **Step 4:** Start Docker Desktop if `docker info` fails; then `dotnet build Sergin.MeterMinder.slnx`. Expected: build succeeded, 0 warnings.

### Task 1: SharedKernel — register RCL texts, move `ShellTexts`, guard test

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs` (`AddSerginBlazorApp`, after `builder.AddSerginCore(modules, remoteModules);`)
- Move: `src/SharedKernel/Sergin.SharedKernel.Application/Localizations/Common/ShellTexts.cs` → `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Localizations/ShellTexts.cs`, namespace `Sergin.SharedKernel.Presentation.Blazor.Localizations`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/SerginBlazorKitExtensions.cs`
- Modify: every SharedKernel Blazor file that `using`s `Sergin.SharedKernel.Application.Localizations.Common` only for `ShellTexts` (the compiler names them)
- Create: `tests/Sergin.MeterMinder.IntegrationTests.All/Localization/UiTextPlacementTests.cs`
- Modify: `src/SharedKernel/.claude/CLAUDE.md` ("Localization" section)

**Interfaces:**
- Produces: `UiTextPlacementTests` with a `[Theory] UiText_KeepsItsKey(string key, Type declaringType)`; later tasks add `[InlineData]`-equivalent rows (use `MemberData` from a `public static TheoryData<string, Type> MovedKeys`, one row per moved class).
- Produces: RCL texts are picked up by name of module: `TextSource.FromAssembly(module.UiAssembly, module.Name)`.

- [ ] **Step 1: Write the failing tests** in `UiTextPlacementTests` (`[Collection(nameof(IntegrationTestCollection))]`, primary-constructor `SerginWebApiFactory<Program> factory`):
  - `UiAssemblies_DeclareNoFieldOrErrorText`: for every assembly in `factory.Services.GetRequiredService<SerginUiModuleCatalog>().Modules.Select(m => m.UiAssembly)` plus `typeof(SerginMainLayout).Assembly`, reflect public static classes (nested included); assert no public static field or property of type `FieldText` or `ErrorTextPair`. `Assert.Empty(offenders)` with offenders as `"Type.Member"` strings.
  - `EveryUiAssemblyText_IsInCatalog`: same assemblies; for every public static `TextDefinition` field found, assert `catalog.TryGet(definition.Key.Value, out _)` where `catalog = factory.Services.GetRequiredService<TextCatalog>()`.
  - `UiText_KeepsItsKey(string key, Type declaringType)` over `MovedKeys`: `Assert.Single(catalog.Entries, e => e.Definition.Key.Value == key)` and its `DeclaringType == declaringType`. First row: `{ "common.navigation.home", typeof(ShellTexts) }` (confirm the key literal against `ShellTexts.Navigation.Home` before writing it), and assert in a separate fact `ShellTexts_LivesInTheBlazorKit`: `Assert.Equal(typeof(SerginMainLayout).Assembly, typeof(ShellTexts).Assembly)`.
- [ ] **Step 2: Run** `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All --filter "FullyQualifiedName~UiTextPlacementTests"`. Expected: `ShellTexts_LivesInTheBlazorKit` FAILS (assembly is `Sergin.SharedKernel.Application`); the others pass vacuously.
- [ ] **Step 3: Implement.** Move `ShellTexts.cs` (namespace change only, contents untouched). In `AddSerginBlazorKit` add `services.AddSingleton(TextSource.FromTypes("common", typeof(ShellTexts)));` next to `MudBlazorTexts.Source`. In `AddSerginBlazorApp` add, after `AddSerginCore`, the loop over `modules.OfType<ISerginWebUiModule>()` registering `TextSource.FromAssembly(module.UiAssembly, module.Name)`, with a one-line comment saying the module's UI-only texts are declared in its RCL. Fix usings the build reports. Update the host's `Components/_Imports.razor` only if the build needs it (it already imports `Sergin.SharedKernel.Presentation.Blazor.Localizations`).
- [ ] **Step 4: Run** `dotnet build Sergin.MeterMinder.slnx` (clean), then `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All --filter "FullyQualifiedName~Localization"`. Expected: all pass, including `ShellTextTests` and `PseudoLocaleSweepTests`.
- [ ] **Step 5: Docs.** In `src/SharedKernel/.claude/CLAUDE.md` "Localization": `ShellTexts` now lives in `Presentation.Blazor` and is registered by `AddSerginBlazorKit`; `AddSerginBlazorApp` scans each `UiAssembly` for UI texts; state the placement rule in one sentence.
- [ ] **Step 6: Commit** in `src/SharedKernel`: `Localization: UI texts are declared in the presentation layer`. Commit the test file in the worktree root: `Test that UI texts live in the RCLs`.

### Task 2: UserAccess

**Files:**
- Create: `src/Modules/Administration/UserAccess/Sergin.Administration.UserAccess.Presentation.Blazor/Users/UserUiTexts.cs`, `Roles/RoleUiTexts.cs`, `Scopes/ScopeUiTexts.cs`
- Modify: `…UserAccess.Application.Configurations/Users/UserTexts.cs`, `Roles/RoleTexts.cs`, `Scopes/ScopeTexts.cs`; RCL call sites; tests that reference a moved member; UserAccess `.claude/CLAUDE.md`
- Test: `tests/.../Localization/UiTextPlacementTests.cs` (`MovedKeys` rows)

**Interfaces:**
- Consumes: Task 1 registration and `MovedKeys`.
- Produces: `UserUiTexts`, `RoleUiTexts`, `ScopeUiTexts` in namespaces `Sergin.Administration.UserAccess.Presentation.Blazor.{Users,Roles,Scopes}`.

Members to move (each a `TextDefinition`, verbatim):
- `UserTexts` → `UserUiTexts`: `Identifier`, `Title`, `New`, `Deactivate`, `DeactivateTitle`, `DeactivateConfirm`.
- `RoleTexts` → `RoleUiTexts`: `New`, `OwnedBy`, `InheritableOnCreate`, `InheritableOnDetail`, `Unknown`, `AddPermission`, `PlatformModule`, `DeleteTitle`, `DeleteConfirm`, `DeleteCascadeConfirm`.
- `ScopeTexts` → `ScopeUiTexts`: `Title`, `New`, `Under`, `TreeRoot`, `NewChild`, `NewRoleHere`, `SubScope`, `Move`, `ChildScopes`, `NoChildScopes`, `NoScopes`, `Level`, `RolesUsableHere`, `Role`, `OwnedBy`, `Inheritable`, `Yes`, `No`, `FilterPlaceholder`, `TreeEmpty`, `SelectPrompt`, `SelectHint`, `DeleteTitle`, `DeleteConfirm`.

Before moving each, grep `.Application`, `.Application.Configurations`, `.Infrastructure` and `.Domain` for it; if anything outside the RCL reads it, it stays and the plan list is wrong — note it in the commit message.

- [ ] **Step 1: Failing test.** Add `MovedKeys` rows: `UserUiTexts.New` key, `RoleUiTexts.DeleteConfirm` key, `ScopeUiTexts.TreeRoot` key, each as the literal key string read from today's declaration (e.g. `"user-access.users.new"`) with its new type. Expected: compile error (types missing).
- [ ] **Step 2: Implement.** Create the three classes (`private static readonly TextCategory category = <Aggregate>Texts.Category;`), move members with their XML docs, delete them from the Application classes, update call sites (RCL and tests, including `PermissionModuleLabelTests`'s `RoleTexts.PlatformModule` → `RoleUiTexts.PlatformModule`).
- [ ] **Step 3: Run** build, then `dotnet test … --filter "FullyQualifiedName~Localization|FullyQualifiedName~Permissions|FullyQualifiedName~Shell"`. Expected: all pass.
- [ ] **Step 4: Docs.** UserAccess `.claude/CLAUDE.md`: where UI texts live.
- [ ] **Step 5: Commit** in the submodule (`Move UI-only texts into the Blazor RCL`) and the test rows in the worktree root.

### Task 3: Settings

Same shape as Task 2.

**Files:** create `src/Modules/Administration/Settings/Sergin.Administration.Settings.Presentation.Blazor/Languages/LanguageUiTexts.cs`, `Translations/TranslationUiTexts.cs`; modify `LanguageTexts.cs`, `TranslationTexts.cs`, call sites, tests (`LookupTests`, `TranslationTests` if they name a moved member), Settings `.claude/CLAUDE.md`.

Members to move:
- `LanguageTexts` → `LanguageUiTexts`: `Title`, `New`, `SearchPlaceholder`, `Publish`, `Unpublish`, `DeleteTitle`, `DeleteConfirm`, `Translations`, `OverviewTranslated`, `OverviewPercent`, `FromCulture`, `LeftToRight`, `RightToLeft`, `DirectionResolved`, `Draft`, `Published`.
- `TranslationTexts` → `TranslationUiTexts`: `Title`, `Progress`, `SearchPlaceholder`, `FilterChip`, `Saved`, `ChangedMeanwhile`, `DeleteTitle`, `DeleteConfirm`, `Untranslated`, `Translated`, `NeedsReview`, `Orphaned`.

- [ ] **Step 1:** `MovedKeys` rows for `LanguageUiTexts.Publish` and `TranslationUiTexts.Orphaned` (literal keys). Expected: compile error.
- [ ] **Step 2:** Implement as Task 2 Step 2.
- [ ] **Step 3:** Build; run `--filter "FullyQualifiedName~Localization"`. Expected: all pass, `SettingsPageRenderingTests` included.
- [ ] **Step 4:** Settings CLAUDE.md.
- [ ] **Step 5:** Commit in submodule and worktree root.

### Task 4: DeviceManagement, root docs and skills

**Files:**
- Create in `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/`: `DeviceManagementUiTexts.cs`, `Devices/DeviceUiTexts.cs`, `Manufacturers/ManufacturerUiTexts.cs`, `Manufacturers/DeviceModels/DeviceModelUiTexts.cs`
- Delete: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Configurations/DeviceManagementTexts.cs`
- Modify: `DeviceTexts.cs`, `ManufacturerTexts.cs`, `DeviceModelTexts.cs`, `DeviceManagementNavigation.cs`, RCL call sites, `tests/.../Permissions/PermissionModuleLabelTests.cs`, other tests the compiler names
- Modify: `.claude/CLAUDE.md` (Localization > Declare bullet), `.claude/skills/add-feature/SKILL.md`, `.claude/skills/add-module/SKILL.md`; submodule pointers

**Interfaces:** Produces `DeviceManagementUiTexts` (`public static readonly TextCategory Category = TextCategory.Create("DeviceManagement");`, `MenuTitle` unchanged), `DeviceUiTexts`, `ManufacturerUiTexts`, `DeviceModelUiTexts` (on `DeviceModelTexts.Category`).

Members to move:
- `DeviceTexts` → `DeviceUiTexts`: `Title`, `New`, `EditTitle`, `SelectManufacturerFirst`, `SearchPlaceholder`, `CreatedBetween`, `FilterChip`, `DateRange`, `DeleteTitle`, `DeleteConfirm`, `DeleteManyTitle`, `DeleteManyConfirm`, `OverviewDevices`, `OverviewInScope`, `OverviewModelsInUse`, `OverviewOfTotal`, `OverviewManufacturers`, `OverviewInCatalog`, `OverviewAdded`, `OverviewAddedCount`, `OverviewLastMonth`, `SummaryDevices`, `SummaryModelsInUse`.
- `ManufacturerTexts` → `ManufacturerUiTexts`: `Title`, `New`, `EditTitle`, `SearchPlaceholder`, `DeleteTitle`, `DeleteConfirm`, `DeleteManyTitle`, `DeleteManyConfirm`, `OverviewModels`, `OverviewOfManufacturer`, `OverviewDevices`, `OverviewInEveryScope`, `SummaryModels`, `SummaryDevices`.
- `DeviceModelTexts` → `DeviceModelUiTexts`: `Identifier`, `Title`, `New`, `RenameTitle`, `Add`, `SearchPlaceholder`, `Empty`, `DevicesColumn`, `DeviceCount`, `InUseTooltip`, `RemoveTitle`, `RemoveConfirm`, `RemoveManyTitle`, `RemoveManyConfirm`.
- `DeviceManagementTexts` → `DeviceManagementUiTexts`: whole class.

- [ ] **Step 1:** `MovedKeys` rows: `"device-management.devices.delete-confirm"` → `DeviceUiTexts`, the `ManufacturerUiTexts.OverviewModels` key, the `DeviceModelUiTexts.RemoveConfirm` key, and `"device-management.menu-title"` → `DeviceManagementUiTexts` (confirm each literal against today's declaration). Expected: compile error.
- [ ] **Step 2:** Implement as Task 2 Step 2; `PermissionModuleLabelTests` switches `DeviceManagementTexts` → `DeviceManagementUiTexts`, nothing else.
- [ ] **Step 3:** Build; run the full suite `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All` and `dotnet test tests/Sergin.Administration.UserAccess.UnitTests`. Expected: all pass, `ModuleConfigurationsAssemblyTests` and `PseudoLocaleSweepTests` included.
- [ ] **Step 4: Docs.** Root CLAUDE.md Localization > "Declare": `<Aggregate>Texts` in `.Application.Configurations` holds the category, fields, `Singular` and error texts; page copy goes in `<Aggregate>UiTexts` in the RCL, on the same category; cite the spec. Same sentence in both skills where they describe declaring texts. DeviceManagement's module CLAUDE.md if it lists text classes.
- [ ] **Step 5:** Commit DeviceManagement + docs + test rows + the three submodule pointer bumps in the worktree root.
- [ ] **Step 6:** `graphify update .` then `python .claude/skills/graphify/scripts/graphify_repair.py` (skip if the worktree has no graph).

### Task 5: Finish

- [ ] Invoke superpowers:finishing-a-development-branch. PRs go SharedKernel first, then UserAccess and Settings, then MeterMinder (whose submodule pointers must reference the merged commits). Remove worktree and branches after merge.
