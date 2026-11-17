# Generated Tree Views Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate a tree-shaped aggregate's tree panel, prompt page, children section and parent link from its view configuration, and move UserAccess's Scope onto them.

**Architecture:** `TreeViewBuilder<TItem>` gains icon and text settings that land on `TreeView`. Two new SharedKernel components, `SerginGeneratedTree<TRoot>` (wrapping `SerginAggregateTree<object>`) and `SerginGeneratedTreePrompt<TRoot>`, read the model through `AggregateViewRegistry`. Their derived values come from one pure, unit-tested `GeneratedTreeParts`. The generated detail and form views render `SerginTreeSelect` themselves. The detail view renders a generated children section, whose order comes from a pure `TreeDescendants`. A detail field gains `WhenEmpty(text)`.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9.9, xUnit + Testcontainers integration tests.

**Spec:** `docs/superpowers/specs/2026-11-17-generated-tree-views-design.md`

## Global Constraints

- `TreatWarningsAsErrors`, `AnalysisMode=All` and Sonar: the build fails on any analyzer warning.
- `.razor` files are markup only. All C# goes in the `.razor.cs` partial class, with no `@code`.
- Every visible text is a declared `TextDefinition`. New SharedKernel texts go under `ShellTexts.Trees` (key prefix `common.trees.`).
- Every new catalog key is added to `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/Types/catalog-keys.txt`. It also gets a `fa` row in `docker-compose/.files/seed/dev-seed.sql`. Every removed key is deleted from both places.
- The scope tree must look and behave as today:
  - icons: AccountTree for the root, FolderOpen for other nodes;
  - node menu: Open, Sub-scope, Information when available, and Delete disabled on a node with children;
  - permissions: `ScopePermissions.Manage` on any scope;
  - selection and reload behaviour unchanged;
  - storage keys `ua.scopes` and `scope`.
- No change to `SerginTree`, `SerginAggregateTree`, `SerginTreeGroup`, `SerginTreeBranch` or `SerginTreeSelect` behaviour.
- Work in this worktree: `.claude/worktrees/gav-trees`, branch `generated-tree-views`.
  - The SharedKernel and UserAccess changes are committed inside their submodules on a branch named `generated-tree-views`.
  - Root commits carry the plan, the spec, the tests, the seed and `catalog-keys.txt`.
  - Commits are under the user's identity, with no Co-Authored-By trailer (root CLAUDE.md).
- Build: `dotnet build Sergin.MeterMinder.slnx`. Tests: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`. Docker Desktop must be running.

## Review Focus

1. **A reader without `ScopePermissions.Manage`** must not see Sub-scope or Delete in the tree's node menu. The derived permission must equal the delete command's. Test: `GeneratedTreeParts_DeleteAndAddChild_CarryTheCommandsPermissions` (Task 2).
2. **Stored panel widths survive.** LayoutKey must be exactly `ua.scopes` and GroupKey exactly `scope`. Test: `ScopeTree_Keys_MatchTheHandWrittenLayout` (Task 5).
3. **The root scope's "Under" field** shows "The root of the tree", not a link to `/ua/scopes/` with an empty id. Test: `RootScopeDetail_Under_ShowsTheTreeRootText` (Task 5).
4. **A delete with a confirm value** (`DeleteScopeCommand(Id, Cascade)`) sent from the tree must carry `Cascade=false`, never throw on a missing value. Test: `DeleteRequest_FillsConfirmValuesWithDefaults` (Task 2).
5. **The children section on a leaf** shows the configured empty text. On a node with grandchildren it lists them indented below the children. Tests: `TreeDescendants_*` (Task 3) and the existing `No child scopes.` assertion (Task 5).

---

### Task 1: Tree settings on the configuration and the model; `WhenEmpty` on a field

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Configuration/TreeViewBuilder.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Model/TreeView.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/AggregateViewModelJoin.cs` (`TreeOf`, around line 147; the `FieldView` build, around line 679)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Configuration/FieldSetBuilder.cs` (`FieldBuilder<T>`, `FieldOverride`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Model/FieldView.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Views/Parts/GeneratedField.razor{,.cs}`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Localizations/ShellTexts.cs` (`Trees`)
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/AggregateViewTestTypes.cs` (`ParentProbeViewConfiguration`, its texts)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/AggregateViewRegistryTests.cs`
- Modify: `tests/.../Commands/Types/catalog-keys.txt`, `docker-compose/.files/seed/dev-seed.sql`

**Interfaces:**
- Produces, on `TreeViewBuilder<TItem>`:
  - `Icon(Func<TItem, string?> icon)`
  - `Filter(TextDefinition placeholder)`
  - `Empty(TextDefinition text)`
  - `Prompt(TextDefinition title, TextDefinition? hint = null, string? icon = null)`
  - `Children(TextDefinition title, TextDefinition empty)`
  - Each method returns the builder. Each throws `ArgumentNullException` on a null required argument.
- Produces new `TreeView` members, added as optional record parameters after `TitleOf`. Each default is resolved in `TreeOf`, so the views never see null except where noted:
  - `Func<object, string?>? IconOf` (null means no icon)
  - `TextDefinition FilterPlaceholder`
  - `TextDefinition Empty`
  - `TextDefinition PromptTitle`
  - `TextDefinition? PromptHint`
  - `string PromptIcon`
  - `TextDefinition ChildrenTitle`
  - `TextDefinition ChildrenEmpty`
- Produces the new `ShellTexts.Trees` texts. Each one's default is used when the configuration names none:
  - `SelectPrompt` "Select an item"
  - `NothingToShow` "Nothing to show."
  - `Children` "Children"
  - `NoChildren` "None."
  - The filter placeholder reuses the existing `ShellTexts.Trees.FilterPlaceholder`.
  - The prompt icon defaults to `Icons.Material.Outlined.AccountTree`.
- Produces `FieldBuilder<T>.WhenEmpty(TextDefinition text)`, and a matching `FieldView.WhenEmpty` (an optional `TextDefinition?` parameter, last). `GeneratedField` handles it as follows:
  - When the value is null, or a blank string, and `WhenEmpty` is set, it renders the text unlinked. This applies even with an `Href`.
  - When the value is null and `WhenEmpty` is not set, `Href` is not invoked and the plain value is shown.

- [ ] **Step 1: Write the failing tests** in `AggregateViewRegistryTests`:
  - First, extend `ParentProbeViewConfiguration`'s `Tree<ParentProbeTreeItem>` with `.Icon(i => i.ParentId is null ? "root-icon" : "node-icon").Filter(ParentProbeTexts.Filter).Prompt(ParentProbeTexts.Prompt, ParentProbeTexts.PromptHint, "prompt-icon").Children(ParentProbeTexts.Kids, ParentProbeTexts.NoKids)`.
  - Declare those five texts in `ParentProbeTexts`.
  - Then write the tests:

```csharp
[Fact]
public void Tree_CarriesTheDeclaredIconAndTexts()
{
    TreeView tree = AggregateViewTestRegistry.Build(typeof(ParentProbeViewConfiguration)).For<ParentProbe>().Tree!;

    Assert.Equal("root-icon", tree.IconOf!(new ParentProbeTreeItem(Guid.NewGuid(), null, "a")));
    Assert.Equal("node-icon", tree.IconOf!(new ParentProbeTreeItem(Guid.NewGuid(), Guid.NewGuid(), "b")));
    Assert.Same(ParentProbeTexts.Filter, tree.FilterPlaceholder);
    Assert.Same(ParentProbeTexts.Prompt, tree.PromptTitle);
    Assert.Same(ParentProbeTexts.PromptHint, tree.PromptHint);
    Assert.Equal("prompt-icon", tree.PromptIcon);
    Assert.Same(ParentProbeTexts.Kids, tree.ChildrenTitle);
    Assert.Same(ParentProbeTexts.NoKids, tree.ChildrenEmpty);
    Assert.Same(ShellTexts.Trees.NothingToShow, tree.Empty);
}

[Fact]
public void Tree_WithOnlyATitle_TakesTheCommonDefaults()
// Build a configuration whose Tree<T> declares only Title (a test-local copy of ParentProbe's with no other tree calls).
// Assert: IconOf is null; FilterPlaceholder is ShellTexts.Trees.FilterPlaceholder; PromptTitle is ShellTexts.Trees.SelectPrompt;
// PromptHint is null; PromptIcon is Icons.Material.Outlined.AccountTree; ChildrenTitle is ShellTexts.Trees.Children;
// ChildrenEmpty is ShellTexts.Trees.NoChildren.

[Fact]
public void DetailField_WhenEmpty_IsCarriedOnTheFieldView()
// Give ParentProbe's Detail a field .Link(...).WhenEmpty(ParentProbeTexts.NoParent), then assert that FieldView.WhenEmpty is the same text.
```

- [ ] **Step 2: Run them and confirm they fail.**
  - Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All --filter "FullyQualifiedName~AggregateViewRegistryTests"`
  - Expected: compile errors, because `Icon` and `WhenEmpty` are undefined.

- [ ] **Step 3: Implement the builder methods, the model members, the `TreeOf` defaults, `WhenEmpty` and the `GeneratedField` rendering rule.**
  - Add the four `ShellTexts.Trees` texts.
  - Add the four keys to `catalog-keys.txt`: `common.trees.select-prompt`, `common.trees.nothing-to-show`, `common.trees.children` and `common.trees.no-children`. Verify the exact casing against how the scanner prints existing `common.trees.*` keys.
  - Add their `fa` rows to the `fa` block of `dev-seed.sql`.
  - Add the probe test texts only to the test types. Probe texts are outside the pinned catalog. Check how `catalog-keys.txt` treats `Tst` texts before adding them there.

- [ ] **Step 4: Run the tests and confirm they pass.** Also run `MigrationPinTests` and `AggregateViewGuardTests`.
  - Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All --filter "FullyQualifiedName~AggregateViewRegistryTests|FullyQualifiedName~MigrationPinTests|FullyQualifiedName~AggregateViewGuardTests"`
  - Expected: PASS.

- [ ] **Step 5: Commit.** Commit in `src/SharedKernel` first, with the message `Generated tree views: tree settings on the configuration, WhenEmpty on a field`. Then commit the root's test, seed and `catalog-keys.txt` changes.

---

### Task 2: `GeneratedTreeParts`: what the generated tree derives from the model

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Runtime/GeneratedTreeParts.cs`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/GeneratedTreePartsTests.cs` (new; `[Collection(nameof(IntegrationTestCollection))]` is not needed, because it is pure)

**Interfaces:**
- Consumes: `AggregateViewModel` (`Tree`, `Routes`, `Schema`, `Root`, `Title`, `Commands`, `ChildCreate`, `ChildCreateHref`, `Audit`); `CommandView` (`Role`, `Hidden`, `BuildFromId`, `BuildFromValues`, `ConfirmFields`, `Permissions`, `AnyScope`, `ConfirmTitle`, `Label`, `Icon`).
- Produces:

```csharp
internal sealed record GeneratedTreeParts(
    TreeView Tree,
    string Href,                       // Routes.List
    Func<Guid, string> NodeHref,       // Routes.Detail
    string LayoutKey,                  // $"{Schema}.{last segment of Routes.List}"  -> "ua.scopes"
    string GroupKey,                   // Root.Name.ToLowerInvariant()                -> "scope"
    IReadOnlyList<SerginBreadcrumb> Trail, // [new SerginBreadcrumb(model.Title, Routes.List)]
    CommandView? Delete,               // the root's shown Delete command, else null
    CommandView? AddChild)             // model.ChildCreate
{
    public static GeneratedTreeParts For(AggregateViewModel model);
    // Throws InvalidOperationException naming model.Root.FullName when Tree is null ("declares no Tree<T>() / ITreeQuery"),
    // when Routes.List is null, or when Routes.Detail is null.

    public static object DeleteRequest(CommandView delete, Guid id);
    // delete.BuildFromId?.Invoke(id) when set; otherwise delete.BuildFromValues!(id, values), where values maps each
    // delete.ConfirmFields name to its ValueType's default (Activator.CreateInstance for a value type, null otherwise).
}
```

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact]
public void For_ParentProbe_DerivesRoutesKeysAndTrail()
{
    AggregateViewModel model = AggregateViewTestRegistry.Build(typeof(ParentProbeViewConfiguration)).For<ParentProbe>();
    GeneratedTreeParts parts = GeneratedTreeParts.For(model);
    Guid id = Guid.NewGuid();

    Assert.Equal(ParentProbeRoutes.List, parts.Href);
    Assert.Equal(ParentProbeRoutes.Detail(id), parts.NodeHref(id));
    Assert.Equal("tst." + ParentProbeRoutes.List.Split('/').Last(), parts.LayoutKey);
    Assert.Equal("parentprobe", parts.GroupKey);
    Assert.Equal(ParentProbeRoutes.List, Assert.Single(parts.Trail).Href);
    Assert.Equal(typeof(DeleteParentProbeCommand), parts.Delete!.Request);
    Assert.Equal(typeof(CreateParentProbeCommand), parts.AddChild!.Request);
}

[Fact]
public void GeneratedTreeParts_DeleteAndAddChild_CarryTheCommandsPermissions()
// Assert that parts.Delete.Permissions and parts.AddChild.Permissions equal the probe's configured permissions, and that AnyScope matches (ParentProbe is not Scoped(): true).

[Fact]
public void For_ARootWithoutATree_Throws_NamingTheRoot()
// For the ViewProbe model (no tree): Assert.Throws<InvalidOperationException>, with the message containing typeof(ViewProbe).FullName.

[Fact]
public void For_ATreeRootWithoutAListRoute_Throws_NamingTheRoot()
// Use a test-local configuration of ParentProbe with Routes(detail only) and Tree<T>: it throws, naming the root.

[Fact]
public void DeleteRequest_FillsConfirmValuesWithDefaults()
{
    // ConfirmProbe's delete is DeleteConfirmProbeCommand(Id, bool Cascade).
    CommandView delete = /* ConfirmProbe model's command with Role == CommandRole.Delete */;
    Guid id = Guid.NewGuid();

    DeleteConfirmProbeCommand command = Assert.IsType<DeleteConfirmProbeCommand>(GeneratedTreeParts.DeleteRequest(delete, id));

    Assert.Equal(id, command.Id.Value);
    Assert.False(command.Cascade);
}

[Fact]
public void DeleteRequest_IdOnly_UsesBuildFromId()
// With ParentProbe's DeleteParentProbeCommand: assert the request is DeleteParentProbeCommand with that id.
```

- [ ] **Step 2: Run them and confirm they fail.** Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All --filter "FullyQualifiedName~GeneratedTreePartsTests"`. Expected: compile error, because `GeneratedTreeParts` is undefined.
- [ ] **Step 3: Implement `GeneratedTreeParts`.** The test project already sees SharedKernel internals. Check the `InternalsVisibleTo` that `MoveCandidates` tests rely on.
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit** in `src/SharedKernel` (message `Generated tree views: GeneratedTreeParts`), then in the root for the tests.

---

### Task 3: `TreeDescendants` and the generated children section

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Runtime/TreeDescendants.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Views/Parts/GeneratedTreeChildren.razor{,.cs}`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Views/SerginGeneratedDetailView.razor{,.cs}`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/GeneratedTreePartsTests.cs`

**Interfaces:**
- Produces:

```csharp
internal static class TreeDescendants
{
    // Every descendant of nodeId, depth first, siblings ordered by TitleOf with StringComparer.CurrentCulture;
    // Depth is 0 for a direct child, 1 for a grandchild, and so on. Empty when the node has none or is not in items.
    public static IReadOnlyList<(object Item, Guid Id, string Title, int Depth)> Of(TreeView tree, IReadOnlyCollection<object> items, Guid nodeId);
}
```

- `GeneratedTreeChildren` takes these parameters: `[Parameter, EditorRequired] TreeView Tree`, `[Parameter, EditorRequired] Guid NodeId`, `[Parameter, EditorRequired] Func<Guid, string> NodeHref` and `[Parameter] int Generation`.
  - **Reading.** It reads the tree through `Tree.Query` with `ISerginDispatcher` when `NodeId` or `Generation` changes. It skips the read when neither changed, the same rule as `ScopeRelationsSlot`.
  - **Layout.** It renders an `<MudText Typo="Typo.h6" Class="mt-6 mb-2">` with `Tree.ChildrenTitle`. Under it comes either a `MudSimpleTable` (`Hover`, `Dense`) with one column, or `Tree.ChildrenEmpty` as body2 text.
  - **Rows.** Each row has a `MudLink` to `NodeHref(id)` with the title. The cell is indented with `padding-left: {Depth * 24 + 16}px`, built with `string.Create(CultureInfo.InvariantCulture, ...)`.
  - **Failed read.** A failed read renders `SerginProblemPanel` with `ErrorPresenter.Present(firstError)`, and no snackbar.
- `SerginGeneratedDetailView` renders `<GeneratedTreeChildren>` when `Model.Tree is not null && Model.Routes.Detail is not null && record is not null`. It goes in `Entities`, after the inline edit cards and before the `EntitiesSlot`, passing `Context.Generation`.

- [ ] **Step 1: Write the failing tests.** Build a `TreeView` from ParentProbe's model and use `ParentProbeTreeItem` items:
  - Root R.
  - B and A under R.
  - A1 under A.

```csharp
[Fact]
public void TreeDescendants_OfTheRoot_AreDepthFirstWithSiblingsByTitle()
// Of(tree, items, R) titles == ["A", "A1", "B"], depths == [0, 1, 0]

[Fact]
public void TreeDescendants_OfALeaf_AreEmpty()
// Of(tree, items, A1) is empty

[Fact]
public void TreeDescendants_OfAnUnknownNode_AreEmpty()
```

- [ ] **Step 2: Run them and confirm they fail.** Expected: compile error.
- [ ] **Step 3: Implement `TreeDescendants`, `GeneratedTreeChildren` and the detail view wiring.** For now the Scope detail shows two child lists: the generated one and the hand-written one in `ScopeRelationsSlot`. Task 5 removes the second.
- [ ] **Step 4: Run `GeneratedTreePartsTests`, `GeneratedScopeViewTests` and `TreePageRenderingTests`.** Expected: PASS.
- [ ] **Step 5: Commit** (SharedKernel `Generated tree views: generated children section`, then the root).

---

### Task 4: `SerginGeneratedTree`, `SerginGeneratedTreePrompt`, and the views selecting their node

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Views/SerginGeneratedTree.razor{,.cs}`
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Views/SerginGeneratedTreePrompt.razor{,.cs}`
- Modify: `SerginGeneratedDetailView.razor` (renders `<SerginTreeSelect Node="@Id.ToString()" />`)
- Modify: `SerginGeneratedFormView.razor{,.cs}`: on create with a `ParentId`, it renders `<SerginTreeSelect Node="@ParentId?.ToString()" TailText="TitleText" />`. Use the same `TitleText` the form header shows. With no `ParentId` on create, it renders `<SerginTreeSelect Node="@null" TailText="TitleText" />`, so the tree keeps today's "no parent" behaviour (`Create_WithoutAParent_SelectsTheRoot`). Check that test's expectation and match it. On edit, it renders `Node="@Id.ToString()"`.

**Interfaces:**
- `SerginGeneratedTree<TRoot>` (`@typeparam TRoot`) has `[Parameter] RenderFragment? ChildContent` and `[Parameter] RenderFragment? AdditionalGroups`.
  - It injects `AggregateViewRegistry`, `ISerginDispatcher`, `IStringLocalizer` and `IUiErrorPresenter`.
  - In `OnInitialized` it builds `GeneratedTreeParts.For(Registry.For<TRoot>())`, which throws on render as the spec says.
  - It then renders `SerginAggregateTree<object>` with these parameters:
    - `TitleText` = `model.Title`, `Trail` = `parts.Trail`, `Href` = `parts.Href`
    - `LayoutKey` = `parts.LayoutKey`, `GroupKey` = `parts.GroupKey`
    - `Load`: sends `tree.Query.Build()` through `tree.Query.Invoker.SendAsync`, and maps the `ViewSendResult` to `ErrorOr<IReadOnlyCollection<object>>` (errors, or the response cast to `IEnumerable` and materialised)
    - `NodeId` = `x => tree.IdOf(x).ToString()`
    - `ParentId` = `x => tree.ParentIdOf(x)?.ToString()`
    - `NodeLabel` = `tree.TitleOf`, `NodeIcon` = `tree.IconOf`
    - `NodeHref` = `x => parts.NodeHref(tree.IdOf(x))`
    - `AddChild`: when `parts.AddChild` is set, `x => new SerginPageAction(parts.AddChild.Label, parts.AddChild.Icon ?? Icons.Material.Outlined.Add, SerginActionKind.Secondary, Href: model.ChildCreateHref(tree.IdOf(x)), Permission: <its single permission or null>, AnyScope: parts.AddChild.AnyScope)`
    - `Delete`: when `parts.Delete` is set, it sends `GeneratedTreeParts.DeleteRequest(...)`. When `RequiresVersion` is set, it sends at the version read through `model.LoadRecordAsync` first, the way `GeneratedListBody` deletes a row. The result maps to `ErrorOr<Deleted>`.
    - `DeletePermission`, `DeleteAnyScope` and `DeleteTitleText` (`ConfirmTitle`) come from `parts.Delete`.
    - `LoadAudit`: when `model.Audit` is set, as `SerginGeneratedDetailView.LoadAuditAsync` does.
    - `FilterPlaceholderText` = `tree.FilterPlaceholder`, `EmptyMessage` = `tree.Empty`
    - `ChildContent` and `AdditionalGroups` are passed through.
  - **Child-create label.** The hand-written layout's node-menu text was `ScopeUiTexts.SubScope`, and the command's label is `ScopeUiTexts.NewChild` "New child scope". Use the command's label: the spec derives AddChild from `ChildCreate`. Record this in the commit message as the one visible text change.
- `SerginGeneratedTreePrompt<TRoot>` renders today's `ScopeTreePage` markup from `tree.PromptIcon`, `PromptTitle` and `PromptHint`, with no hint line when the hint is null:

```razor
<div class="pa-8 text-center">
    <MudIcon Icon="@tree.PromptIcon" Size="Size.Large" Class="mb-2" />
    <MudText Typo="Typo.h6">@Localizer.Text(tree.PromptTitle)</MudText>
    <MudText Typo="Typo.body2" Class="mud-text-secondary">@Localizer.Text(tree.PromptHint)</MudText>
</div>
```

  It throws on render (through `GeneratedTreeParts.For`) for a root with no tree or no list route.

- [ ] **Step 1: Make sure the existing selection tests still pin the behaviour.** `TreePageRenderingTests` (all five) and `GeneratedScopeViewTests` must pass unchanged once the pages stop rendering `SerginTreeSelect` in Task 5. For now, add the `SerginTreeSelect` to the views. The pages still carry their own copy, and two markers on one node are harmless because the last owner wins. Run both classes. Expected: PASS.
- [ ] **Step 2: Implement both components.** They are first used in Task 5, whose tests prove them.
- [ ] **Step 3: Build.** Run `dotnet build Sergin.MeterMinder.slnx`. Expected: 0 warnings, 0 errors.
- [ ] **Step 4: Commit** (SharedKernel `Generated tree views: generated tree, prompt, views select their node`).

---

### Task 5: Scope on the generated tree

**Files:**
- Modify: `src/Modules/Administration/UserAccess/Sergin.Administration.UserAccess.Presentation.Blazor/Scopes/ScopeViewConfiguration.cs`
- Modify: `Scopes/Components/ScopeTreeLayout.razor` (three lines). Delete `ScopeTreeLayout.razor.cs`.
- Modify: `Scopes/Pages/ScopeTreePage.razor`, `ScopeDetailPage.razor`, `CreateScopePage.razor` (drop `SerginTreeSelect`; the prompt page uses `<SerginGeneratedTreePrompt TRoot="Scope" />`). Delete any code-behind left with nothing in it.
- Delete: `Scopes/Components/ScopeTreeTable.razor{,.cs}` and `Scopes/Components/ScopeParentValue.razor{,.cs}`
- Rename `Scopes/Components/ScopeRelationsSlot.razor{,.cs}` to `ScopeRolesSlot.razor{,.cs}`. Keep only the roles table and its load.
- Modify: `Scopes/ScopeUiTexts.cs`. Remove the texts nothing reads any more (expected: `NoScopes`, `Level`, `SubScope`; confirm each with a grep). Keep `ChildScopes`, `NoChildScopes`, `TreeRoot`, `FilterPlaceholder`, `TreeEmpty`, `SelectPrompt` and `SelectHint`, which the configuration now reads.
- Modify: `tests/.../Commands/Types/catalog-keys.txt` and `docker-compose/.files/seed/dev-seed.sql` (remove the deleted keys' `fa` rows)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/GeneratedScopeViewTests.cs` and `tests/.../Shell/TreePageRenderingTests.cs`

**Interfaces:**
- Consumes everything from Tasks 1 to 4. The configuration becomes:

```csharp
view.Tree<ScopeTreeItem>(tree => tree
    .Title(x => x.Name)
    .Icon(x => x.ParentId is null ? Icons.Material.Outlined.AccountTree : Icons.Material.Outlined.FolderOpen)
    .Filter(ScopeUiTexts.FilterPlaceholder)
    .Empty(ScopeUiTexts.TreeEmpty)
    .Prompt(ScopeUiTexts.SelectPrompt, ScopeUiTexts.SelectHint, Icons.Material.Outlined.AccountTree)
    .Children(ScopeUiTexts.ChildScopes, ScopeUiTexts.NoChildScopes));
// in Detail<ScopeQueryResponse>:
detail.Title(x => x.Name).Field(x => x.ParentName).Label(ScopeUiTexts.Under)
      .Link(scope => $"/ua/scopes/{scope.ParentId}").WhenEmpty(ScopeUiTexts.TreeRoot);
view.Slot<ScopeRolesSlot>(SerginViewSlot.DetailEntities);
```

```razor
@* ScopeTreeLayout.razor *@
@inherits LayoutComponentBase
@layout Sergin.SharedKernel.Presentation.Blazor.Layout.SerginMainLayout
<SerginGeneratedTree TRoot="Scope">@Body</SerginGeneratedTree>
```

- [ ] **Step 1: Write the new tests.**

```csharp
// GeneratedScopeViewTests
[Fact]
public async Task RootScopeDetail_Under_ShowsTheTreeRootText()
// GET /ua/scopes/{Scope.RootId.Value}: html contains "The root of the tree" and does not contain href="/ua/scopes/" followed by a quote (no empty-id link).

[Fact]
public async Task ScopeDetail_Under_LinksToTheParent()
// RbacScenario: GET /ua/scopes/{Feeder1} contains href="/ua/scopes/{North}" inside the Under field (SerginDetailField label "Under").

[Fact]
public async Task ScopeDetail_ChildScopes_ListsDescendantsIndented()
// RbacScenario: GET /ua/scopes/{Root}: "Child scopes" heading; North before Feeder 1 before South (Feeder 1 under North); Feeder 1's cell padding-left is 24px more than North's.

[Fact]
public void ScopeTree_Keys_MatchTheHandWrittenLayout()
// Resolve AggregateViewRegistry from factory.Services: GeneratedTreeParts.For(registry.For<Scope>()) has LayoutKey "ua.scopes" and GroupKey "scope"; Delete.Request is DeleteScopeCommand; DeleteRequest(delete, id) is DeleteScopeCommand with Cascade false.
```

- [ ] **Step 2: Run them and confirm that the Under and keys tests fail against the current code.** The Under link test fails because `ScopeParentValue` renders differently. Check what fails and say so in the report. The children test may already pass through the hand-written table.
- [ ] **Step 3: Switch Scope over** as listed under Files, and update `catalog-keys.txt` and the seed.
- [ ] **Step 4: Run** `GeneratedScopeViewTests`, `TreePageRenderingTests`, `ScopedRbacPageRenderingTests`, `PseudoLocaleSweepTests`, `MigrationPinTests` and `FieldTextGuardTests`. Expected: PASS. If a test asserted the old "Sub-scope" text or the "Level" column, update the assertion to the new behaviour and name it in the report.
- [ ] **Step 5: Commit** in `src/Modules/Administration/UserAccess` (`Scopes on the generated tree`), then in the root for the tests, seed and `catalog-keys.txt`.

---

### Task 6: Docs, full suite and browser check

**Files:**
- Modify: `src/SharedKernel/.claude/CLAUDE.md`. Update the "Generated aggregate views" bullets on trees and parents: the new `Tree<T>` settings, `SerginGeneratedTree`/`SerginGeneratedTreePrompt`, the children section, `WhenEmpty`, and the views selecting their node. Update the `Presentation.Blazor/Trees/` bullet to say pages no longer render `SerginTreeSelect` for a generated view.
- Modify: `src/Modules/Administration/UserAccess/.claude/CLAUDE.md` (the scope pages section)
- Modify: the root `.claude/CLAUDE.md`. Update the "A tree is a layout" bullet: a module's layout is now three lines over `SerginGeneratedTree<TRoot>`, and the pages drop `SerginTreeSelect`.
- Modify: `.claude/skills/add-feature/SKILL.md` and `.claude/skills/add-module/SKILL.md`, only where they describe a hand-written tree layout. Run a grep first.

- [ ] **Step 1: Update the docs above.**
- [ ] **Step 2: Run the full suite.** Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj` and `dotnet test tests/Sergin.Administration.UserAccess.UnitTests/Sergin.Administration.UserAccess.UnitTests.csproj`. Expected: all pass. The licence-gated TickerQ tests are skipped.
- [ ] **Step 3: Run a headless browser check** (memory: Playwright-Python against the host on :5555). On `/ua/scopes`, confirm each of these:
  - the root icon and the folder icons;
  - expanding and selecting a node opens its detail, with the "Child scopes" section and "Under";
  - the node ⋮ shows Open, New child scope and Delete (disabled on North, which has children);
  - deleting a leaf from the tree removes it;
  - the filter placeholder reads "Filter scopes…".
- [ ] **Step 4: Commit the docs** in each repo.
