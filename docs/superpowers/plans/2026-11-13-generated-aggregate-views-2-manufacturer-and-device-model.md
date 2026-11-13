# Generated Aggregate Views, Plan 2 of 4: Manufacturer and DeviceModel — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the Manufacturer aggregate and its DeviceModel child entity onto SharedKernel's generated views. This adds what children need: a generated child section, child detail and child form pages, entity overviews and the Page edit mode.

**Architecture:** Plan 1's `AggregateViewModel` gains a list of `ChildViewModel`s, one per child entity its root's typed requests name. Each is joined at host start from the child requests (`IChildListOf`, `IChildGetOneQuery`, `IAddChildCommand`, `IRemoveChildCommand`, and the child forms of `IUpdateCommand`/`ISetPropertyCommand`/`ITransitionCommand`) and a `view.Child<TChild>(...)` declaration inside the root's view configuration. Three new components read it:
- `SerginGeneratedChildSection` (embedded in the generated detail view's Entities),
- `SerginGeneratedChildDetailView<TRoot, TChild>`,
- `SerginGeneratedChildFormView<TRoot, TChild>`.

Every child write is sent at the root's version, which each child view reads first. The plan starts with a refactor of the logic the three Plan 1 views copy, before a fourth and fifth view copy it again.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9.12.0, MediatR, FluentValidation, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-11-12-generated-aggregate-views-design.md`. **Read with it:** `docs/superpowers/plans/2026-11-12-generated-aggregate-views-1-carry-forward.md`. It holds Plan 1's rulings and deferred findings; Tasks 1 and 2 close the ones assigned here.

**Series:**
- Plan 1 (landed, #57 / SK #41): framework and Device.
- Plan 2 (this): Manufacturer and DeviceModel.
- Plan 3: Settings. Enum value texts, the Dialog and Inline edit modes, root-level set-property commands, reverse pairs, the translation grid slot.
- Plan 4: UserAccess. Plus deleting `SerginScheduleAction`, docs and skills.

## Global Constraints

- `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer: every line passes analysis. No `@code` blocks; all C# lives in `.razor.cs`.
- Central Package Management: no `Version` on a `PackageReference`. No new packages.
- Every visible text is a declared `TextDefinition`/`FieldText`.
  - UI-only texts live in the RCL (`ManufacturerUiTexts`, `DeviceModelUiTexts`, `ShellTexts`), none in `.Application.Configurations`.
  - A new key gets a `fa` row in `docker-compose/.files/seed/dev-seed.sql` and a line in `catalog-keys.txt`. A deleted key loses both.
  - `PseudoLocaleSweepTests` stays green.
- The view configuration builder has **no** method for permissions, scope, version rule or schedule name. Those are read from `CommandConfigurationRegistry`.
- Every route stays exactly as it is:
  - `/dm/manufacturers`, `/dm/manufacturers/new`, `/dm/manufacturers/{Id:guid}`, `/dm/manufacturers/{Id:guid}/edit`
  - `/dm/manufacturers/{ManufacturerId:guid}/models/new`, `/models/{Id:guid}`, `/models/{Id:guid}/edit`
- Reflection runs once, at registry build. Components read cached delegates.
- Inject `ISerginDispatcher`, never `ISender`.
  - Use `SendVersionedAsync` only for a `Versioned()` root's GetOne and for version-guarded writes. Everything else uses `SendAsync` (carry-forward: the NotPublished hazard).
  - A stale answer (`VersionErrors.IsStale`) shows the errors once and reloads.
- A child write is sent at the **root's** version. A child view reads the root (versioned) **before** the child, so a change to the child between the two reads makes the version stale.
- Branches:
  - SharedKernel: branch `generated-aggregate-views-2` in the `src/SharedKernel` submodule, landing as a PR in `poursh/Sergin.SharedKernel`.
  - Root repo: branch `generated-aggregate-views-2`, in the worktree `.claude/worktrees/gav2` (already created, submodules initialized).
- Before pushing either branch or opening a PR, stop and ask the user.
- Commits use the user's git identity only. No `Co-Authored-By` trailer (CLAUDE.md overrides the harness default).
- Tests need Docker up: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter <filter>`. Run the full suite at the end of Tasks 1, 5 and 8.

## Review Focus

1. **Bulk remove of several models when one is in use.** The others are removed, each send carries the version the previous one returned, the in-use model is refused alone, and a stale answer ends the run. Test in Task 5: `ManufacturerDetail_BulkRemove_ChainsVersionsAndRefusesInUseAlone`.
2. **Someone adds a model after the page loaded, then the user renames or removes one.** One error shows, the page reloads with the new version, and nothing is sent twice. Tests:
   - Task 6: `DeviceModelDetail_StaleRemove_ReloadsAndKeepsModel`
   - Task 7: `RenameDeviceModel_StaleSave_ReloadsForm`
3. **A model page under a manufacturer id that does not exist.** The problem panel shows, with the placeholder trail still linking to `/dm/manufacturers/{id}`. Pinned by `BreadcrumbRenderingTests` (the `/models/new` and `/models/{ModelId}/edit` cases for an unseeded id), which must pass unchanged after Tasks 6 and 7.
4. **A user who may read manufacturers but not update them.** No Add model, Rename or Remove, no row checkboxes, and model rows still link to their detail page. Test in Task 5: `ManufacturerDetail_ReadOnlyUser_ShowsModelsWithoutCommands`.
5. **A child declaration that cannot work.** Host start is refused naming the configuration and the type, never a null at first render. Cases:
   - a child config naming a property its response lacks;
   - a child set-property command with no `Edit(SerginEditMode.Page)`;
   - a Page edit mode with no child edit route;
   - two Page-mode commands on one child;
   - a child with no `Named(...)`.

   Tests in Task 3: `Guard_Child*`.

---

## File Structure

SharedKernel (`src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/`):

| File | Responsibility |
|---|---|
| `Model/ViewRequest.cs` (new) | `ViewRequest<TBuild>(Type Type, RequestInvoker Invoker, TBuild Build)`: one request role, its invoker and its factory |
| `Model/AggregateViewModel.cs` | regrouped onto `ViewRequest`s; gains `EntityOverview`, `Children`, `DetailOverviewCollapsed` |
| `Model/ChildViewModel.cs`, `Model/ChildViewRoutes.cs` (new) | one child entity, as its views show it |
| `Model/SerginEditMode.cs` (new) | `Dialog`, `Inline`, `Page` |
| `Model/CommandView.cs`, `Model/CommandRole.cs`, `Model/FieldView.cs` | `EditMode`, `DisabledWhen`; roles `AddChild`, `RemoveChild`; `Format` |
| `Configuration/ChildViewBuilder.cs` (new) | `view.Child<TChild>(child => …)` |
| `Configuration/AggregateViewBuilder.cs`, `CommandViewBuilder.cs`, `FieldSetBuilder.cs` | `Child<T>`, `DetailOverviewCollapsed()`, `Edit(mode)`, `DisabledWhen`, `Label`, `Format` |
| `AggregateViewModelJoin.cs`, `ChildViewModelJoin.cs` (new), `AggregateViewGuard.cs` | the joins and the new refusals |
| `Views/Parts/GeneratedViewSupport.cs` (new) | the logic the views share: list step, record display, send from id, stale handling, placeholder arguments |
| `Views/SerginGeneratedDetailView.razor{,.cs}` | entity overview slots, child sections |
| `Views/SerginGeneratedChildSection.razor{,.cs}` + `Views/Parts/GeneratedChildListBody.razor{,.cs}` (new) | the embedded child list |
| `Views/SerginGeneratedChildDetailView.razor{,.cs}`, `Views/SerginGeneratedChildFormView.razor{,.cs}` (new) | the child's routed pages |
| `AggregateViewContext.cs` | gains `Overview` |
| `Shell/ShellTexts.cs`, `Localizations/CommonTexts.cs` | `CommonTexts.More` moves to `ShellTexts.CommandActions.More` (same key) |

DeviceManagement RCL (`src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/`):
- New: `Manufacturers/ManufacturerViewConfiguration.cs`, `Manufacturers/Components/ManufacturerOverviewSlot.razor{,.cs}`, `ManufacturerOverviewSummarySlot.razor{,.cs}`.
- The seven Manufacturer and DeviceModel pages host generated views.
- Every `Manufacturers/**/Components/*View.*` and every `Manufacturers/**/Models/*FormModel.cs` is deleted.
- The Device overview slots read `Context.Overview`.

Tests (`tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/`):
- New: `GeneratedManufacturerViewTests.cs`, `GeneratedDeviceModelViewTests.cs`.
- `AggregateViewTestTypes.cs` gains `ViewProbe` child types.
- `AggregateViewRegistryTests`, `AggregateViewGuardTests` and `RequestRuntimeTests` are extended.

---

### Task 1: Consolidate the generated views' shared logic and regroup the model

A pure refactor. The suite stays green, and no test assertion changes except the model property paths in `AggregateViewRegistryTests` and `RequestRuntimeTests`.

**Files:**
- Create: `Aggregates/Model/ViewRequest.cs`, `Aggregates/Views/Parts/GeneratedViewSupport.cs`
- Modify: `Aggregates/Model/AggregateViewModel.cs`, `AggregateViewModelJoin.cs`, the three `SerginGenerated*View.razor.cs`, `Views/Parts/GeneratedListBody.razor.cs`, `Shell/ShellTexts.cs`, `Localizations/CommonTexts.cs` (and every `CommonTexts.More` reference)
- Test: `AggregateViewRegistryTests.cs`, `RequestRuntimeTests.cs` (paths only)

**Interfaces:**
- Produces `public sealed record ViewRequest<TBuild>(Type Type, RequestInvoker Invoker, TBuild Build) where TBuild : Delegate`.
- `AggregateViewModel` replaces each `Type? X` + `RequestInvoker? XInvoker` + `BuildX` triple with one `ViewRequest<…>? X`:
  - `GetOne`: `ViewRequest<Func<Guid, object>>?`
  - `List`: `ViewRequest<Func<ListQueryInput, object>>?`
  - `Overview`: `ViewRequest<Func<object>>?`
  - `Audit`: `ViewRequest<Func<Guid, object>>?`
  - `Create` and `Update`: `ViewRequest<Func<Guid, IReadOnlyDictionary<string, object?>, object>>?`
- `LoadRecordAsync`, `BuildForm` and `SubmitFormAsync` keep their signatures.
- Produces `internal static class GeneratedViewSupport`:
  - `SerginBreadcrumb? ListStep(AggregateViewModel model, SerginUiModuleCatalog catalog)`: the nav item whose `Href` equals `Routes.List`, else `new(model.Title, list)`, else null.
  - `FieldView? TitleFieldOf(IReadOnlyList<FieldView> fields, string? titleField)`
  - `string DisplayOf(object? record, FieldView? titleField, Guid fallback)`
  - `Task<ViewSendResult> SendFromIdAsync(ISerginDispatcher dispatcher, CommandView command, Guid id, RowVersion? version)`: versioned only when `command.RequiresVersion`.
  - `Task<bool> FailedAsync(ViewSendResult result, IUiErrorPresenter presenter, Func<Task> reloadAsync)`: notifies every error, reloads on stale, and returns whether it failed.
  - `Dictionary<string, object?> ArgumentsOf(Func<object, IReadOnlyDictionary<string, object?>>? reader, object? record, string display)`: the record's bag plus `Name` = display unless the record has a `Name`.
  - Each view and `GeneratedListBody` calls these instead of its own copy.
- `ShellTexts.CommandActions.More` (key `common.actions.more`, default "More actions") replaces `CommonTexts.More`. The key is unchanged, so `catalog-keys.txt` and the `fa` row are untouched.

- [ ] **Step 1: Make the change.** Make no behaviour change: the same sends, texts, order and disabled flags.
- [ ] **Step 2: Build.** Run `dotnet build Sergin.MeterMinder.slnx`. Expected: 0 warnings, 0 errors.
- [ ] **Step 3: Run the full suite.** Expected: everything that passed on `main` passes (971 passed, 3 skipped).
- [ ] **Step 4: Commit** in SK, `"Generated views: shared view support, model grouped by request"`, then commit the root (test paths, pointer).

---

### Task 2: Entity overviews, and an overview loaded once

Manufacturer's detail page shows an overview from `GetManufacturerOverviewQueryCommand : IEntityOverviewQuery<Manufacturer, ManufacturerId>`. Plan 1 handled only the collection overview, and its Device slots each loaded it themselves (carry-forward: "overview loaded twice").

**Files:**
- Modify: `AggregateViewContext.cs`, `Model/SerginViewSlot.cs`, `Model/AggregateViewModel.cs`, `AggregateViewModelJoin.cs`, `AggregateViewGuard.cs`, `Configuration/AggregateViewBuilder.cs`, `SerginGeneratedDetailView.razor{,.cs}`, `SerginGeneratedListView.razor.cs`
- Modify (root): `Devices/Components/DeviceOverviewSlot.razor.cs`, `DeviceOverviewSummarySlot.razor.cs`
- Test: `AggregateViewRegistryTests.cs`, `GeneratedViewProbeTests.cs`, `GeneratedDeviceViewTests.cs`

**Interfaces:**
- `AggregateViewContext(object? Record, RowVersion? Version, Func<Task> ReloadAsync, IReadOnlyList<Guid> TargetIds, object? Overview = null)`.
- `SerginViewSlot` gains `DetailOverview` and `DetailOverviewSummary`.
- `AggregateViewModel` gains:
  - `ViewRequest<Func<Guid, object>>? EntityOverview`: the single `IEntityOverviewQuery<TRoot, TId>`, id-only, built with `RequestFactory.ForId`. The guard refuses one that is not id-only, naming it.
  - `bool DetailOverviewCollapsed`.
- `AggregateViewBuilder<TRoot>.DetailOverviewCollapsed()` starts the detail overview collapsed.
- Detail view:
  - After a successful record load, it sends `EntityOverview` (plain `SendAsync`). A failed overview read leaves `Overview` null, silently, as today.
  - The `DetailOverview` and `DetailOverviewSummary` slots render as `SerginAggregateDetailView`'s `Overview` and `OverviewSummary`, with `OverviewCollapsed` set from the model.
- List view: sends `Overview` once per load or reload, and passes it in `Context.Overview` to both list slots. The Device slots stop sending the query themselves.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public void Registry_ProbeEntityOverview_IsIdOnly()              // ViewProbe gains GetViewProbeOverviewQuery : IEntityOverviewQuery<ViewProbe, ViewProbeId>
[Fact] public async Task ProbeDetail_OverviewSlot_ReceivesLoadedOverview() // probe slot renders Context.Overview's figure
[Fact] public async Task DeviceList_OverviewQuery_SentOncePerLoad()     // a counting probe handler, or a dispatcher spy: 1 send for 2 slots
```

- [ ] **Step 2: Run** `--filter FullyQualifiedName~Aggregates`. Expected: the new tests FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~Aggregates|FullyQualifiedName~Shell"`. Expected: PASS.
- [ ] **Step 5: Commit** in SK, `"Generated views: entity overview, overview loaded once"`, then commit the root.

---

### Task 3: Child entities in the view configuration, registry and guard

**Files:**
- Create: `Model/ChildViewModel.cs`, `Model/ChildViewRoutes.cs`, `Model/SerginEditMode.cs`, `Configuration/ChildViewBuilder.cs`, `ChildViewModelJoin.cs`
- Modify: `Model/AggregateViewModel.cs`, `Model/CommandView.cs`, `Model/CommandRole.cs`, `Model/FieldView.cs`, `Configuration/AggregateViewBuilder.cs`, `Configuration/CommandViewBuilder.cs`, `Configuration/FieldSetBuilder.cs`, `AggregateViewModelJoin.cs`, `AggregateViewGuard.cs`, `Runtime/RequestFactory.cs`
- Create (root): `Manufacturers/ManufacturerViewConfiguration.cs`. The full declaration is below. The old pages keep hosting the old views until Tasks 4–7.
- Test: `AggregateViewTestTypes.cs` (child probe types), `AggregateViewRegistryTests.cs`, `AggregateViewGuardTests.cs`, `RequestRuntimeTests.cs`

**Interfaces (produced):**
- `public enum SerginEditMode { Dialog, Inline, Page }`.
  - `CommandViewBuilder<T>.Edit(SerginEditMode mode)` sets it; the default is `Dialog`.
  - `CommandView` gains `SerginEditMode EditMode` and `Func<object, bool>? DisabledWhen` + `TextDefinition? DisabledReason`.
  - `CommandViewBuilder<T>.DisabledWhen<TRecord>(Func<TRecord, bool> when, TextDefinition? reason = null)`. This is presentation only: the command stays callable and the pipeline still refuses. It applies wherever a record or row of `TRecord` is at hand; `TRecord` must be the child list item, the child GetOne response, or the root's equivalents, which the guard checks.
- `CommandRole` gains `AddChild` and `RemoveChild`. Defaults:
  - `AddChild`: (`CommonTexts.Add`, `Icons.Material.Filled.Add`, Primary).
  - `RemoveChild`: (`CommonTexts.Remove`, `Icons.Material.Outlined.Delete`, Danger).
  - A child `IUpdateCommand<,,,>` takes the root `Update` defaults.
  - A child transition or set-property command has no default label (refused without one, as for roots).
- `FieldBuilder<T>.Label(TextDefinition label)` shows a property that has no `FieldText` (or replaces its label). `FieldView.Label` becomes `TextDefinition`, and its callers read `FieldText.Label`.
- `FieldBuilder<T>.Format(TextDefinition text, string placeholder = "Value")` renders the value through `text`, with the value in `{placeholder}`, a number through `ICultureFormatter.Number`. It is stored as `FieldView.Format` (`TextDefinition?`) and `FormatPlaceholder` (`string`).
- `public sealed record ChildViewRoutes(Func<Guid, Guid, string>? Detail, Func<Guid, string>? Create, Func<Guid, Guid, string>? Edit)`, where the first `Guid` is always the root id.
- `AggregateViewBuilder<TRoot>.Child<TChild>(Action<ChildViewBuilder<TRoot, TChild>> configure) where TChild : class, IEntity`. `ChildViewBuilder<TRoot, TChild>` has:
  - `Named(TextDefinition singular)`: required.
  - `Title(TextDefinition title)`: the section title; defaults to `Named`.
  - `Routes(Func<Guid, Guid, string>? detail = null, Func<Guid, string>? create = null, Func<Guid, Guid, string>? edit = null)`.
  - `Command<TCommand>()`: the same `CommandViewBuilder<T>`.
  - `Detail<TResponse>(…)` and `List<TItem>(…)`: the same `FieldSetBuilder`/`ListViewBuilder`. `ListViewBuilder` gains `Empty(TextDefinition)`.
  - `Form<TCommand>(…)`: the same `FormViewBuilder`.
- `public sealed record ChildViewModel(...)` holds:
  - `Child`, `Singular`, `Title`, `Routes`;
  - `ViewRequest<Func<Guid, ListQueryInput, object>>? List` (`RequestFactory.ForChildList`) and `ListItem`, `ListBodyType` (= `typeof(GeneratedChildListBody<>)` closed over the item), `ListFields`, `ListFieldSet`, `ListTitleField`, `ListSearchPlaceholder`, `EmptyText`, `ListItemArguments`;
  - `ViewRequest<Func<Guid, Guid, object>>? GetOne` (new `RequestFactory.ForChildId(Type)`: root id, child id), `DetailFields`, `DetailTitleField`, `DetailArguments`;
  - `ViewRequest<Func<Guid, IReadOnlyDictionary<string, object?>, object>>? Add` (built with the root id) and `AddFields`, `AddTitle`;
  - `Edit`: the child `IUpdateCommand<,,,>` or the one child set-property command with `EditMode == Page`, as `ViewRequest<Func<Guid, Guid, IReadOnlyDictionary<string, object?>, object>>?` (new `RequestFactory.ForChildForm`), with `EditFields`, `EditTitle`, `EditCommand` (`CommandView?`);
  - `Commands`: every child command as a `CommandView`; `BuildFromId` becomes `Func<Guid, Guid, object>?` for children (`RequestFactory.ForChildId`), so `CommandView` gains a nullable `BuildFromIds`.
- `AggregateViewModel` gains `IReadOnlyList<ChildViewModel> Children`, ordered by child type name. A child is built for every child type named by the root's typed requests, with or without a `Child<T>` declaration, so a child with requests and no declaration is refused for its missing `Named`.

**Start refusals (new),** each naming the configuration and the type:
- A child with no `Named(...)`.
- `Child<T>()` for a type that is not a child of the root.
- A child command typed to another child or root.
- A field override naming a property the child response or item lacks.
- A set-property command (root or child) whose mode is not `Page`: "edit mode {mode} is not supported yet; declare Edit(SerginEditMode.Page)". Plan 3 lifts it.
- A root-level set-property command in any mode (Plan 3).
- A child Page-mode command or child update without a child `edit` route.
- More than one of {child update, child Page-mode command}.
- A child `create` route with no `IAddChildCommand`.
- A child `detail` route with no `IChildGetOneQuery`.
- A child route no `[Route]` declares. This reuses Plan 1's route check, sampled with two GUIDs.
- `DisabledWhen<TRecord>` with a `TRecord` that is no record of the command's views.

**Manufacturer's configuration (full):**

```csharp
internal sealed class ManufacturerViewConfiguration : IAggregateViewConfiguration<Manufacturer>
{
    public void Configure(AggregateViewBuilder<Manufacturer> view)
    {
        view.Title(ManufacturerUiTexts.Title)
            .Routes("/dm/manufacturers", id => $"/dm/manufacturers/{id}", "/dm/manufacturers/new", id => $"/dm/manufacturers/{id}/edit")
            .DetailOverviewCollapsed();

        view.Command<CreateManufacturerCommand>().Label(ManufacturerUiTexts.New);
        view.Command<DeleteManufacturerCommand>()
            .ConfirmDelete(ManufacturerUiTexts.DeleteTitle, ManufacturerUiTexts.DeleteConfirm)
            .ConfirmDeleteMany(ManufacturerUiTexts.DeleteManyTitle, ManufacturerUiTexts.DeleteManyConfirm);

        view.Slot<ManufacturerOverviewSlot>(SerginViewSlot.DetailOverview)                 // Task 5 adds the components
            .Slot<ManufacturerOverviewSummarySlot>(SerginViewSlot.DetailOverviewSummary);

        view.Detail<ManufacturerQueryResponse>(detail => detail.Title(x => x.Name));
        view.List<GetManufacturerListItem>(list => list.Title(x => x.Name).SearchPlaceholder(ManufacturerUiTexts.SearchPlaceholder));
        view.Form<CreateManufacturerCommand>(form => form.Title(ManufacturerUiTexts.New));
        view.Form<UpdateManufacturerCommand>(form => form.Title(ManufacturerUiTexts.EditTitle));

        view.Child<DeviceModel>(child =>
        {
            child.Named(DeviceModelTexts.Singular).Title(DeviceModelUiTexts.Title)
                .Routes(
                    detail: (manufacturerId, id) => $"/dm/manufacturers/{manufacturerId}/models/{id}",
                    create: manufacturerId => $"/dm/manufacturers/{manufacturerId}/models/new",
                    edit: (manufacturerId, id) => $"/dm/manufacturers/{manufacturerId}/models/{id}/edit");

            child.Command<AddDeviceModelCommand>().Label(DeviceModelUiTexts.Add);
            child.Command<RenameDeviceModelCommand>().Label(CommonTexts.Rename).Icon(Icons.Material.Outlined.Edit).Edit(SerginEditMode.Page);
            child.Command<RemoveDeviceModelCommand>()
                .ConfirmDelete(DeviceModelUiTexts.RemoveTitle, DeviceModelUiTexts.RemoveConfirm)
                .ConfirmDeleteMany(DeviceModelUiTexts.RemoveManyTitle, DeviceModelUiTexts.RemoveManyConfirm)
                .DisabledWhen<GetDeviceModelListItem>(x => x.DeviceCount > 0, DeviceModelUiTexts.InUseTooltip);

            child.List<GetDeviceModelListItem>(list =>
            {
                list.Title(x => x.Name).SearchPlaceholder(DeviceModelUiTexts.SearchPlaceholder).Empty(DeviceModelUiTexts.Empty);
                list.Field(x => x.DeviceCount).Label(DeviceModelUiTexts.DevicesColumn).Format(DeviceModelUiTexts.DeviceCount, "Count");
            });
            child.Detail<DeviceModelQueryResponse>(detail =>
            {
                detail.Title(x => x.Name);
                detail.Field(x => x.ManufacturerName).Link(x => $"/dm/manufacturers/{x.ManufacturerId}");
                detail.Field(x => x.Id).Label(DeviceModelUiTexts.Identifier);
            });
            child.Form<AddDeviceModelCommand>(form => form.Title(DeviceModelUiTexts.New));
            child.Form<RenameDeviceModelCommand>(form => form.Title(DeviceModelUiTexts.RenameTitle));
        });
    }
}
```

(Omit the two `Slot<…>` lines in this task. Task 5 adds them together with the components. The builder method names above are fixed; a reviewer checks them against this block.)

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public void Registry_Manufacturer_HasDeviceModelChild()
// model.Children single: Child == typeof(DeviceModel); List.Type == GetDeviceModelListQueryCommand; GetOne.Type == GetDeviceModelByIdQueryCommand;
// Add.Type == AddDeviceModelCommand; Edit.Type == RenameDeviceModelCommand; EditCommand.EditMode == Page.

[Fact] public void Registry_DeviceModelCommands_ReadPolicyFromConfiguration()
// Add/Remove/Rename: Permissions == [ManufacturerPermissions.Update], AnyScope true, RequiresVersion true; Remove.Selectable true;
// roles AddChild / RemoveChild / SetProperty; Remove.DisabledWhen(new GetDeviceModelListItem(id, "x", 1)) true, (…, 0) false.

[Fact] public void Registry_DeviceModelList_HasCountColumnWithFormat()
// ListFields has DeviceCount with Label == DeviceModelUiTexts.DevicesColumn, Format == DeviceModelUiTexts.DeviceCount, FormatPlaceholder == "Count".

[Fact] public void Runtime_ChildBuilders_BuildFromRootAndChildIds()
// ForChildId(GetDeviceModelByIdQueryCommand)(m, d) == new(new ManufacturerId(m), new DeviceModelInternalId(d));
// ForChildForm(RenameDeviceModelCommand)(m, d, {Name:"N"}).Name.Value == "N".

[Fact] public void Guard_ChildWithoutName_Refuses()                    // message contains config name and child type name
[Fact] public void Guard_ChildFieldOverrideOnUnknownProperty_Refuses()
[Fact] public void Guard_ChildSetPropertyWithoutPageMode_Refuses()     // message contains "Edit(SerginEditMode.Page)"
[Fact] public void Guard_ChildPageModeWithoutEditRoute_Refuses()
[Fact] public void Guard_ChildTwoPageModeCommands_Refuses()
[Fact] public void Guard_ChildCreateRouteWithoutAddCommand_Refuses()
[Fact] public void Guard_DisabledWhenOnForeignRecord_Refuses()
[Fact] public void Guard_RootSetPropertyCommand_Refuses()
```

The guard tests use new test-only child types in `AggregateViewTestTypes.cs` (`ViewProbeChild`, `ViewProbeChildId`, and its list, get-one, add, remove and set-property records). Broken configurations go in their own folders, as `UnlistedTargets`/`UnresolvedFilters` do, so no host scans them.

- [ ] **Step 2: Run** `--filter FullyQualifiedName~Aggregates`. Expected: the new tests FAIL.
- [ ] **Step 3: Implement.** `ChildViewModelJoin` mirrors `AggregateViewModelJoin`'s field, form and command code by calling the same private helpers, which become `internal static` where both need them. It does not copy them.
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~Aggregates|FullyQualifiedName~Shell|FullyQualifiedName~Localization"`. Expected: PASS. The old Manufacturer pages are untouched and still render.
- [ ] **Step 5: Commit** in SK, `"Generated views: child entities in configuration, registry and guard"`. Commit the root with `ManufacturerViewConfiguration`.

---

### Task 4: Manufacturer list on the generated view

**Files:**
- Modify: `Manufacturers/Pages/ManufacturerListPage.razor` (`<SerginGeneratedListView TRoot="Manufacturer" />`)
- Test: `GeneratedManufacturerViewTests.cs` (new)

The generated list already provides:
- the Name column, linking to the detail page (the title field) and sortable (`ManufacturerListFields.Name`);
- the Address column;
- the search placeholder;
- New manufacturer as the primary action (`ManufacturerPermissions.Update`, any scope);
- the Edit row icon, the Delete row icon and bulk Delete with the configured confirm texts.

No SharedKernel change is expected. If one is needed, it is a defect in Plan 1's list view: fix it in SK with a test.

- [ ] **Step 1: Write the failing tests** (prerender HTML, as `GeneratedDeviceViewTests` does):

```csharp
[Fact] public async Task ManufacturerList_RendersColumnsSearchAndCreate()
// contains "Search name or address…", href="/dm/manufacturers/new", ">New manufacturer", a seeded manufacturer's name linking to /dm/manufacturers/{id}

[Fact] public async Task ManufacturerList_RowCommands_EditLinkAndDelete()
// contains href="/dm/manufacturers/{id}/edit" and aria-label="Delete"
```

- [ ] **Step 2: Run.** These tests may already pass against the old view, because the generated list keeps its visible output. Record the result. The tests pin that output across the switch.
- [ ] **Step 3: Switch the page.**
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~GeneratedManufacturerViewTests|FullyQualifiedName~Shell|FullyQualifiedName~PseudoLocale"`. Expected: PASS.
- [ ] **Step 5: Commit** `"Manufacturer list on the generated view"`.

---

### Task 5: Generated child section, and Manufacturer detail on the generated view

**Files:**
- Create (SK): `Views/SerginGeneratedChildSection.razor{,.cs}`, `Views/Parts/GeneratedChildListBody.razor{,.cs}`
- Modify (SK): `SerginGeneratedDetailView.razor{,.cs}`
- Create (root): `Manufacturers/Components/ManufacturerOverviewSlot.razor{,.cs}`, `ManufacturerOverviewSummarySlot.razor{,.cs}`. They render today's two cards and the summary line from `(ManufacturerOverview?)Context.Overview`, with the same texts and the same `–` for a missing figure.
- Modify (root): `ManufacturerViewConfiguration.cs` (enable the slot line), `Manufacturers/Pages/ManufacturerDetailPage.razor` (`<SerginGeneratedDetailView TRoot="Manufacturer" Id="Id" />`)
- Test: `GeneratedManufacturerViewTests.cs`

**Interfaces:**
- `SerginGeneratedChildSection`: `[Parameter] AggregateViewModel Model`, `ChildViewModel Child`, `Guid RootId`, `RowVersion? Version`, `EventCallback OnChanged`; `public Task ReloadAsync()`. It renders `SerginEntityListView` with:
  - `TitleText = Child.Title`, `Count` = the list's total;
  - `Actions`: Add, linking to `Child.Routes.Create(RootId)`, with the Add command's permission and `AnyScope`.
- The section's body is `Child.ListBodyType` (a `GeneratedChildListBody<TItem>` over `SerginEntityList<TItem>`):
  - Columns from `Child.ListFields`. The title field links via `SerginEntityLink` to `Routes.Detail(RootId, id)`. A `Format` field renders through its text.
  - Search and sort from `Child.ListFieldSet`; `EmptyMessage` from `Child.EmptyText`.
  - Row icons:
    - the edit command (Page) links to `Routes.Edit(RootId, id)`;
    - RemoveChild confirms, then sends `BuildFromIds(RootId, id)` at `Version`.
    - Each icon is disabled while `Version is null`, or when `DisabledWhen(row)` holds (tooltip `DisabledReason`).
  - Bulk: Remove for a `Selectable` RemoveChild. It confirms with `ConfirmManyTitle`/`ConfirmManyText`, runs, and reports with `CommonTexts.BulkRemoved`.
    - The run is `public static Task<(SerginBulkOutcome Outcome, RowVersion? Version)> GeneratedChildBulk.RemoveAsync(ISerginDispatcher dispatcher, CommandView command, Guid rootId, RowVersion? version, IReadOnlyList<Guid> ids)` in `Aggregates/Runtime/GeneratedChildBulk.cs`. The test calls it directly.
    - Each send carries the version the previous one returned, and a stale answer ends the run (`SerginBulk.RunAsync(…, VersionErrors.IsStale)`).
  - Any answer but a non-stale refusal calls `OnChanged`. The root view reloads its record and version, then the section reloads.
- Detail view: for each `Model.Children` entry with a `List`, it renders a section in `Entities`, after the `DetailEntities` slot and before the scheduled-tasks section, passing the held version. Reload reloads the record and every section.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public async Task ManufacturerDetail_RendersFieldsOverviewAndModelSection()
// contains the name, the address, "Device models", the "Add model" href /dm/manufacturers/{id}/models/new,
// a model name linking to /models/{modelId}, "{n} devices", aria-label="Information"

[Fact] public async Task ManufacturerDetail_InUseModel_RemoveDisabledWithTooltip()
// a model with a device: its Remove icon is disabled and the row shows "In use by devices — cannot be removed"

[Fact] public async Task ManufacturerDetail_ReadOnlyUser_ShowsModelsWithoutCommands()
// a caller holding only manufacturers.read: model names present and linked; no "Add model", no aria-label="Rename", no "Remove", no checkbox column

[Fact] public async Task ManufacturerDetail_BulkRemove_ChainsVersionsAndRefusesInUseAlone()
// calls GeneratedChildBulk.RemoveAsync directly with a scoped ISerginDispatcher: 3 models, the middle one in use →
// Outcome.Succeeded == 2, one validation error, the returned Version equals the manufacturer's stored version, two models gone

[Fact] public async Task ManufacturerDetail_StaleDelete_ReloadsAndKeepsManufacturer()
```

Keep the existing `PageTypeRenderingTests` (`/dm/manufacturers/{id}` contains `/models/new`) and `ViewHostRenderingTests` assertions as they are.

- [ ] **Step 2: Run.** Expected: the new tests FAIL.
- [ ] **Step 3: Implement** the section and the slots, then switch the page.
- [ ] **Step 4: Run the full suite.** Expected: PASS.
- [ ] **Step 5: Commit** in SK, `"Generated child section"`. Commit the root, `"Manufacturer detail on the generated view"`.

---

### Task 6: Generated child detail view, and the DeviceModel detail page

**Files:**
- Create (SK): `Views/SerginGeneratedChildDetailView.razor{,.cs}`
- Modify (root): `Manufacturers/DeviceModels/Pages/DeviceModelDetailPage.razor` (`<SerginGeneratedChildDetailView TRoot="Manufacturer" TChild="DeviceModel" RootId="ManufacturerId" Id="Id" />`)
- Test: `GeneratedDeviceModelViewTests.cs` (new)

**Interfaces:**
- `SerginGeneratedChildDetailView<TRoot, TChild>`: `[Parameter] Guid RootId`, `Guid Id`.
- Load:
  - The root comes first, through `Model.LoadRecordAsync`, keeping its version.
  - Then the child, through `Child.GetOne`, sent with `SendAsync`.
  - A failed child load is the problem panel. A failed root load with a child that loaded leaves the version null. A guarded command then reports `Error.NotFound()` and reloads instead of sending, as today.
- Trail:
  - the list step;
  - the root step: its display, or `new(Model.Singular, Routes.Detail(RootId))` before the load, always linked by the route `RootId`;
  - the child step: its display, or `new(Child.Singular)`.
- Header: reload and copy link.
- Body:
  - `SerginEntityDetailView`, with `TitleText = Child.Singular` and `FieldsTitleData` = the child display.
  - Fields from `Child.DetailFields`.
  - Actions:
    - the edit command links to `Routes.Edit(RootId, Id)`;
    - RemoveChild confirms, sends at the root version and leaves for `Model.Routes.Detail(RootId)`;
    - a child transition sends at the root version and reloads.
  - Stale answers reload both reads.
- Ruling for the reviewer: the page gains a reload button it did not have. This is accepted parity drift, consistent with every generated detail page.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public async Task DeviceModelDetail_RendersFieldsTrailAndActions()
// contains the model name, the manufacturer name linking to /dm/manufacturers/{m}, label "ID" with the model id,
// href ".../models/{id}/edit", ">Remove", the trail Manufacturers > {manufacturer} > {model}

[Fact] public async Task DeviceModelDetail_UnknownModel_ShowsProblemPanelWithLinkedTrail()
// unseeded model under a seeded manufacturer: not-found title; trail links /dm/manufacturers/{m}

[Fact] public async Task DeviceModelDetail_StaleRemove_ReloadsAndKeepsModel()
// arrange: load the view's version, then add another model through the dispatcher; the remove answers stale, the model still exists
```

- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement**, then switch the page.
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~GeneratedDeviceModelViewTests|FullyQualifiedName~Shell|FullyQualifiedName~PseudoLocale"`. Expected: PASS.
- [ ] **Step 5: Commit** in SK, `"Generated child detail view"`. Commit the root, `"Device model detail on the generated view"`.

---

### Task 7: Generated child form view; Add model, Rename model and the Manufacturer forms

**Files:**
- Create (SK): `Views/SerginGeneratedChildFormView.razor{,.cs}`
- Modify (root): `AddDeviceModelPage.razor` (`<SerginGeneratedChildFormView TRoot="Manufacturer" TChild="DeviceModel" RootId="ManufacturerId" />`), `RenameDeviceModelPage.razor` (`… RootId="ManufacturerId" Id="Id" />`), `ManufacturerCreatePage.razor` (`<SerginGeneratedFormView TRoot="Manufacturer" />`), `ManufacturerEditPage.razor` (`… Id="Id" />`)
- Test: `GeneratedDeviceModelViewTests.cs`, `GeneratedManufacturerViewTests.cs`

**Interfaces:**
- `SerginGeneratedChildFormView<TRoot, TChild>`: `[Parameter] Guid RootId`, `Guid? Id`. No `Id` means the add form (`Child.Add`); an `Id` means the edit form (`Child.Edit`).
- It reuses `GeneratedInput` and `GeneratedFormValues`, and validates through `RequestInvoker.ValidateAsync` over the command built from the root id, the child id and the values (the same build the submit sends).
- Loads:
  - The root first, versioned. A failed root load is the problem panel, with the placeholder trail still linked by `RootId`. This applies to the add form too.
    - Ruling: today's add page kept the form and reported not-found on submit. A problem panel on an unknown manufacturer is the consistent shape, and `BreadcrumbRenderingTests` still holds.
  - Edit: then the child, prefilling each field from the child response property of the same name.
- Submit:
  - The command is sent at the root's version.
  - Add leaves for `Routes.Detail(RootId, created.Id)` when there is a detail route, else for the root's detail page. Edit leaves for `Routes.Detail(RootId, Id)`.
  - A stale answer reloads both reads.
- Title: `AddTitle`/`EditTitle`, else `ShellTexts.Forms.NewTitle`/`EditTitle` with `Child.Singular`.
- Trail:
  - the list step;
  - the root, linked;
  - edit only: the child, linked to its detail route;
  - last: the edit command's label (`Rename`) for edit, the title for add.
- Cancel goes to the child detail page (edit) or the root detail page (add).
- The Manufacturer create and edit forms need no SK change. Plan 1's form view covers them, with the `New`/`EditTitle` titles declared in Task 3.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public async Task AddDeviceModel_RendersNameInputTitleAndTrail()
// "New device model", a Name input, trail links /dm/manufacturers and /dm/manufacturers/{m}, Cancel href /dm/manufacturers/{m}

[Fact] public async Task RenameDeviceModel_PrefillsNameAndTrail()
// input value == the model's name; trail ends "Rename"; links /models/{id}

[Fact] public async Task RenameDeviceModel_StaleSave_ReloadsForm()
// as Plan 1's DeviceEdit_StaleSave_ReloadsForm: change the manufacturer after the load; one stale error; the version reloads

[Fact] public async Task ManufacturerForms_RenderCreateAndPrefilledEdit()
// /dm/manufacturers/new: "New manufacturer", Name and Address inputs; /{id}/edit: prefilled name, trail ends "Edit"
```

`BreadcrumbRenderingTests` (the unseeded `/models/new` and `/models/{ModelId}/edit` cases) and `ViewHostRenderingTests` (`/dm/manufacturers/new` → "New manufacturer", "Create", cancel `/dm/manufacturers`) must pass unchanged.

- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement**, then switch the four pages.
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~Generated|FullyQualifiedName~Shell|FullyQualifiedName~PseudoLocale|FullyQualifiedName~Validation"`. Expected: PASS.
- [ ] **Step 5: Commit** in SK, `"Generated child form view"`. Commit the root, `"Manufacturer and device model forms on the generated views"`.

---

### Task 8: Remove the hand-written views, verify in a browser, land the PRs

**Files:**
- Delete: every `.razor` and `.razor.cs` under `Manufacturers/Components/` and `Manufacturers/DeviceModels/Components/`, and `Manufacturers/Models/*FormModel.cs` and `Manufacturers/DeviceModels/Models/*FormModel.cs`, with their now-empty folders and any `_Imports.razor` `@using` that only they needed.
- Modify:
  - `ManufacturerUiTexts.cs` and `DeviceModelUiTexts.cs`: delete every text with no remaining reference (check with Grep across `src/` and `tests/`);
  - `tests/.../catalog-keys.txt` and the `fa` rows in `docker-compose/.files/seed/dev-seed.sql`: remove the same keys, and add a row for any key this plan added;
  - the docs: `src/SharedKernel/.claude/CLAUDE.md` "Generated aggregate views" (children, `Child<T>`, `SerginEditMode` and what is supported, `DisabledWhen`, `Label`/`Format`, entity overview, `Context.Overview`); `src/Modules/DeviceManagement/CLAUDE.md` (Manufacturer and DeviceModel are generated); root `.claude/CLAUDE.md` (the "Device pages are generated" bullet now names Manufacturer and DeviceModel, and `DeviceModelListView` stops being "the reference component"; point to `ManufacturerViewConfiguration` instead).
- Create: `docs/superpowers/plans/2026-11-13-generated-aggregate-views-2-carry-forward.md`, holding the execution ledger's rulings and deferred findings, as Plan 1 did.

- [ ] **Step 1: Delete and clean.** Run `dotnet build Sergin.MeterMinder.slnx`. Expected: 0 warnings, 0 errors.
- [ ] **Step 2: Run the full suite.** Expected: 0 failed. Report the counts.
- [ ] **Step 3: Browser check** (Playwright, per the `headless-browser-checks` memory, host on :5555). Walk:
  1. `/dm/manufacturers`: search, sort by name, bulk delete of two fresh manufacturers.
  2. Manufacturer detail: the overview expands and collapses, Add model creates and lands on the model page, the Rename row icon renames, the in-use Remove is disabled with its tooltip, Remove of an unused model works, bulk Remove of two models works, Delete manufacturer leaves for the list.
  3. Model detail: Rename, then Remove.
  4. Create and edit a manufacturer.
  5. No console errors on any of these pages.
- [ ] **Step 4: Commit** the docs and deletions in SK and in the root. Ask the user before pushing. Then push both branches and open the SK PR, then the root PR (body: depends on the SK PR; bump the pointer after it merges).

---

## Self-review notes

- **Spec coverage:**
  - The child section, child detail and child form pages: Tasks 5–7.
  - Child routes declared in the root configuration and checked against `[Route]`: Task 3.
  - The Page edit mode for Rename device model: Tasks 3 and 7.
  - Child writes at the root's version: Tasks 5–7.
  - Convert Manufacturer and DeviceModel: Tasks 4–8.
  - The Plan 1 carry-forward items assigned here: Task 1 (dedupe, model grouping, `CommonTexts.More`) and Task 2 (the overview loaded twice).
  - Child `rootId` routes and builders: Task 3.
- **Left to Plan 3 on purpose:** the Dialog and Inline edit modes, root-level set-property commands (the Task 3 guard refuses both meanwhile, and nothing converted so far has one), enum value texts, reverse pairs, the bool texts.
