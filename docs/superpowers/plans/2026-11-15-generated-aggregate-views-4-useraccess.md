# Generated Aggregate Views, Plan 4 of 4: UserAccess — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the UserAccess pages onto SharedKernel's generated views. This covers User and its assignments, Role and its permission picker, and Scope and its tree, including Move. It then deletes `SerginScheduleAction` and updates the docs and skills, which finishes the series.

**Architecture:**
- **Hardening first.** Task 1 closes the five items the Plan 3 final review asked for before UserAccess.
- **What UserAccess needs from SharedKernel:**
  - A record that belongs to a parent record, through `BelongsTo<TParent, TResponse>`. A role belongs to its owner scope and a scope to its parent scope. This gives the trail's parent step, where a delete leaves to, and a create form that takes the parent's id from its page.
  - A delete that asks for extra values (`Cascade`) in its confirm dialog.
  - A confirm on a transition (Deactivate).
  - Move, through a parent-picker dialog fed by the root's `ITreeQuery`.
  - A child entity drawn by a component of its own, through `Child<T>(c => c.Section<TComponent>())`. Assignments are read as one unpaged list plus the effective permissions, so no generated table fits them.
  - Custom form inputs, through `Form<T>(f => f.Field(x => x.P).Component<T>())`. This is the permission picker.
- **The special parts are rung-3 slots** (spec decision 4):
  - The role permission chips and picker: `RolePermissionsSlot`.
  - The scope's child scopes and usable roles: `ScopeRelationsSlot`.
  - The assignments: `UserAssignmentsSection`.

  Each slot reads `Context` (record, version, reload, generation) and sends its own commands at `Context.Version`.
- **Scopes keep `ScopeTreeLayout`, `SerginAggregateTree` and `SerginTreeSelect`** (spec, "Trees"). Only the pane content becomes generated: the detail view and the create form. After a write that stays on the page, a generated detail view calls `Host.ChangedAsync()`, so the tree beside it reloads.

**Tech Stack:** .NET 10 (SDK pinned 10.0.404 by `global.json`), Blazor Server, MudBlazor 9.12.0, MediatR, FluentValidation, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-11-12-generated-aggregate-views-design.md`. **Read with it:** `docs/superpowers/plans/2026-11-13-generated-aggregate-views-3-carry-forward.md`. It holds Plan 3's rulings, deferred findings and final-review triage. Task 1 closes the triage. Any other deferred minor that the code of a task touches may be fixed there; the rest stay recorded.

**Series:**
- Plan 1 (#57 / SK #41): framework and Device.
- Plan 2 (#58 / SK #42): Manufacturer and DeviceModel.
- Plan 3 (#59 / SK #43, Settings #5): Settings.
- Plan 4: this plan.

## Global Constraints

- `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer: every line passes analysis. No `@code` blocks; all C# lives in `.razor.cs`. No new packages, and no `Version` on a `PackageReference`.
- Every visible text is a declared `TextDefinition` or `FieldText`.
  - UI-only texts go in the RCL (`UserUiTexts`, `RoleUiTexts`, `ScopeUiTexts`, `AssignmentUiTexts`, `ShellTexts`). Field texts go in `.Application.Configurations`.
  - A new key gets a `fa` row in `docker-compose/.files/seed/dev-seed.sql` and a line in `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/Types/catalog-keys.txt`. A deleted key loses both.
  - `PseudoLocaleSweepTests` stays green.
- The view configuration builder gets **no** method for permissions, scope, version rule or schedule name. Those are read from `CommandConfigurationRegistry`. Links carry no permission.
- Every route stays exactly as it is:
  - `/ua/users`, `/ua/users/new`, `/ua/users/{Id:guid}`
  - `/ua/roles/new` (query `scopeId`), `/ua/roles/{Id:guid}`
  - `/ua/scopes`, `/ua/scopes/new` (query `parentId`), `/ua/scopes/{Id:guid}`
- Reflection runs once, at registry build. Components read cached delegates.
- Inject `ISerginDispatcher`, never `ISender`.
  - `SendVersionedAsync` is only for a `Versioned()` root (Role, Scope): its GetOne and its version-guarded writes.
  - `MoveScope` and `DeleteScope` are `WithoutExpectedVersion()` and go through plain `SendAsync`.
  - User is not versioned.
  - A stale answer (`VersionErrors.IsStale`) shows its errors once and reloads.
- Behaviour kept unless a ruling below says otherwise (spec: converted commands keep their behaviour):
  - Rename role, rename scope and role inheritability are `Inline`.
  - Deactivate asks for confirmation.
  - The root scope offers no Move and no Delete.
  - Delete role leaves to its owner scope. Delete scope leaves to its parent.
- **Rulings made while planning** (record them in the ledger at setup):
  - The `Cascade` checkbox moves from the rename card (role) or the move card (scope) into the delete confirm dialog, the only command that reads it. `RoleUiTexts.DeleteCascadeConfirm` is deleted: the dialog shows one text, and the Cascade checkbox carries its own label. Cost if wrong: the confirm text no longer changes with the checkbox.
  - `RoleUiTexts.InheritableOnDetail` is deleted. The inline switch sits inside the detail field labelled "Inheritable", so it needs no second label. `InheritableOnCreate` becomes the create form field's label override. Cost: one helper line fewer on the detail page.
  - Scope's "New child scope" button becomes the generated self-parented create action (Task 3). It carries the create command's permission, as today.
  - "New role here" stays a `Link(...)` with no permission, as today.
  - `AppliesWhen` on a command with no reverse partner is a supported feature: it says when the command is offered. Plan 3 accepted it silently; it is now documented and tested. Cost: none; `DeleteScope` and `MoveScope` use it.
- Branches, each named `generated-aggregate-views-4`, created in Task 1:
  - SharedKernel: in `src/SharedKernel`, from `bbdc36d`. Lands as a PR in `poursh/Sergin.SharedKernel`.
  - UserAccess: in `src/Modules/Administration/UserAccess`, from `456a75f`. Lands as a PR in `poursh/Sergin.Administration.UserAccess`.
  - Root: the worktree `.claude/worktrees/gav4`, which exists already with submodules initialized.
  - Settings is untouched.
- Each task commits SharedKernel first, then UserAccess, then the root (submodule pointers and root files).
- PR order: SharedKernel, then UserAccess, then root. Before pushing any branch or opening any PR, stop and ask the user.
- Commits use the user's git identity only. No `Co-Authored-By` trailer (CLAUDE.md overrides the harness default).
- Tests need Docker up: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter <filter>`. Run the full suite at the end of Tasks 1, 4, 7 and 8.

## Review Focus

1. **Moving a scope under itself or below it.** The Move dialog never offers the scope or its descendants. The server refusing a cycle stays the guarantee. Test in Task 4: `MoveCandidates_ExcludeSelfAndDescendants`.
2. **Two administrators on one role.** One renames the role. The other's Grant, sent at the old version, is refused as stale: one error shows, the page reloads, and the permission is not granted. Test in Task 6: `RolePermissions_StaleGrant_IsRefusedAndReloads`.
3. **A reader on scope and role pages** (holds `scopes.read` / `roles.read` only):
   - Name and inheritability show as text.
   - There is no rename card, no Move, no Delete and no "New child scope".
   - "New role here" is still there.
   - Tests in Task 7: `ScopeDetail_ForAReader_HidesTheManageControls`. In Task 6: `RoleDetail_ForAReader_ShowsInheritableAsText`.
4. **Delete a scope that still owns roles, Cascade unchecked.** The server refuses. The error shows and the page stays on the scope: no leave, no tree jump. Test in Task 2: `DeleteWithValues_Refused_StaysAndReloads`, on a probe.
5. **The root scope.** No Move and no Delete are offered, on the page or in its header slot. Test in Task 7: `RootScopeDetail_OffersNoMoveOrDelete`.

---

## File Structure

SharedKernel (`src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/` unless noted):

| File | Responsibility |
|---|---|
| `Runtime/GeneratedSetProperty.cs` | outcome carries its errors (`SetPropertyOutcome(Status, Errors)`); no shared `Errors` property |
| `AggregateViewGuard.cs` | the new refusals (Tasks 1–4) |
| `Configuration/FieldSetBuilder.cs` | `FieldBuilder.Shown()` |
| `Configuration/CommandViewBuilder.cs`, `Model/CommandView.cs` | `Confirm(title, text, confirmLabel?)`, `ConfirmFields`, `BuildMove` |
| `Views/SerginConfirmValuesDialog.razor{,.cs}` (new) | a delete confirm with the command's extra shown values |
| `Configuration/AggregateViewBuilder.cs`, `Model/BelongsToView.cs` (new) | `BelongsTo<TParent, TResponse>(parentId, parentName)`; `Tree<TItem>(…)` |
| `Configuration/FormViewBuilder.cs`, `Model/FormFieldView.cs` | `Parent(x => x.P)`, `Field(x => x.P).Label(...).Component<T>()` |
| `Model/AggregateViewModel.cs`, `AggregateViewModelJoin.cs` | `BelongsTo`, `Tree`, `ChildCreate` (self-parented create), the new command fields |
| `Model/TreeView.cs` (new), `Runtime/MoveCandidates.cs` (new) | the tree read, its item reader, candidates for a move |
| `Views/SerginMoveDialog.razor{,.cs}` (new) | the Move edit dialog |
| `Configuration/ChildViewBuilder.cs`, `Model/ChildViewModel.cs`, `ChildViewModelJoin.cs` | `Section<TComponent>()`: a child drawn by its own component |
| `Views/SerginGeneratedDetailView.razor{,.cs}`, `SerginGeneratedFormView.razor{,.cs}`, `Parts/GeneratedCommandActions.cs`, `Parts/GeneratedViewSupport.cs` | wiring: parent trail, leave target, generation, `ChangedAsync`, confirms, move, sections, parent param |
| `ScheduledTasks/SerginScheduleAction.cs` | deleted (Task 8) |

UserAccess RCL (`src/Modules/Administration/UserAccess/Sergin.Administration.UserAccess.Presentation.Blazor/`):

**New:**
- `Users/UserViewConfiguration.cs`
- `Users/Assignments/Components/UserAssignmentsSection.razor{,.cs}` (renamed from `UserAssignmentsView`)
- `Roles/RoleViewConfiguration.cs`
- `Roles/Components/RolePermissionsSlot.razor{,.cs}`
- `Roles/Components/PermissionPickerInput.razor{,.cs}`
- `Scopes/ScopeViewConfiguration.cs`
- `Scopes/Components/ScopeRelationsSlot.razor{,.cs}`
- `Scopes/Components/ScopeParentValue.razor{,.cs}`

**Deleted:**
- `Users/Components/User{Create,Detail,List}View.*`
- `Users/Models/NewUserFormModel.cs`
- `Roles/Components/Role{Create,Detail}View.*`
- `Roles/Models/*`
- `Scopes/Components/Scope{Create,Detail}View.*`
- `Scopes/Models/*`

**Kept:**
- `ScopeTreeLayout`, `ScopeTreeTable`, `PermissionLabels`, `PermissionSubheader`, `NewAssignmentFormModel`.
- The pages, which change to host generated views.

UserAccess Application.Configurations: `Users/Assignments/AssignmentTexts.cs` gains `Singular` ("Assignment").

Tests (`tests/Sergin.MeterMinder.IntegrationTests.All/`):
- New: `Aggregates/GeneratedUserViewTests.cs`, `Aggregates/GeneratedRoleViewTests.cs`, `Aggregates/GeneratedScopeViewTests.cs`.
- Extended:
  - `Aggregates/AggregateViewTestTypes.cs`, which gets new probe roots, each test-only (see the hazard note in SK CLAUDE.md).
  - `AggregateViewRegistryTests`, `AggregateViewGuardTests`, `RequestRuntimeTests`, `GeneratedViewProbeTests`.
  - `Commands/Types/catalog-keys.txt`.
  - `ScheduledTasks/ScheduleProbePage.razor.cs`.

---

### Task 1: Branches, ledger rulings, Plan 3 hardening

**Files:** `Runtime/GeneratedSetProperty.cs`, `AggregateViewGuard.cs`, `AggregateViewModelJoin.cs`, `ChildViewModelJoin.cs`, the views that read `GeneratedSetProperty.Errors`; tests `AggregateViewGuardTests.cs`, `RequestRuntimeTests.cs`, `GeneratedViewProbeTests.cs` and probe types as needed.

**Interfaces — Produces:**
- `GeneratedSetProperty.SendAsync(...)` returns `SetPropertyOutcome(SetPropertyStatus Status, IReadOnlyList<Error> Errors)`, with `SetPropertyStatus { Sent, Stale, Failed, Invalid }`.
- The public `Errors` property is removed. Every caller reads `outcome.Errors`.

- [ ] **Step 1: Create the two submodule branches.** `git -C src/SharedKernel switch -c generated-aggregate-views-4` and `git -C src/Modules/Administration/UserAccess switch -c generated-aggregate-views-4`. Check that HEADs are `bbdc36d` and `456a75f`. Commit the plan file on the root branch.

- [ ] **Step 2: Write the failing tests.**
  - `GeneratedSetProperty_TwoSends_EachOutcomeCarriesItsOwnErrors`: two overlapping sends, one invalid and one stale. Each outcome's `Errors` holds only its own errors.
  - `Guard_InlineChoiceWhoseFieldIsHidden_Refuses`: an `Inline` bool or enum set-property command whose GetOne field is `Hidden()`. The message names the command and says "hidden".
  - `Guard_InlineChoiceWhoseFieldsAreReplacedBySlot_Refuses`: the same command with a `DetailFields` slot configured.
  - `Guard_AppliesWhenOnChildReversePair_Refuses`: a child `IReverseOf` pair declaring `AppliesWhen` is refused. `ChildReversePairWithoutAppliesWhen_Builds`: a child pair declaring none builds, and both commands are offered.
  - `Guard_OverviewOnListUnderRoot_Refuses`: an `IOverviewQuery` on a `ListUnder` root.
  - `Detail_DialogEdit_SaveStaleCancel`: the Dialog edit mode end to end. Open the dialog, save a valid value: sent, dialog closed, view reloaded. Save at a stale version: the dialog stays open showing the stale error once. Cancel: nothing sent. Drive it in process the way `ScheduledTasksSectionRefreshTests` renders a component. If `MudDialog` cannot open there, test `SerginSetPropertyDialog`'s submit, stale and cancel handlers through their seam, and ledger the ruling.

- [ ] **Step 3: Run them.** `--filter "FullyQualifiedName~Aggregates"`. Expected: the new tests FAIL.

- [ ] **Step 4: Implement.**
  - Remove `GeneratedSetProperty.Errors`.
  - Make the inline-choice refusal part of `AggregateViewGuard.EnsureEditable`. It needs the detail field set and the slot map: pass them in.
  - For child pairs: in `ChildViewModelJoin`, the reverse-pair check demands no `AppliesWhen` and refuses one.
  - The overview refusal goes in the `ListUnder` guard.
  - Update the SK CLAUDE.md "Edit modes" and "Reverse pairs" bullets in the same commit (no longer "a known gap", no longer "must declare it too").

- [ ] **Step 5: Run the full suite.** Expected: PASS. Commit SK, then commit the root pointer.

### Task 2: Shown fields, confirms on any command, deletes that ask for values, generation and `ChangedAsync`

**Files:** `Configuration/FieldSetBuilder.cs`, `Configuration/CommandViewBuilder.cs`, `Model/CommandView.cs`, `AggregateViewModelJoin.cs`, `AggregateViewGuard.cs`, `Views/SerginConfirmValuesDialog.razor{,.cs}` (new), `Views/SerginGeneratedDetailView.razor.cs`, `Views/Parts/GeneratedCommandActions.cs`, `Views/Parts/GeneratedListBody.razor.cs`; tests in `GeneratedViewProbeTests.cs`, `AggregateViewGuardTests.cs`, `AggregateViewRegistryTests.cs`.

**Interfaces — Produces:**
- `FieldBuilder<T>.Shown()`: shows a field hidden by default (a `Guid` or `Id`).
- `CommandViewBuilder<T>.Confirm(TextDefinition title, TextDefinition text, TextDefinition? confirmLabel = null)`: any detail-bar or row command asks before sending. For a delete, it is the same as `ConfirmDelete`. Placeholders are filled from the record, as delete confirms are today.
- `CommandView` gains `TextDefinition? ConfirmLabel` and `IReadOnlyList<FormFieldView> ConfirmFields`.
- A delete whose constructor is `(Id, extra shown values…)` gets `ConfirmFields` (the shown values) and `BuildFromValues`. `BuildFromId` stays null for it.
- `SerginConfirmValuesDialog` parameters: `Text` (string, already localized), `Fields` (`IReadOnlyList<FormFieldView>`), `ConfirmLabel`. It returns the values (`IReadOnlyDictionary<string, object?>`) on confirm, or a cancelled result.
- `AggregateViewContext.Generation` on a detail view: bumped by the header reload and after every successful write that keeps the view open.
- After such a write (a transition, set-property or move) the detail view awaits `Host.ChangedAsync()`.

- [ ] **Step 1: Write the failing tests.** Use probe roots added to `AggregateViewTestTypes.cs`:
  - `Detail_ShownIdField_RendersWithItsLabel`: `Field(x => x.Id).Shown().Label(...)` renders the id.
  - `Transition_WithConfirm_AsksFirst`: through the `GeneratedCommandActions` seam, a declined confirm sends nothing and an accepted one sends once.
  - `Registry_DeleteWithExtraValues_HasConfirmFields`: `DeleteProbeCommand(ProbeId Id, bool Cascade)` gives `ConfirmFields` = [`Cascade`], and `BuildFromValues(id, {Cascade: true})` builds the command.
  - `Guard_DeleteWithHiddenExtraValue_Refuses`: an extra value marked `NotShown` leaves the dialog unable to fill it, so it is refused, naming the command.
  - `List_DeleteWithValues_IsNotARowOrBulkAction`: no row icon, no bulk action.
  - `DeleteWithValues_Refused_StaysAndReloads` (Review Focus 4): the server refuses. The error is shown once, no `LeaveAsync`, the record reloads.
  - `Detail_HeaderReload_BumpsGeneration`, and `Detail_SetPropertySent_CallsHostChanged`: use a recording `SerginViewHost`.

- [ ] **Step 2: Run them.** Expected: FAIL.

- [ ] **Step 3: Implement.**
  - Relax the guard that demands an id-only delete constructor: allow id plus shown values. Hidden extras stay refused.
  - The detail delete with `ConfirmFields` opens `SerginConfirmValuesDialog`. Its inputs are `GeneratedInput`, bound to a `GeneratedFormValues`. On confirm it builds through `BuildFromValues`, then sends: versioned when `RequiresVersion`, else plainly.
  - A delete with `ConfirmFields` is skipped by the list's row and bulk actions. Document it beside the `AppliesWhen` bulk note in SK CLAUDE.md.
  - The confirm button label: `ConfirmLabel`, else the command's label.

- [ ] **Step 4: Run** `--filter "FullyQualifiedName~Aggregates"`. Expected: PASS. Commit SK and the root pointer.

### Task 3: `BelongsTo`, a form's parent, the tree as a list route, form field overrides

**Files:** `Configuration/AggregateViewBuilder.cs`, `Model/BelongsToView.cs` (new), `Configuration/FormViewBuilder.cs`, `Model/FormFieldView.cs`, `Model/AggregateViewModel.cs`, `AggregateViewModelJoin.cs`, `AggregateViewGuard.cs`, `Views/SerginGeneratedDetailView.razor.cs`, `Views/SerginGeneratedFormView.razor{,.cs}`, `Views/Parts/GeneratedInput.razor{,.cs}`, `Views/Parts/GeneratedViewSupport.cs`, `Views/Parts/GeneratedCommandActions.cs`; tests in the same files as Task 2.

**Interfaces — Produces:**
- `AggregateViewBuilder<TRoot>.BelongsTo<TParent, TResponse>(Func<TResponse, Guid?> parentId, Func<TResponse, string?> parentName)` gives `AggregateViewModel.BelongsTo` (`BelongsToView(Type Parent, Func<object, Guid?> ParentId, Func<object, string?> ParentName)`). Effects:
  - **Detail trail:** the parent model's list step, then `SerginBreadcrumb.Data(parentName, parentDetail(parentId))` when `parentId` is not null, then the record.
  - **Delete leaves to** the parent's detail, else the parent's `Routes.List`, else `/`.
- `FormViewBuilder<TCommand>.Parent(Expression<Func<TCommand, object?>> property)`: the property is filled from `SerginGeneratedFormView.ParentId` and not rendered. A create command that is `IUnderParent<…>` has `ParentId` as its parent property without declaring it.
- `SerginGeneratedFormView<TRoot>` gets a `[Parameter] Guid? ParentId`.
  - With a parent property and no `ParentId`, it throws on render, naming the root.
  - With `BelongsTo` and `ParentId`, the create trail is the parent's list step, then the parent's `Singular` linking to `parentDetail(ParentId)`, then the title. Cancel goes to the parent's detail.
- `FormViewBuilder<TCommand>.Field(Expression<Func<TCommand, object?>> property)` returns a `FormFieldBuilder` with `Label(TextDefinition)` and `Component<TComponent>()`.
  - The component receives `[Parameter] object? Value`, `[Parameter] EventCallback<object?> ValueChanged` and `[Parameter] FormFieldView Field`.
  - A field with a component skips the value-object input table, so a collection-typed property (`IReadOnlyCollection<Permission>`) is allowed.
- **Self-parented create:** a root whose create command is `IUnderParent<TRoot, …>` gets a detail-bar action, `AggregateViewModel.ChildCreate`. Its kind is Secondary, its label and icon are the create command's (`Command<T>().Label(...)`), and its permissions and `AnyScope` are the create command's. It links to `{Routes.Create}?parentId={id}`.
- `Routes(list: …)` is accepted for a root whose collection read is an `ITreeQuery<TRoot>` instead of an `IListOf<TRoot>`. The list step and the leave target use it. `SerginGeneratedListView` for such a root throws on render, naming the root.

- [ ] **Step 1: Write the failing tests** (probe roots: a parent probe with GetOne, and a child probe with `BelongsTo` and a create command carrying the parent id):
  - `Detail_BelongsTo_TrailNamesTheParent`: the trail steps are the parent's list label, the parent name linking to `/probe-parents/{id}`, then the record.
  - `Detail_BelongsTo_DeleteLeavesToTheParent`: the recorded `LeaveAsync` href is the parent's detail.
  - `Form_Parent_FillsTheCommandAndRendersNoInput`: a sent command carries `ParentId`, and no input is rendered for it.
  - `Form_ParentMissing_Throws`.
  - `Form_BelongsTo_CreateTrailAndCancelUseTheParent`.
  - `Form_FieldComponent_RendersAndFeedsTheCommand`: a test input component sets a collection value, and the built command carries it.
  - `Form_FieldLabel_OverridesTheFieldText`.
  - `Detail_SelfParentedCreate_LinksWithParentId`: the action is present for a holder of the create permission and absent without it.
  - `Guard_TreeRootListRoute_Accepted` and `Guard_ListRouteWithNeitherListNorTree_StillRefuses`.
  - `Guard_BelongsTo_ParentWithoutDetailRoute_Refuses`.
  - `Guard_BelongsTo_WithListUnder_Refuses`.
  - `Guard_BelongsTo_ResponseIsNotTheGetOneResponse_Refuses`.
  - `Guard_FormParent_UnknownOrNonGuidProperty_Refuses`.

- [ ] **Step 2: Run them.** Expected: FAIL.

- [ ] **Step 3: Implement.**
  - Join `BelongsTo` after every model is built. The parent model is looked up in the registry, so build it in a second pass, as `ListUnder`'s parent is resolved.
  - The parent value goes into the form values under the parent property's name. Convert `Guid` to a typed id through `ValueObjects`, as pickers do.

- [ ] **Step 4: Run** `--filter "FullyQualifiedName~Aggregates"`. Expected: PASS. Commit SK and the root pointer.

### Task 4: Move, and child sections drawn by a component

**Files:** `Configuration/AggregateViewBuilder.cs` (`Tree<TItem>`), `Model/TreeView.cs` (new), `Runtime/MoveCandidates.cs` (new), `Views/SerginMoveDialog.razor{,.cs}` (new), `Model/CommandView.cs` (`BuildMove`), `Configuration/ChildViewBuilder.cs`, `Model/ChildViewModel.cs`, `ChildViewModelJoin.cs`, `AggregateViewModelJoin.cs`, `AggregateViewGuard.cs`, `Views/SerginGeneratedDetailView.razor{,.cs}`; tests as before.

**Interfaces — Produces:**
- `AggregateViewModel.Tree`: `TreeView(ViewRequest<Func<object>> Query, Type Item, Func<object, Guid> IdOf, Func<object, Guid?> ParentIdOf, Func<object, string> TitleOf)`. It comes from the root's `ITreeQuery<TRoot>`, whose constructor takes no arguments, and its item's `Guid Id` and `Guid? ParentId`.
- The tree item's title: `AggregateViewBuilder<TRoot>.Tree<TItem>(Action<TreeViewBuilder<TItem>> configure)` with `Title(x => x.Name)`. Without it, the item's first `string` property is used.
- `CommandView.BuildMove: Func<Guid, Guid, object>?`: for an `IMoveCommand` whose constructor is `(Id, parent id)`.
- `MoveCandidates.For(TreeView tree, IReadOnlyCollection<object> items, Guid id)` returns `IReadOnlyList<(Guid Id, string Title, int Depth)>`. It is every item except `id` and its descendants, depth first, siblings by title in the current culture.
- A shown move command is a Secondary detail-bar action. Its label is required, and a move with none is refused like a transition. It opens `SerginMoveDialog` (a `MudSelect` of the candidates, labelled with the move command's parent-id `FieldText`). On confirm it sends versioned when `RequiresVersion`, else plainly. It then shows errors, reloads and calls `Host.ChangedAsync()`.
- `ChildViewBuilder<TRoot, TChild>.Section<TComponent>()`: the child is drawn by `TComponent`, which receives `[Parameter] AggregateViewContext Context` (the root's) in place of `SerginGeneratedChildSection`.
  - A section child needs only `Named(...)`.
  - Its list, GetOne, add and remove requests are not built or checked, so an unpaged `IChildListOf` is allowed.
  - Its commands are not offered by the generated views.

- [ ] **Step 1: Write the failing tests.**
  - `MoveCandidates_ExcludeSelfAndDescendants` (Review Focus 1): a tree root > a > a1, root > b. For `a`, the candidates are root and b, with depths.
  - `Detail_Move_SendsTheChosenParentAndCallsHostChanged`.
  - `Detail_Move_AppliesWhen_HidesIt`.
  - `Guard_MoveWithoutTreeQuery_Refuses`, `Guard_MoveTreeItemWithoutParentId_Refuses` and `Guard_MoveWithoutLabel_Refuses`.
  - `Registry_ChildSection_SkipsListAndCommands`: an unpaged `IChildListOf` probe child builds.
  - `Detail_ChildSection_RendersTheComponentWithContext`.
  - `Guard_ChildSectionWithRoutesOrList_Refuses`: a section child that also declares `Routes`, `List`, `Detail`, `Form` or `Command`.

- [ ] **Step 2: Run them.** Expected: FAIL.

- [ ] **Step 3: Implement** to the interfaces above. The Move dialog loads the tree when it opens, not with the record.

- [ ] **Step 4: Run the full suite.** Expected: PASS. Update SK CLAUDE.md: "Views" (Move is rendered now) and "Deferred" (drop Move and child `AppliesWhen`). Commit SK and the root pointer.

### Task 5: User and assignments

**Files:**
- UserAccess RCL: `Users/UserViewConfiguration.cs`.
- `Users/Assignments/Components/UserAssignmentsSection.razor{,.cs}`, moved from `UserAssignmentsView`. It takes `Context`, not `UserId`.
- The three `Users/Pages/*.razor`.
- Delete `Users/Components/*` and `Users/Models/NewUserFormModel.cs`.
- UserAccess Application.Configurations: `AssignmentTexts.Singular`.
- Root: `catalog-keys.txt`, `dev-seed.sql`, `Aggregates/GeneratedUserViewTests.cs`, `Shell/ViewHostRenderingTests.cs` (test name only).

**Interfaces — Consumes:**
- Task 2: `Shown()`, `Confirm(...)` and `Generation`.
- Task 4: `Section<T>()`.

- [ ] **Step 1: Write the failing tests** in `GeneratedUserViewTests`:
  - `UserList_RendersNameAndIdentifierColumns_AndNoSearch`.
  - `UserDetail_ShowsIdentifier_AndDeactivateAsksFirst`, through the actions seam.
  - `UserDetail_EmbedsAssignmentsSection`.
  - `UserDetail_HeaderReload_ReloadsAssignments`: assign a role, bump the generation, and the row appears.
  - `UserCreate_TitleIsNewUser_AndSaveOpensTheUser`.

- [ ] **Step 2: Run them.** Expected: FAIL.

- [ ] **Step 3: Implement `UserViewConfiguration`.**
  - `Title(UserUiTexts.Title)`.
  - `Routes(list: "/ua/users", detail: id => $"/ua/users/{id}", create: "/ua/users/new")`.
  - `Command<CreateUserCommand>().Label(UserUiTexts.New)` and `Form<CreateUserCommand>(f => f.Title(UserUiTexts.New))`.
  - `Command<DeactivateUserCommand>()`:
    - `.Label(UserUiTexts.Deactivate)`
    - `.Icon(Icons.Material.Outlined.PersonOff)`
    - `.Kind(SerginActionKind.Danger)`
    - `.Confirm(UserUiTexts.DeactivateTitle, UserUiTexts.DeactivateConfirm, UserUiTexts.Deactivate)`
  - `Detail<UserQueryResponse>(d => d.Field(x => x.Id).Shown().Label(UserUiTexts.Identifier))`, and the same on `List<GetUserListItem>`.
  - `Child<RoleAssignment>(c => c.Named(AssignmentTexts.Singular).Section<UserAssignmentsSection>())`.

  The section reloads when `Context.Generation` changes and after its own assign and revoke. Its markup and texts are today's.

- [ ] **Step 4: Change the pages to host generated views.** `UserListPage` hosts `<SerginGeneratedListView TRoot="User" />`. `CreateUserPage` hosts the form view. `UserDetailPage` hosts `<SerginGeneratedDetailView TRoot="User" Id="Id" />`. Add the `AssignmentTexts.Singular` key (`user-access.users.assignments.singular`) to `catalog-keys.txt` and a `fa` row in `dev-seed.sql`.

- [ ] **Step 5: Run** `--filter "FullyQualifiedName~User|FullyQualifiedName~Shell|FullyQualifiedName~PseudoLocale"`. Expected: PASS, with the existing assertions in `ViewHostRenderingTests`, `BreadcrumbRenderingTests`, `NavVisibilityTests` and `UserReachTests` unchanged. Commit UserAccess and the root.

### Task 6: Role and the permission picker

**Files:**
- UserAccess RCL: `Roles/RoleViewConfiguration.cs`, `Roles/Components/RolePermissionsSlot.razor{,.cs}`, `Roles/Components/PermissionPickerInput.razor{,.cs}`, and the two `Roles/Pages`.
- Delete `Roles/Components/Role{Create,Detail}View.*` and `Roles/Models/*`.
- Delete the keys `RoleUiTexts.InheritableOnDetail` and `DeleteCascadeConfirm`, with their seed rows and `catalog-keys.txt` lines.
- Root: `Aggregates/GeneratedRoleViewTests.cs`.

**Interfaces — Consumes:**
- Task 2: confirm values.
- Task 3: `BelongsTo`, `Form.Parent`, `Field(...).Label().Component<T>()`.

- [ ] **Step 1: Write the failing tests** in `GeneratedRoleViewTests`:
  - `RoleDetail_TrailIsScopesOwnerRole_AndOwnedByLinksTheScope`.
  - `RoleDetail_RenameIsInlineCard_InheritableIsInlineSwitch`.
  - `RoleDetail_ForAReader_ShowsInheritableAsText` (Review Focus 3).
  - `RoleDetail_Delete_AsksForCascade_AndLeavesToTheOwnerScope`.
  - `RolePermissions_GrantAndRevoke_AtTheHeldVersion`.
  - `RolePermissions_StaleGrant_IsRefusedAndReloads` (Review Focus 2).
  - `RoleCreate_TakesScopeIdFromTheQuery_AndPicksPermissions`. The page is `/ua/roles/new?scopeId=<root>`. The sent command's `OwnerScopeId` is the root, and the picked permissions are sent.

- [ ] **Step 2: Run them.** Expected: FAIL.

- [ ] **Step 3: Implement `RoleViewConfiguration`.**
  - Routes: `detail: id => $"/ua/roles/{id}"`, `create: "/ua/roles/new"`.
  - `BelongsTo<Scope, RoleQueryResponse>(r => r.OwnerScopeId, r => r.OwnerScopeName)`.
  - `Detail<RoleQueryResponse>`:
    - `Title(x => x.Name)`
    - `Field(x => x.OwnerScopeName).Label(RoleUiTexts.OwnedBy).Link(r => $"/ua/scopes/{r.OwnerScopeId}")`
    - `Field(x => x.Permissions).Hidden()`
  - `Command<RenameRoleCommand>().Label(CommonTexts.Rename).Edit(SerginEditMode.Inline)`.
  - `Command<SetRoleInheritableCommand>().Edit(SerginEditMode.Inline)`.
  - `Command<DeleteRoleCommand>().ConfirmDelete(RoleUiTexts.DeleteTitle, RoleUiTexts.DeleteConfirm)`.
  - `Command<CreateRoleCommand>().Label(RoleUiTexts.New)`.
  - `Form<CreateRoleCommand>`:
    - `Title(RoleUiTexts.New)`
    - `Parent(x => x.OwnerScopeId)`
    - `Field(x => x.IsInheritable).Label(RoleUiTexts.InheritableOnCreate)`
    - `Field(x => x.Permissions).Component<PermissionPickerInput>()`
  - `Slot<RolePermissionsSlot>(SerginViewSlot.DetailEntities)`.

  The slot and the input share today's picker markup (`PermissionSubheader`, `PermissionLabels.Sections`). The slot sends `GrantRolePermissionCommand` and `RevokeRolePermissionCommand` through `SendVersionedAsync` at `Context.Version`, shows errors, then `await Context.ReloadAsync()`. `CreateRolePage` passes `ParentId="ScopeId"`.

- [ ] **Step 4: Run** `--filter "FullyQualifiedName~Role|FullyQualifiedName~ScopedRbac|FullyQualifiedName~PseudoLocale|FullyQualifiedName~Breadcrumb"`. Expected: PASS, with `ScopedRbacPageRenderingTests` unchanged. The four role tests and `RolePages_DoNotLeakThePickerHeadingsOntoThePage` are included. Commit UserAccess and the root.

### Task 7: Scope in the tree

**Files:**
- UserAccess RCL: `Scopes/ScopeViewConfiguration.cs`, `Scopes/Components/ScopeRelationsSlot.razor{,.cs}`, `Scopes/Components/ScopeParentValue.razor{,.cs}`.
- `Scopes/Pages/ScopeDetailPage.razor` and `CreateScopePage.razor`, which keep `SerginTreeSelect`, the `@key` and the root default.
- Delete `Scopes/Components/Scope{Create,Detail}View.*` and `Scopes/Models/*`.
- Root: `Aggregates/GeneratedScopeViewTests.cs`.

**Interfaces — Consumes:** Tasks 2–4.

- [ ] **Step 1: Write the failing tests** in `GeneratedScopeViewTests`:
  - `ScopeDetail_InThePane_RendersFieldsRelationsAndHeaderSlotButtons`.
  - `ScopeDetail_RenameInline_ReloadsTheTree`: a recording host sees `ChangedAsync`.
  - `ScopeDetail_Move_UnderAnotherScope_ReloadsTheTree`.
  - `ScopeDetail_Delete_AsksForCascade_AndLeavesToTheParent`.
  - `RootScopeDetail_OffersNoMoveOrDelete` (Review Focus 5).
  - `ScopeDetail_ForAReader_HidesTheManageControls` (Review Focus 3).
  - `ScopeDetail_NewChildLinksWithParentId_NewRoleHereLinksWithScopeId`.

- [ ] **Step 2: Run them.** Expected: FAIL.

- [ ] **Step 3: Implement `ScopeViewConfiguration`.**
  - `Routes(list: "/ua/scopes", detail: id => $"/ua/scopes/{id}", create: "/ua/scopes/new")`.
  - `BelongsTo<Scope, ScopeQueryResponse>(s => s.ParentId, s => s.ParentName)`.
  - `Tree<ScopeTreeItem>(t => t.Title(x => x.Name))`.
  - `Detail<ScopeQueryResponse>(d => d.Title(x => x.Name).Field(x => x.ParentName).Label(ScopeUiTexts.Under).Component<ScopeParentValue>())`. The component renders a link to the parent, or `ScopeUiTexts.TreeRoot` when the parent is null.
  - `Command<RenameScopeCommand>().Label(CommonTexts.Rename).Edit(SerginEditMode.Inline)`.
  - `Command<MoveScopeCommand>().Label(ScopeUiTexts.Move).AppliesWhen<ScopeQueryResponse>(s => s.ParentId is not null)`.
  - `Command<DeleteScopeCommand>().ConfirmDelete(ScopeUiTexts.DeleteTitle, ScopeUiTexts.DeleteConfirm).AppliesWhen<ScopeQueryResponse>(s => s.ParentId is not null)`.
  - `Command<CreateScopeCommand>().Label(ScopeUiTexts.NewChild)` and `Form<CreateScopeCommand>(f => f.Title(ScopeUiTexts.New))`.
  - `Link(ScopeUiTexts.NewRoleHere, Icons.Material.Outlined.Badge, id => $"/ua/roles/new?scopeId={id}")`.
  - `Slot<ScopeRelationsSlot>(SerginViewSlot.DetailEntities)`: today's child-scopes `ScopeTreeTable` and the roles-usable table, loaded when `Context.Generation` changes.

  The pages:
  - `ScopeDetailPage` hosts `<SerginGeneratedDetailView TRoot="Scope" @key="Id" Id="Id" />`.
  - `CreateScopePage` hosts `<SerginGeneratedFormView TRoot="Scope" ParentId="ParentId" />`.

- [ ] **Step 4: Run the full suite.** Expected: PASS. `TreePageRenderingTests` and `ScopedRbacPageRenderingTests` keep their assertions. Commit UserAccess and the root.

### Task 8: Delete `SerginScheduleAction`, docs and skills, browser walk, carry-forward, PRs

**Files:**
- Delete `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/ScheduledTasks/SerginScheduleAction.cs`.
- `tests/.../ScheduledTasks/ScheduleProbePage.razor.cs`: call `IDialogService.ShowScheduleAsync(new SerginScheduleRequest(…))` directly.
- `.claude/CLAUDE.md` (root), `src/SharedKernel/.claude/CLAUDE.md`, `src/Modules/Administration/UserAccess/.claude/CLAUDE.md`.
- `.claude/skills/add-feature/SKILL.md`, `.claude/skills/add-module/SKILL.md`.
- `docs/superpowers/plans/2026-11-15-generated-aggregate-views-4-carry-forward.md` (new).

- [ ] **Step 1: Delete and verify.**
  - Run `grep -rn "SerginScheduleAction\|UserDetailView\|RoleDetailView\|ScopeDetailView\|UserListView\|UserCreateView\|RoleCreateView\|ScopeCreateView" src tests --include=*.cs --include=*.razor --include=*.md`. Expected: only historical mentions in `docs/` and the plan files.
  - Build.
  - Run `--filter "FullyQualifiedName~ScheduledTasks"`. Expected: PASS.

- [ ] **Step 2: Update the docs.**
  - Root CLAUDE.md: the "pages are generated" bullet now covers every aggregate, with User, Role and Scope as references for `Section`, `BelongsTo`, Move and slots. Drop "every other aggregate still uses until the later plans" and the hand-written-view references in the page/view and tree bullets.
  - SK CLAUDE.md: the four new mechanisms (Tasks 2–4) and the removed `SerginScheduleAction`. Update the "UI parts" paragraph: a page no longer adds a Schedule action by hand.
  - UserAccess CLAUDE.md: the view configurations and slots.
  - Skills: `/add-feature`'s Blazor step and `/add-module`'s RCL step describe a view configuration plus thin pages, not a hand-written view.

- [ ] **Step 3: Browser walk.** Use Playwright on the dev host at :5555 (memory: headless-browser checks).
  - Users: list, open, deactivate (confirm), assign a role, revoke it.
  - Roles: rename inline, toggle inheritable, grant and revoke a permission, delete with Cascade (leaves to the scope).
  - Scopes: tree navigation, rename (the tree label updates), move under another scope (the tree updates), new child scope, delete (leaves to the parent).
  - Device: Read profile split button, then Schedule… and View schedules (regression).
  - Write the results to the ledger.

- [ ] **Step 4: Write the carry-forward** with the rulings and deferred findings from the ledger. Run the full suite. Expected: PASS. Commit SK, UserAccess and root.

- [ ] **Step 5: Stop and ask the user before pushing.** On approval, push the three branches and open PRs in this order: SK, UserAccess, then root, each with a body in the earlier PRs' shape. After each submodule PR merges, bump the root pointers.
