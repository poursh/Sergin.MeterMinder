# Generated Aggregate Views, Plan 1 of 4: Framework and Device — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build SharedKernel's generated aggregate views (view configuration, registry, start guard, split-button command menus, generated list/detail/form views) and convert the Device aggregate onto them.

**Architecture:** A per-aggregate `IAggregateViewConfiguration<TRoot>` in each module's Blazor RCL declares presentation-only facts. At host start `AggregateViewRegistry` joins those with `CommandConfigurationRegistry`, `AggregateFeatureRegistry` and `TextCatalog` into one immutable `AggregateViewModel` per root, including compiled delegates for sending requests and building commands, so nothing reflects per render. Generated components (`SerginGeneratedListView<TRoot>`, `SerginGeneratedDetailView<TRoot>`, `SerginGeneratedFormView<TRoot>`) read that model and render through the existing view types (`SerginAggregateListView`, `SerginAggregateDetailView`, `SerginFormView`).

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9.9.0, MediatR, FluentValidation, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-11-12-generated-aggregate-views-design.md`

**Series:** Plan 1 (this) — framework + Device. Plan 2 — Manufacturer + DeviceModel (child section, child detail/form pages, Page edit mode). Plan 3 — Settings (enum value texts, Inline edit mode, Dialog edit mode, translation grid slot). Plan 4 — UserAccess (role permission slot, scope tree, assignments) + deleting `SerginScheduleAction`, old views, docs and skills. Each later plan is written after the previous one lands, against its real interfaces.

## Global Constraints

- `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer: every line passes analysis. No `@code` blocks; all C# in `.razor.cs`.
- No `PackageReference` `Version` attributes (Central Package Management). No new packages are needed for this plan.
- Every visible text is a declared `TextDefinition`/`FieldText`; UI-only texts in the RCL (`DeviceUiTexts`, `ScheduledTaskUiTexts`, `ShellTexts`), none in `.Application.Configurations`. `PseudoLocaleSweepTests` must stay green.
- `.Application.Configurations` gets no UI facts; the view configuration builder has **no** method for permissions, scope, version rule or schedule name.
- Every existing route stays exactly as it is (`/dm/devices`, `/dm/devices/new`, `/dm/devices/{Id:guid}`, `/dm/devices/{Id:guid}/edit`).
- Reflection runs once, at registry build. Components read cached delegates.
- Inject `ISerginDispatcher`, never `ISender`. Versioned sends through `SendVersionedAsync`; a stale answer (`VersionErrors.IsStale`) reloads.
- Split button: arrow and its menu render only when at least one menu item is visible; the schedule items need `ScheduledTaskPermissions.Manage` on the current scope **and** the command's own permission (with its `AnyScope`).
- SharedKernel changes are committed in the `src/SharedKernel` submodule on a branch `generated-aggregate-views` and land as a PR in `poursh/Sergin.SharedKernel`; root-repo changes (DeviceManagement, tests, pointer bump) on a root branch of the same name. Work in a git worktree; run `git submodule update --init --recursive` inside it first.
- Commits: user's git identity only, no `Co-Authored-By` trailer (CLAUDE.md).
- Tests run with Docker up: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter <filter>`.

## Review Focus

1. **User holds the command permission but not `permission.sys.scheduled-tasks.manage`** — Read profile shows as a plain button, no arrow, no empty menu. Test in Task 1 (`ActionBar_CommandWithHiddenMenuItems_RendersPlainButton`) and Task 6 (`DeviceDetail_WithoutManagePermission_ShowsPlainReadProfile`).
2. **Bulk Schedule… with a selection that spans devices the user can't schedule on** — the dialog receives exactly the selected targets; the run's per-item grant check refuses the rest. Test in Task 7 (`DeviceList_BulkSchedule_TargetsEverySelectedRow`).
3. **Stale version on a generated delete or edit** — the error snackbar shows once and the view reloads with the new version; nothing is sent twice. Test in Task 6 (`DeviceDetail_StaleDelete_ReloadsAndKeepsDevice`) and Task 8 (`DeviceEdit_StaleSave_ReloadsForm`).
4. **A view configuration naming a property the response does not have, or a command typed to another root** — host start refused naming both types, not a null reference at first render. Test in Task 3 (`Guard_FieldOverrideOnUnknownProperty_Refuses`, `Guard_CommandOfAnotherRoot_Refuses`).
5. **View schedules clicked while the section has not loaded yet / count zero** — the item shows "View schedules (0)", scrolls and filters without error; clearing the filter shows all schedules again. Test in Task 2 (`Section_CommandFilter_ShowsOnlyThatCommand_AndClears`).

---

## File Structure

SharedKernel (`src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/`):

| File | Responsibility |
|---|---|
| `Pages/SerginMenuItem.cs` (new) | `SerginMenuItem`, `SerginBulkMenuItem<TItem>` records |
| `Pages/SerginPageAction.cs`, `Pages/SerginBulkAction.cs` | gain `MenuItems` |
| `Pages/SerginSplitButton.razor{,.cs}` (new) | main button + arrow `MudMenu`; plain button when no visible item |
| `Pages/SerginActionVisibility.cs` | also filters menu items |
| `Pages/SerginActionBar.razor`, `Pages/SerginSelectionBar.razor{,.cs}`, `Pages/SerginActionMenu.razor` | render split buttons / nested items |
| `ScheduledTasks/SerginScheduledTasksSection.razor{,.cs}` | `CommandName` filter, `CountOf`, `Loaded`, `ElementId` |
| `ScheduledTasks/ScheduledTaskUiTexts.cs` | `ViewSchedules`, `FilteredTo`, `ClearFilter` texts |
| `Aggregates/Configuration/*.cs` (new) | `IAggregateViewConfiguration<TRoot>`, builders |
| `Aggregates/Model/*.cs` (new) | `AggregateViewModel`, `CommandView`, `FieldView`, `FormFieldView`, `AggregateViewRoutes`, enums |
| `Aggregates/AggregateViewRegistry.cs` (new) | build + lookup |
| `Aggregates/AggregateViewGuard.cs` (new) | start refusals |
| `Aggregates/Runtime/RequestInvoker.cs`, `Runtime/RequestFactory.cs`, `Runtime/ValueObjects.cs` (new) | compiled send and construction delegates |
| `Aggregates/Views/SerginGenerated{List,Detail,Form}View.razor{,.cs}` (new) + `Aggregates/Views/Parts/*` | the generated components |
| `Aggregates/AggregateViewContext.cs` (new) | what a slot/field component receives |

SharedKernel host: `Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs` registers the registry and runs the guard.

DeviceManagement RCL (`src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Devices/`): new `DeviceViewConfiguration.cs`, `Components/DeviceOverviewSlot.razor{,.cs}`, `Components/DeviceScopeFilter.razor{,.cs}`; pages switch to generated views; `Components/Device{List,Detail,Create,Edit}View.*` deleted in Task 9.

Tests (`tests/Sergin.MeterMinder.IntegrationTests.All/`): new `Aggregates/` folder (`SplitButtonRenderingTests`, `AggregateViewRegistryTests`, `AggregateViewGuardTests`, `RequestRuntimeTests`, `GeneratedDeviceViewTests`, `AggregateViewTestTypes.cs`); edits to `ScheduledTasks/ScheduledTaskUiTests.cs`, `ScheduledTasks/DeviceScheduleUiTests.cs`, `Shell/PageTypeRenderingTests.cs`.

---

### Task 1: Split button and command menu items

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Pages/SerginMenuItem.cs`, `Pages/SerginSplitButton.razor`, `Pages/SerginSplitButton.razor.cs`
- Modify: `Pages/SerginPageAction.cs`, `Pages/SerginBulkAction.cs`, `Pages/SerginActionVisibility.cs`, `Pages/SerginActionBar.razor`, `Pages/SerginSelectionBar.razor{,.cs}`, `Pages/SerginActionMenu.razor{,.cs}`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/SplitButtonRenderingTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record SerginMenuItem(TextDefinition Label, string? Icon, EventCallback OnClick, PermissionDefinition? Permission = null, bool AnyScope = false, object? LabelArguments = null);` — `LabelArguments` fills placeholders (`{Count}`) through `Localizer.Text(label, args)`.
  - `public sealed record SerginBulkMenuItem<TItem>(TextDefinition Label, string? Icon, Func<IReadOnlyList<TItem>, Task> OnRun, PermissionDefinition? Permission = null, bool AnyScope = false);`
  - `SerginPageAction` gains trailing `IReadOnlyList<SerginMenuItem>? MenuItems = null`; `SerginBulkAction<TItem>` gains trailing `IReadOnlyList<SerginBulkMenuItem<TItem>>? MenuItems = null`.
  - `SerginActionVisibility.VisibleAsync` returns actions whose `MenuItems` are already filtered to the visible ones (an action keeps showing when its own permission passes even if every menu item is hidden).
  - `SerginSplitButton` parameters: `SerginPageAction Action` (already visibility-filtered), `Variant Variant`, `Color Color`. Renders `MudButtonGroup` (main `MudButton` + `MudMenu` with `Icon=ArrowDropDown`, `AriaLabel` = `ShellTexts.Actions.More` "More actions") when `Action.MenuItems` has items, else a plain `MudButton` identical to today's markup.

- [ ] **Step 1: Write the failing tests.** Render with `HtmlRenderer` inside `factory.Services.CreateScope()` and a probe user (`ProbeUserContextFactory`, `RbacScenario.CreateGrantedCallerAsync`, as `DeviceScheduleUiTests` does):

```csharp
[Fact] public async Task ActionBar_CommandWithVisibleMenuItem_RendersSplitButton()
// Action "Read profile" (perm P) with one item (perm P). Caller holds P.
// Assert html contains "Read profile" and aria-label="More actions" exactly once.

[Fact] public async Task ActionBar_CommandWithHiddenMenuItems_RendersPlainButton()
// Item requires ScheduledTaskPermissions.Manage; caller holds only P.
// Assert "Read profile" present, "More actions" absent.

[Fact] public async Task ActionBar_CommandHidden_HidesItsMenuToo()
// Caller holds neither. Assert neither "Read profile" nor "More actions".

[Fact] public async Task SelectionBar_BulkActionWithMenuItem_RendersSplitButton()
// SerginSelectionBar with Count=3 and one bulk action with one visible item: "More actions" present.

[Fact] public async Task ActionMenu_ItemsOfAnAction_RenderAsNestedEntries()
// SerginActionMenu row menu: action label and each visible item label are both menu entries.
```

- [ ] **Step 2: Run** `dotnet test … --filter FullyQualifiedName~SplitButtonRenderingTests` — expected FAIL (compile error: `SerginMenuItem` not defined).
- [ ] **Step 3: Implement** the records, the `MenuItems` parameters, visibility filtering, `SerginSplitButton`, and use it from `SerginActionBar` (non-danger actions) and `SerginSelectionBar`. In `SerginActionMenu`, render an action's visible items as further `MudMenuItem`s directly after it (MudBlazor 9 nested menus are not needed). Add `ShellTexts.Actions.More` (`common.actions.more`, "More actions") in SharedKernel's shell texts.
- [ ] **Step 4: Run** the same filter — PASS. Then `--filter FullyQualifiedName~PageTypeRenderingTests` — PASS (no change for actions without items).
- [ ] **Step 5: Commit** in `src/SharedKernel`: `git commit -m "Split button: command menu items on action and selection bars"`.

---

### Task 2: Scheduled-tasks section filter and count

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/ScheduledTasks/SerginScheduledTasksSection.razor{,.cs}`, `ScheduledTasks/ScheduledTaskUiTexts.cs`
- Test: `tests/.../ScheduledTasks/ScheduledTaskUiTests.cs` (add), `tests/.../ScheduledTasks/ScheduleProbePage.razor{,.cs}` (pass the new parameter)

**Interfaces:**
- Produces on `SerginScheduledTasksSection`:
  - `[Parameter] public string? CommandName { get; set; }` — when set, only tasks and one-off runs with that `CommandName` show, with a chip `ScheduledTaskUiTexts.FilteredTo` ("Showing {Command}") whose close raises `CommandNameChanged`.
  - `[Parameter] public EventCallback<string?> CommandNameChanged { get; set; }`
  - `[Parameter] public EventCallback Loaded { get; set; }` — raised after every successful load.
  - `public int CountOf(string commandName)` — scheduled tasks (not one-off runs) for that command in the last load; 0 before the first.
  - `public const string ElementId = "sergin-scheduled-tasks";` on the section's root element.
- Texts: `ScheduledTaskUiTexts.ViewSchedules` = "View schedules ({Count})", `FilteredTo` = "Showing {Command}". Keys under the existing `ScheduledTaskUiTexts` category.

- [ ] **Step 1: Write the failing tests** in `ScheduledTaskUiTests` on the probe page (seed two tasks of `tst.probes.touch` and one of a second schedulable probe command `tst.probes.touch-one`):

```csharp
[Fact] public async Task Section_CommandFilter_ShowsOnlyThatCommand_AndClears()
// GET /tst/probes/{id}?schedules=tst.probes.touch (probe page binds CommandName from the query).
// Assert html contains "Showing tst.probes.touch", contains "tst.probes.touch" rows (2), does NOT contain "tst.probes.touch-one".
// GET without the query: both commands present, no "Showing".

[Fact] public async Task Section_CountOf_CountsScheduledTasksPerCommand()
// The probe page renders data-count="@section.CountOf("tst.probes.touch")" after Loaded.
// Assert the prerendered html contains data-count="2".
```

- [ ] **Step 2: Run** `--filter FullyQualifiedName~ScheduledTaskUiTests` — FAIL.
- [ ] **Step 3: Implement** the parameters, filter in `LoadAsync`'s result view (filter the displayed collections, keep the loaded ones for `CountOf`), the chip, the `id`, and the texts. Update `ScheduleProbePage` to bind `CommandName` from a `[SupplyParameterFromQuery(Name = "schedules")]` and render `data-count`.
- [ ] **Step 4: Run** `--filter FullyQualifiedName~ScheduledTask` — PASS (all existing section tests too).
- [ ] **Step 5: Commit** in `src/SharedKernel` and root (probe page/test): `"Scheduled-tasks section: command filter and per-command count"`.

---

### Task 3: View configuration API, registry and start guard

**Files:**
- Create in `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/`:
  - `Configuration/IAggregateViewConfiguration.cs`, `Configuration/AggregateViewBuilder.cs`, `Configuration/CommandViewBuilder.cs`, `Configuration/FieldSetBuilder.cs`, `Configuration/ListViewBuilder.cs`, `Configuration/FormViewBuilder.cs`
  - `Model/AggregateViewModel.cs`, `Model/CommandView.cs`, `Model/FieldView.cs`, `Model/FormFieldView.cs`, `Model/AggregateViewRoutes.cs`, `Model/CommandRole.cs`, `Model/SerginViewSlot.cs`
  - `AggregateViewContext.cs`, `AggregateViewRegistry.cs`, `AggregateViewGuard.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs`
- Test: `tests/.../Aggregates/AggregateViewTestTypes.cs`, `AggregateViewRegistryTests.cs`, `AggregateViewGuardTests.cs`

**Interfaces:**
- Produces (namespace `Sergin.SharedKernel.Presentation.Blazor.Aggregates`):
  - `public interface IAggregateViewConfiguration<TRoot> where TRoot : class, IAggregateRoot { void Configure(AggregateViewBuilder<TRoot> view); }`
  - `AggregateViewBuilder<TRoot>`:
    - `Title(TextDefinition title)` — list page title; detail/form titles default to the root's `NameOf` (`Named(...)`).
    - `Routes(string list, Func<Guid, string> detail, string? create = null, Func<Guid, string>? edit = null)`
    - `CommandViewBuilder<TCommand> Command<TCommand>() where TCommand : IBaseCommand`
    - `Detail<TResponse>(Action<FieldSetBuilder<TResponse>> configure)`
    - `List<TItem>(Action<ListViewBuilder<TItem>> configure)`
    - `Form<TCommand>(Action<FormViewBuilder<TCommand>> configure)`
    - `Slot<TComponent>(SerginViewSlot slot) where TComponent : IComponent` — `SerginViewSlot { DetailFields, DetailEntities, ListOverview, ListOverviewSummary }`.
  - `CommandViewBuilder<TCommand>`: `Label(TextDefinition)`, `Icon(string)`, `SucceededText(TextDefinition)` (placeholders filled from the loaded record's properties), `ConfirmDelete(TextDefinition title, TextDefinition text)`, `Hidden()`, `MenuItem(TextDefinition label, string? icon, Func<AggregateViewContext, Task> onClick, PermissionDefinition? permission = null)`.
  - `FieldSetBuilder<T>`: `FieldBuilder<T> Field(Expression<Func<T, object?>> property)`; `Title(Expression<Func<T, object?>> property)` — the record's display field (breadcrumb tail, fields title, schedule target label; default: first `string` property with a `FieldText`).
  - `FieldBuilder<T>`: `Link(Func<T, string> href)`, `Hidden()`, `Order(int order)`, `Component<TComponent>() where TComponent : IComponent` (receives `[Parameter] AggregateViewContext Context`, `[Parameter] object? Value`).
  - `ListViewBuilder<TItem>` : `FieldSetBuilder<TItem>` plus `Filter(string listFieldId, Type component)` — a component with `[Parameter] Guid? Value` / `EventCallback<Guid?> ValueChanged` replacing the generated picker for an `Identifier` filter.
  - `FormViewBuilder<TCommand>`: `ChildPicker<TParentRoot, TResponse>(Expression<Func<TCommand, object?>> property, Func<TResponse, Guid> parentFromRecord)` — renders a parent picker (parent root's list query) then the child picker (`IChildListOf<TParentRoot, …>` query with `RootId` = selected parent); `parentFromRecord` seeds the parent on an edit form.
  - `public sealed record AggregateViewContext(object? Record, RowVersion? Version, Func<Task> ReloadAsync, IReadOnlyList<Guid> TargetIds);`
  - `public sealed class AggregateViewRegistry { public AggregateViewModel For<TRoot>(); public AggregateViewModel? Find(Type root); public IReadOnlyCollection<AggregateViewModel> All { get; } public static AggregateViewRegistry Build(IEnumerable<Assembly> uiAssemblies, CommandConfigurationRegistry commands, AggregateFeatureRegistry aggregates, TextCatalog texts); }`
  - `AggregateViewModel` (record): `Type Root`, `TextDefinition Title`, `TextDefinition Singular`, `AggregateViewRoutes Routes`, `AggregateFeatures Features`, `Type? GetOne`, `Type? List`, `Type? Overview`, `Type? Audit`, `Type? Create`, `Type? Update`, `IReadOnlyList<CommandView> Commands`, `IReadOnlyList<FieldView> DetailFields`, `IReadOnlyList<FieldView> ListFields`, `ListFieldSet? ListFieldSet`, `IReadOnlyList<FormFieldView> CreateFields`, `IReadOnlyList<FormFieldView> UpdateFields`, `IReadOnlyDictionary<SerginViewSlot, Type> Slots`, `bool HasSchedulableCommand`.
  - `CommandView` (record): `Type Request`, `CommandRole Role` (`Create, Update, Delete, Transition, SetProperty, Move`), `TextDefinition Label`, `string? Icon`, `SerginActionKind Kind`, `IReadOnlyCollection<PermissionDefinition> Permissions`, `bool AnyScope`, `bool RequiresVersion`, `string? ScheduleName`, `bool Selectable`, `TextDefinition? SucceededText`, `TextDefinition? ConfirmTitle`, `TextDefinition? ConfirmText`, `bool Hidden`, `IReadOnlyList<CommandMenuItemView> MenuItems`.
  - `FieldView` (record): `string Name`, `FieldText Label`, `Func<object, object?> Get`, `Func<object, string>? Href`, `Type? Component`, `bool Hidden`, `int Order`.
  - `FormFieldView` (record): `string Name`, `FieldText Label`, `Type ValueType` (the constructor parameter type), `FormInputKind Kind` (`Text, Number, Switch, Date, Select, Picker, ChildPicker`), `Type? PickerRoot`, `Type? PickerParentRoot`, `Func<object, Guid>? ParentFromRecord`.
  - `public static class AggregateViewGuard { public static void Ensure(AggregateViewRegistry views, IEnumerable<Assembly> uiAssemblies); }`

Derivation rules the registry implements (the spec's "Actions by command type" table):
- For every request type in `commands.RequestTypes` with `CommandTypes.TargetOf(t)?.Root == TRoot`: queries by interface (`IGetOneQuery<,>`, `IListOf<>`, `IOverviewQuery<>`, `IAuditQuery<,>`); commands by effect (`ICreateCommand` → Create/Primary, `IUpdateCommand` → Update/Secondary, `IDeleteCommand` → Delete/Danger, `ITransitionCommand` → Transition/Secondary, `ISetPropertyCommand` → SetProperty/Secondary, `IMoveCommand` → Move/Secondary). Child-target commands are skipped in this plan (Plan 2). `ISystemCommand` never becomes an action.
- Per command, from `commands.For(t)`: `Permissions`=`RequiredPermissions`, `AnyScope`=`PermissionScope == PermissionScope.Any`, `RequiresVersion`, `ScheduleName`; `Selectable`=`CommandTypes.IsSelectable(t)`.
- Default labels/icons: Create `CommonTexts.Create`/`Icons.Material.Filled.Add`, Update `CommonTexts.Edit`/`Outlined.Edit`, Delete `CommonTexts.Delete`/`Outlined.Delete`. Transition and SetProperty have no default.
- Fields: GetOne response's and list item's public properties; label = `texts.FindField(category, name)`; a `Guid` property, or one named `Id`, is hidden unless the configuration names it; properties with no `FieldText` are hidden. Order = declaration order unless `Order(n)`.
- Form fields: the create/update command's primary-constructor parameters minus `CommandTypes.InterfaceMemberNames(t)` and `commands.HiddenProperties(t)`. Kind by `ValueObjects.PrimitiveOf(type)` (Task 4): `string` → Text, numeric → Number, `bool` → Switch, `DateTime`/`DateOnly` → Date, enum → Select, `Guid` wrapper that is some root's id type → Picker (`PickerRoot` = that root), wrapper that is a child entity's id type → ChildPicker only when configured with `ChildPicker`, else refused by the guard.

Guard refusals (each `InvalidOperationException` names the configuration and offending type/property):
1. Two configurations for one root. 2. A configuration without a parameterless constructor. 3. `Command<T>()` where `T` is not typed to the root. 4. A Transition or SetProperty command (not `Hidden()`) with no `Label`. 5. `Detail<TResponse>` where `TResponse` is not the GetOne query's response; `List<TItem>` likewise for the list item. 6. `Field(x => x.P)` where `P` is not a property of the record. 7. A configured route (list/create; detail/edit with `Guid.Empty` substituted, compared as templates `{Id:guid}`) with no matching `[Route]` among the UI assemblies' components. 8. A form field of a child id type with no `ChildPicker`. 9. A root with routes but no GetOne or list query.

- [ ] **Step 1: Write test types** in `AggregateViewTestTypes.cs`: a test-only `ViewProbe : AggregateRoot<ViewProbeId>` with `ViewProbeAggregateFeatureConfiguration` (`Audited().Versioned()`, permissions `permission.tst.view-probes.read/update/run`, texts category `Tst/ViewProbes` with `FieldText Name`, `Singular`), records `GetViewProbeQuery : IQuery<ViewProbeResponse>, IGetOneQuery<ViewProbe, ViewProbeId>`, `ViewProbeResponse(Guid Id, string Name, int Hits)`, `RunViewProbeCommand(ViewProbeId Id) : ICommand, ITransitionCommand<ViewProbe, ViewProbeId>, ISelectable` with configuration `RequirePermissions(run).Schedulable("tst.view-probes.run")`, `DeleteViewProbeCommand : IDeleteCommand…`, and a valid `ViewProbeViewConfiguration`. Build registries with `CommandConfigurationRegistry.FromConfigurationTypes(...)`, `AggregateFeatureRegistry.FromConfigurationTypes(...)`, and a `TextCatalog` from `TextSource.FromTypes(...)`.
- [ ] **Step 2: Write the failing registry tests:**

```csharp
[Fact] public void Registry_ReadsPermissionScopeVersionAndSchedule_FromCommandConfiguration()
// CommandView for RunViewProbeCommand: Role Transition, Permissions == [run], RequiresVersion == true,
// ScheduleName == "tst.view-probes.run", Selectable == true, Label == the configured label.

[Fact] public void Registry_DeleteGetsDefaultLabelKindAndIcon()
// Delete: Label == CommonTexts.Delete, Kind == Danger, Icon == Icons.Material.Outlined.Delete.

[Fact] public void Registry_DetailFields_HideGuidsAndUnlabelled_KeepDeclarationOrder()
// DetailFields visible names == ["Name"] ("Hits" has no FieldText, Id is Guid).

[Fact] public void Registry_HasSchedulableCommand_WhenAnyCommandIsSchedulable()
```

(The real-host test `Registry_RealHost_BuildsDeviceModel` is written in Task 6, once Device has a configuration.)

- [ ] **Step 3: Write the failing guard tests** — one `[Fact]` per refusal 1–9 above, each with a deliberately broken test configuration, asserting `Assert.Throws<InvalidOperationException>` and that the message contains the configuration's and the offending type's/property's names. Include `Guard_FieldOverrideOnUnknownProperty_Refuses` and `Guard_CommandOfAnotherRoot_Refuses` (Review Focus 4).
- [ ] **Step 4: Run** `--filter FullyQualifiedName~Aggregates` — FAIL (types missing).
- [ ] **Step 5: Implement** builders, model, registry and guard. Builders collect into internal state; `AggregateViewRegistry.Build` scans `uiAssemblies` for closed `IAggregateViewConfiguration<>` implementations (same scan shape as `CommandConfigurationRegistry.Build`), runs each `Configure` once, and joins. The guard runs inside `Build` for refusals 1–6, 8, 9 and in `AggregateViewGuard.Ensure` for 7 (it needs routes).
- [ ] **Step 6: Register** in `AddSerginBlazorApp`: `builder.Services.AddSingleton(provider => AggregateViewRegistry.Build(uiAssemblies, provider.GetRequiredService<CommandConfigurationRegistry>(), provider.GetRequiredService<AggregateFeatureRegistry>(), provider.GetRequiredService<TextCatalog>()))`; in `UseSerginWebUiAsync` after `EnsureCommandTypes`, resolve it and call `AggregateViewGuard.Ensure(...)`.
- [ ] **Step 7: Run** `--filter FullyQualifiedName~Aggregates` — PASS; then the full suite — PASS (no module has a configuration yet).
- [ ] **Step 8: Commit** SharedKernel + tests: `"Aggregate view configuration, registry and start guard"`.

---

### Task 4: Request runtime — compiled send and construction

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Runtime/RequestInvoker.cs`, `Runtime/RequestFactory.cs`, `Runtime/ValueObjects.cs`, `Runtime/ViewSendResult.cs`
- Modify: `Aggregates/Model/CommandView.cs`, `Model/AggregateViewModel.cs` (hold the compiled delegates), `AggregateViewRegistry.cs`
- Test: `tests/.../Aggregates/RequestRuntimeTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ViewSendResult(object? Value, RowVersion? Version, IReadOnlyList<Error> Errors) { public bool IsError => Errors.Count > 0; }`
  - `RequestInvoker`: `public static RequestInvoker For(Type requestType)` (built once per type at registry build); `Task<ViewSendResult> SendAsync(ISerginDispatcher dispatcher, object request, CancellationToken ct = default)`; `Task<ViewSendResult> SendVersionedAsync(ISerginDispatcher dispatcher, object request, RowVersion? expected, CancellationToken ct = default)`. Implementation closes `ISerginDispatcher.SendAsync<TResponse>`/`SendVersionedAsync<TResponse>` over the request's `IRequest<ErrorOr<TResponse>>` response type with an expression-compiled delegate.
  - `RequestFactory`: `Func<Guid, object> ForId(Type requestType)` — for a request whose constructor has exactly the target id parameter (`Id` of the root's id type); `Func<Guid, IReadOnlyDictionary<string, object?>, object> ForForm(Type requestType)` — id (ignored for create) plus form values by parameter name, primitives wrapped through `ValueObjects`; `Func<ListQueryInput, object> ForList(Type listQueryType)` where `ListQueryInput(Paggination Paggination, string? Term, IReadOnlyList<FilterData> Filtering, IReadOnlyList<SortingData> Sorting)` matches the `ListQuery` base constructor; `Func<Guid?, object> ForChildList(Type)` deferred to Plan 2.
  - `ValueObjects`: `static Type? PrimitiveOf(Type type)` (the single-constructor-parameter primitive of a `sealed record` wrapper, or the type itself if primitive/enum), `static object? Wrap(Type type, object? primitive)`, `static object? Unwrap(object? value)`.
  - `CommandView` gains `RequestInvoker Invoker` and `Func<Guid, object>? BuildFromId`; `AggregateViewModel` gains `RequestInvoker? GetOneInvoker`, `Func<Guid, object>? BuildGetOne`, `Func<ListQueryInput, object>? BuildList`, etc. Registry refuses (guard) a Transition/Delete command whose constructor is not id-only.

- [ ] **Step 1: Write the failing tests:**

```csharp
[Fact] public void ValueObjects_PrimitiveOf_ReadsWrappedType()
// PrimitiveOf(typeof(DeviceId)) == typeof(string); PrimitiveOf(typeof(DeviceIntenralId)) == typeof(Guid); PrimitiveOf(typeof(int)) == typeof(int).

[Fact] public void ValueObjects_WrapUnwrap_RoundTrip()
// Wrap(typeof(DeviceId), "MTR-1") is DeviceId { Value: "MTR-1" }; Unwrap(that) == "MTR-1".

[Fact] public void RequestFactory_ForId_BuildsIdOnlyCommand()
// ForId(typeof(ReadDeviceProfileCommand))(g) == new ReadDeviceProfileCommand(new DeviceIntenralId(g)).

[Fact] public void RequestFactory_ForForm_BuildsUpdateCommandFromValues()
// ForForm(typeof(UpdateDeviceCommand))(g, {DeviceId:"MTR-9", DeviceModelId:m}) equals the hand-built command.

[Fact] public async Task RequestInvoker_SendVersioned_ReturnsValueAndVersion()
// Seed a device (CatalogSeed), scope from factory.Services, invoker for GetDeviceByIdQueryCommand:
// result.IsError false, Value is DeviceQueryResponse with the seeded DeviceId, Version not null.

[Fact] public async Task RequestInvoker_NotFound_ReturnsErrors()
// Unknown id: IsError true, Errors[0].Type == ErrorType.NotFound.
```

- [ ] **Step 2: Run** `--filter FullyQualifiedName~RequestRuntimeTests` — FAIL.
- [ ] **Step 3: Implement**; wire the delegates into the registry build.
- [ ] **Step 4: Run** `--filter FullyQualifiedName~Aggregates` — PASS.
- [ ] **Step 5: Commit** `"Aggregate views: compiled request send and construction"`.

---

### Task 5: Generated detail view

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Views/SerginGeneratedDetailView.razor{,.cs}`, `Aggregates/Views/Parts/GeneratedField.razor{,.cs}`, `Aggregates/Views/Parts/GeneratedCommandActions.cs`
- Modify: `_Imports.razor`, `GlobalUsings.cs` (import `…Aggregates`, `…Aggregates.Views`)
- Test: `tests/.../Aggregates/GeneratedViewProbeTests.cs` + a probe page `tests/.../Aggregates/ViewProbeDetailPage.razor{,.cs}` (`@page "/tst/view-probes/{Id:guid}"`, `@layout SerginPageLayout`, `<SerginGeneratedDetailView TRoot="ViewProbe" Id="Id" />`) with test handlers for `GetViewProbeQuery`, `GetViewProbeAuditQuery`, `RunViewProbeCommand`, `DeleteViewProbeCommand` (in-memory, registered like `ProbeHandlers.AddTo`).

**Interfaces:**
- Consumes: Task 1 (`SerginMenuItem`, `MenuItems`), Task 2 (section `CommandName`, `CountOf`, `Loaded`, `ElementId`), Task 3 (`AggregateViewRegistry.For<TRoot>()`, `AggregateViewModel`, `CommandView`, `FieldView`, `AggregateViewContext`), Task 4 (`RequestInvoker`, `BuildFromId`, `BuildGetOne`).
- Produces: `SerginGeneratedDetailView<TRoot>` with `[Parameter] Guid Id`. Behaviour:
  - Loads GetOne through `SendVersionedAsync`; holds record and version; `Problem` from `IUiErrorPresenter.Present` on error.
  - `Title` = `model.Singular`; trail = list section step (`SerginBreadcrumb.Of` the module nav item whose `Href == model.Routes.List`, found through `SerginUiModuleCatalog`) + `SerginBreadcrumb.Data(display field)` or `new(model.Singular)` before load.
  - Header: `SerginReloadButton`, `SerginCopyLinkButton`, and `SerginInfoButton` when `model.Audit` is set (loads audit through its invoker; `Version` = held version).
  - Fields: `SerginDetailField` per visible `FieldView` (link when `Href`, component through `DynamicComponent` with `Context`/`Value` parameters); `FieldsTitleData` = display field; `SerginViewSlot.DetailFields` replaces the fields when configured.
  - Actions, in `model.Commands` order minus `Hidden`, Danger last: Update → `Href = Routes.Edit(Id)`; Transition → `OnClick` sends `BuildFromId(Id)` (`SendVersionedAsync` with the held version when `RequiresVersion`, else `SendAsync`), button `Disabled` while running, success snackbar `SucceededText` with the record's properties as arguments, errors via `Notify(result.Errors)`, reload on stale; Delete → `ConfirmDeleteAsync(Localizer, ConfirmTitle ?? CommonTexts.Delete, ConfirmText ?? ShellTexts.Actions.DeleteConfirm, record)` then versioned send then `Host.LeaveAsync(Routes.List)`; stale reloads. Each action carries `Permission` (first of `Permissions`) and `AnyScope`.
  - Schedulable command (`ScheduleName` set) gets `MenuItems`: Schedule… (`ScheduledTaskUiTexts.ScheduleAction`, `Icons.Material.Outlined.Schedule`, opens `ShowScheduleAsync(new SerginScheduleRequest(schema, Label, () => (IBaseCommand)BuildFromId(Id), [new ScheduledTaskTarget(Id, null)], [display]))`, then `section.ReloadAsync()`), and View schedules (`ScheduledTaskUiTexts.ViewSchedules`, `LabelArguments = new { Count = section.CountOf(name) }`, sets the section's `CommandName` and scrolls with MudBlazor `IScrollManager.ScrollToFragmentAsync(SerginScheduledTasksSection.ElementId, ScrollBehavior.Smooth)`). Both items: `Permission = ScheduledTaskPermissions.Manage` is checked once in `OnInitializedAsync` through `IScopeAuthorization.CurrentUserHasAsync` (items omitted without it); each item also carries the command's own `Permission`/`AnyScope`. Configured `MenuItems` from the view configuration are appended.
  - `Entities`: the `DetailEntities` slot, then `SerginScheduledTasksSection` (`Schema` = the module schema of `TRoot`, from `SerginUiModuleCatalog`; `TargetId = Id`; `@bind-CommandName`; `Loaded` → `StateHasChanged`) when `model.HasSchedulableCommand`.

- [ ] **Step 1: Write the failing tests** (prerender HTML via `GET`, as `PageTypeRenderingTests` does):

```csharp
[Fact] public async Task ProbeDetail_RendersFieldsHeaderAndActions()
// Contains the probe's Name, aria-label="Reload", aria-label="Copy link", aria-label="Information",
// "Run probe" (configured label), ">Delete".

[Fact] public async Task ProbeDetail_SchedulableCommand_RendersSplitButtonWithScheduleItems()
// Caller holds run + Manage: aria-label="More actions" present; "Scheduled tasks" section present.

[Fact] public async Task ProbeDetail_WithoutManagePermission_RendersPlainRunButton()
// Caller holds run only: "Run probe" present, "More actions" absent.

[Fact] public async Task ProbeDetail_WithoutRunPermission_HidesRunButton()

[Fact] public async Task ProbeDetail_UnknownId_ShowsProblemPanel()
// Contains the not-found title from ErrorTexts, no action buttons.
```

Behaviour through clicks (stale delete, run snackbar, View schedules scroll) is covered against the real Device page in Task 6 and the browser check in Task 9.

- [ ] **Step 2: Run** `--filter FullyQualifiedName~GeneratedViewProbeTests` — FAIL.
- [ ] **Step 3: Implement** the component. Keep `.razor` markup-only: the generic body that needs runtime types is built in the code-behind with `RenderTreeBuilder` (e.g. `DynamicComponent`), not `@code`.
- [ ] **Step 4: Run** — PASS; `--filter FullyQualifiedName~Shell` — PASS.
- [ ] **Step 5: Commit** `"Generated detail view"`.

---

### Task 6: Device detail on the generated view

**Files:**
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Devices/DeviceViewConfiguration.cs`
- Modify: `Devices/Pages/DeviceDetailPage.razor` (`<SerginGeneratedDetailView TRoot="Device" Id="Id" />`), `_Imports.razor` if needed
- Test: `tests/.../ScheduledTasks/DeviceScheduleUiTests.cs`, `tests/.../Shell/PageTypeRenderingTests.cs`, `tests/.../Aggregates/GeneratedDeviceViewTests.cs`, `tests/.../Aggregates/AggregateViewRegistryTests.cs` (add `Registry_RealHost_BuildsDeviceModel`)

**Interfaces:**
- Consumes: Tasks 3–5.
- Produces: `internal sealed class DeviceViewConfiguration : IAggregateViewConfiguration<Device>` declaring: `Title(DeviceUiTexts.Title)`; `Routes("/dm/devices", id => $"/dm/devices/{id}", "/dm/devices/new", id => $"/dm/devices/{id}/edit")`; `Command<ReadDeviceProfileCommand>().Label(DeviceUiTexts.ReadProfile).Icon(Icons.Material.Outlined.Sensors).SucceededText(DeviceUiTexts.ReadProfileDone)`; `Command<DeleteDeviceCommand>().ConfirmDelete(DeviceUiTexts.DeleteTitle, DeviceUiTexts.DeleteConfirm)`; `Detail<DeviceQueryResponse>(d => { d.Title(x => x.DeviceId); d.Field(x => x.DeviceModelName).Link(x => $"/dm/manufacturers/{x.ManufacturerId}/models/{x.DeviceModelId}"); d.Field(x => x.ManufacturerName).Link(x => $"/dm/manufacturers/{x.ManufacturerId}"); })`. (List and form parts are added in Tasks 7–8.)

- [ ] **Step 1: Update/write the failing tests:**

```csharp
// DeviceScheduleUiTests
[Fact] public async Task DeviceDetail_ShowsReadProfileSplitButton()
// Contains "Read profile", aria-label="More actions", "Scheduled tasks". Does NOT contain a standalone
// button text "Schedule…" outside the menu (menu content is not prerendered: assert "Schedule…" absent from html).
[Fact] public async Task DeviceDetail_WithoutManagePermission_ShowsPlainReadProfile()
// Caller with DevicePermissions.ReadProfile but not Manage: "Read profile" present, "More actions" absent, "Scheduled tasks" absent.
// Keep DeviceDetail_ShowsEngineOffLine and DeviceDetail_WithoutReadProfilePermission_HidesBoth unchanged.

// GeneratedDeviceViewTests (dispatch-level, no browser)
[Fact] public async Task DeviceDetail_StaleDelete_ReloadsAndKeepsDevice()
// Using the generated component's delete path through a test harness is not possible server-side;
// assert instead at the dispatcher level the exact send the view makes: CommandView for DeleteDeviceCommand
// RequiresVersion == true, and VersionedDispatch with a stale version returns VersionErrors.Stale and the device stays.

// AggregateViewRegistryTests
[Fact] public void Registry_RealHost_BuildsDeviceModel() // as described in Task 3 Step 2
```

`PageTypeRenderingTests.DeviceDetailPage_RendersInfoIcon_Actions_AndFields` keeps its existing assertions unchanged (edit href, Delete, manufacturer link, `maker-{marker}` text) — it must pass against the generated page.

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~DeviceScheduleUiTests|FullyQualifiedName~PageTypeRenderingTests|FullyQualifiedName~GeneratedDeviceViewTests|FullyQualifiedName~AggregateViewRegistryTests"` — FAIL.
- [ ] **Step 3: Implement** the configuration and switch the page.
- [ ] **Step 4: Run** the same filter — PASS; then `--filter FullyQualifiedName~PseudoLocaleSweepTests` — PASS.
- [ ] **Step 5: Commit** root repo: `"Device detail on the generated view"`.

---

### Task 7: Generated list view and Device list

**Files:**
- Create: `src/SharedKernel/…/Aggregates/Views/SerginGeneratedListView.razor{,.cs}`, `Aggregates/Views/Parts/GeneratedListBody.razor{,.cs}` (generic `TItem`), `Aggregates/Views/Parts/GeneratedFilterPanel.razor{,.cs}`
- Create (DM RCL): `Devices/Components/DeviceOverviewSlot.razor{,.cs}` (the overview cards and summary moved verbatim from `DeviceListView`, loading `GetDeviceOverviewQueryCommand` itself), `Devices/Components/DeviceScopeFilter.razor{,.cs}` (the scope select moved from `DeviceListView`, `[Parameter] Guid? Value`, `EventCallback<Guid?> ValueChanged`)
- Modify: `Devices/DeviceViewConfiguration.cs`, `Devices/Pages/DeviceListPage.razor`
- Test: `tests/.../Shell/PageTypeRenderingTests.cs` (existing list facts), `tests/.../Aggregates/GeneratedDeviceViewTests.cs`, `tests/.../Lists/` (unchanged, must pass)

**Interfaces:**
- Consumes: Tasks 1, 3, 4.
- Produces: `SerginGeneratedListView<TRoot>` (no parameters). Renders `SerginAggregateListView` with `TitleText = model.Title`, trail = the list nav step, header `SerginReloadButton`, `Overview`/`OverviewSummary` from slots, actions = Create (Primary, `Href = Routes.Create`), and a `GeneratedListBody<TItem>` (closed over the list item type through `RenderTreeBuilder.OpenComponent(typeof(GeneratedListBody<>).MakeGenericType(itemType))`) that renders `SerginAggregateList<TItem>` with:
  - columns = visible `ListFields`; header `MudTableSortLabel` with `SortLabel` = the `ListFieldSet` id whose name equals the property camel-cased, when that field is `Sortable`; the display field links to `Routes.Detail(item.Id)` (only that column links);
  - `Load` = `BuildList(ListQueryInput)` sent through `RequestInvoker.SendAsync`, `PageIndex = state.Page + 1` (comment why);
  - `Searchable` = true unless the list query's `ListFieldSet` is null;
  - filter panel per `ListFieldSet` field with `FilterModes`: `Identifier` + `equals` → a `MudSelect<Guid?>` filled from the root whose id type matches the field name (`manufacturerId` → Manufacturer list query; `deviceModelId` → child list under the selected `manufacturerId`, disabled until set) or the configured `Filter(id, component)`; `Instant` + `between` → `MudDateRangePicker` labelled by the field's `FieldText`; chips via `BuildFilters`/`OnFilterRemoved`/`OnFiltersReset`; pickers load on `OnFilterPanelOpened`;
  - row commands: Update icon link, Delete icon (confirm + versioned delete reading the version just before, as today), and a `SerginActionMenu` holding Transition commands with their menu items;
  - bulk actions: every `Selectable` command (`SerginBulk.RunAsync` per item, `SerginBulkOutcome.Report`), schedulable ones with a `SerginBulkMenuItem` Schedule… (targets = selected ids, labels = display field) — no View schedules on the list.
- `DeviceViewConfiguration` adds: `Slot<DeviceOverviewSlot>(SerginViewSlot.ListOverview)`; `List<GetDeviceListItem>(l => { l.Title(x => x.DeviceId); l.Filter(DeviceListFields.ScopeId, typeof(DeviceScopeFilter)); })`.

- [ ] **Step 1: Write the failing tests:**

```csharp
[Fact] public async Task DeviceList_RendersColumnsSortLabelsFilterAndBulk()
// GET /dm/devices: "Device ID", "Model", "Manufacturer" headers; aria-label="Advanced filter"; "New device";
// mud-checkbox present (bulk). PageTypeRenderingTests' two list facts keep passing unchanged.

[Fact] public void DeviceList_BulkSchedule_TargetsEverySelectedRow()
// Registry-level: the bulk Read profile action for ReadDeviceProfileCommand has one menu item (Schedule…);
// invoking its OnRun with three GetDeviceListItem builds SerginScheduleRequest.Targets with exactly those three ids
// (expose the request builder as internal static GeneratedListBody<TItem>.ScheduleRequestFor(model, command, items) and test it).

[Fact] public async Task DeviceList_OverviewSlot_Prerenders()
// aria-label="Overview" present, DeviceUiTexts.OverviewDevices default text present.
```

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~GeneratedDeviceViewTests|FullyQualifiedName~PageTypeRenderingTests"` — FAIL.
- [ ] **Step 3: Implement** the SharedKernel components, the two Device slot components, the configuration additions, and switch `DeviceListPage`.
- [ ] **Step 4: Run** that filter, then `--filter "FullyQualifiedName~Lists|FullyQualifiedName~PseudoLocaleSweepTests|FullyQualifiedName~Breadcrumb"` — PASS.
- [ ] **Step 5: Commit** SharedKernel (`"Generated list view"`) and root (`"Device list on the generated view"`).

---

### Task 8: Generated form view and Device create/edit

**Files:**
- Create: `src/SharedKernel/…/Aggregates/Views/SerginGeneratedFormView.razor{,.cs}`, `Aggregates/Views/Parts/GeneratedInput.razor{,.cs}`
- Modify: `Devices/DeviceViewConfiguration.cs`, `Devices/Pages/DeviceCreatePage.razor`, `Devices/Pages/DeviceEditPage.razor`
- Test: `tests/.../Aggregates/GeneratedDeviceViewTests.cs`, `tests/.../Validation/FormValidatorTests.cs` and `ValidationProblemRenderingTests.cs` (must stay green)

**Interfaces:**
- Consumes: Tasks 3, 4; `ISerginFormValidator.ValidateAsync<TRequest>(request, propertyName)`.
- Produces: `SerginGeneratedFormView<TRoot>` with `[Parameter] Guid? Id` — null means create (uses `model.Create`, `CreateFields`), set means edit (uses `model.Update`, `UpdateFields`, loads GetOne versioned, renders the form only once a version is held, prefills each field from the record's property of the same name, `ChildPicker` parent from `ParentFromRecord`). Renders `SerginFormView` (`Model` = an internal values dictionary holder, `Validation` = per-field func calling `ValidateAsync` on `BuildForm(Id, values)` — the same `ToCommand` for validation and submit), `SubmitLabelText` = `CommonTexts.Create`/`CommonTexts.Save`, `CancelHref` = `Routes.List`/`Routes.Detail(Id)`. Title = `ShellTexts.Forms.NewTitle` ("New {Name}") / `ShellTexts.Forms.EditTitle` ("Edit {Name}") with `Name` = localized `Singular` — unless the configuration sets `Form<T>(f => f.Title(text))`, which Device uses to keep `DeviceUiTexts.New`/`DeviceUiTexts.EditTitle`. Submit: create → `SendAsync`, on success `Host.LeaveAsync(Routes.Detail(response.Id))` (`ICreatedResponse`); edit → `SendVersionedAsync` at the held version, on success leave to detail, stale → `Notify` + reload. Errors: `ErrorPresenter.Notify(result.Errors)`.
- Inputs (`GeneratedInput`): Text → `MudTextField<string>` `Immediate="true"`, label/placeholder from the `FieldText`; Number/Switch/Date/Select analogous; Picker → `MudSelect<Guid>` over the picker root's list query (first page of 100, display field); ChildPicker → parent `MudSelect<Guid>` (view state, not on the command, `For` pointing at it) + child `MudSelect<Guid>` disabled until a parent is chosen.
- `FormViewBuilder<TCommand>` gains `Title(TextDefinition)`. `DeviceViewConfiguration` adds `Form<CreateDeviceCommand>(f => { f.Title(DeviceUiTexts.New); f.ChildPicker<Manufacturer, DeviceQueryResponse>(x => x.DeviceModelId, r => r.ManufacturerId); })` and the same for `UpdateDeviceCommand` with `DeviceUiTexts.EditTitle`.

- [ ] **Step 1: Write the failing tests:**

```csharp
[Fact] public async Task DeviceCreate_RendersGeneratedInputs()
// GET /dm/devices/new: label "Device ID", "Manufacturer" select, "Model" select (disabled), button "Create",
// cancel href "/dm/devices". Breadcrumb contains "New device".

[Fact] public async Task DeviceEdit_PrefillsFromRecord()
// Seed device; GET /dm/devices/{id}/edit: input value equals the seeded DeviceId; "Save"; cancel href to detail.

[Fact] public async Task DeviceEdit_StaleSave_ReloadsForm()
// Dispatcher-level: the form's submit path for UpdateDeviceCommand requires a version (CommandView.RequiresVersion);
// sending at an old version returns VersionErrors.Stale and the record is unchanged.

[Fact] public async Task DeviceCreate_DuplicateId_ValidationMessageFromPipeline()
// ISerginFormValidator.ValidateAsync(new CreateDeviceCommand(existingId, model), "DeviceId") returns the
// "'Device ID' is already in use." message — the form's per-field func is this call.
```

- [ ] **Step 2: Run** `--filter FullyQualifiedName~GeneratedDeviceViewTests` — FAIL.
- [ ] **Step 3: Implement**, switch both pages.
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~GeneratedDeviceViewTests|FullyQualifiedName~Validation|FullyQualifiedName~Breadcrumb|FullyQualifiedName~PseudoLocaleSweepTests"` — PASS.
- [ ] **Step 5: Commit** SharedKernel (`"Generated form view"`) and root (`"Device create and edit on the generated form"`).

---

### Task 9: Remove Device's hand-written views, verify in a browser, land the PRs

**Files:**
- Delete: `src/Modules/DeviceManagement/…/Devices/Components/DeviceListView.razor{,.cs}`, `DeviceDetailView.razor{,.cs}`, `DeviceCreateView.razor{,.cs}`, `DeviceEditView.razor{,.cs}`, `Devices/Models/*FormModel.cs` no longer referenced
- Modify: `src/Modules/DeviceManagement/CLAUDE.md` (Device views are generated; the view configuration is the place for Device UI facts), `src/SharedKernel/.claude/CLAUDE.md` (new "Generated aggregate views" section: configuration, registry, guard refusals, split button, customization rungs), root `.claude/CLAUDE.md` ("Blazor UI conventions": a bullet pointing at the generated views and the SharedKernel section; leave the hand-written conventions in place until Plan 4)

- [ ] **Step 1: Delete** the files; `dotnet build Sergin.MeterMinder.slnx` — expected: 0 warnings, 0 errors.
- [ ] **Step 2: Run the full suite** `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj` — expected: all pass except the TickerQ licence-gated skips.
- [ ] **Step 3: Browser check** (Playwright-Python, chromium-1129, host on :5555, as in the headless-browser memory): on `/dm/devices/{id}` click Read profile (success snackbar), open the arrow → Schedule… → save a daily schedule (section lists it), arrow → "View schedules (1)" (page scrolls, chip "Showing dm.devices.read-profile", close chip shows all); Delete with confirm; on `/dm/devices` select 3 rows → bulk Read profile, bulk arrow → Schedule… (dialog lists 3 targets); filter by manufacturer → model → created range; create and edit a device. Expected: every step as described, no console errors.
- [ ] **Step 4: Update the three CLAUDE.md files.**
- [ ] **Step 5: Commit** SharedKernel docs, push the SharedKernel branch, open its PR (`gh pr create -R poursh/Sergin.SharedKernel`). After it merges, bump the pointer in root, commit root changes, push and open the root PR.

---

## Self-Review Notes

- Spec coverage for Plan 1: configuration/registry/guard (Task 3), runtime (Task 4), detail/list/form (Tasks 5, 7, 8), split button + Schedule…/View schedules (Tasks 1, 2, 5, 7), customization rungs 1–3 exercised by Device (links, filter component, overview slot, picker override), routes unchanged and checked (guard refusal 7), docs (Task 9). Deferred by design to Plans 2–4: child sections and child pages, edit modes (Dialog/Inline/Page), enum value texts, Move, reverse-pair toggles, the four special slots, deleting `SerginScheduleAction` (still used by `ScheduleProbePage`) and the `/add-feature`/`/add-module` skill updates.
- `IReverseOf` pairs and `IMoveCommand` map to `CommandRole` values in Task 3 but render no action until a module needs them (Plans 3–4).
