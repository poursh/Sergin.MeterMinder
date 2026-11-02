# Tree Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the tree page and the page host into Blazor layouts, so `/ua/scopes`, `/ua/scopes/{id}` and `/ua/scopes/new?parentId=…` all render inside one tree that keeps its state, and DeviceManagement pages use `SerginPageLayout`.

**Architecture:** `SerginPageLayout` and a per-module `ScopeTreeLayout` nest in `SerginMainLayout`. The tree component (`SerginTree`, renamed from `SerginTreePage`) renders its layout's `@Body` as its content side and cascades a `Pane` host and a `SerginTreeContext`. Pages report their node with a `SerginTreeSelect` marker; clicks navigate to `NodeHref(node)`.

**Tech Stack:** .NET 10, Blazor Server (interactive server), MudBlazor 9.12, xUnit + Testcontainers, Playwright-Python 1.46.

**Spec:** `docs/superpowers/specs/2026-11-02-tree-page-design.md` (revision 2)

## Global Constraints

- Warnings are errors (`TreatWarningsAsErrors`, `AnalysisMode=All`, Sonar). No `@code` blocks; C# in `.razor.cs`, `public sealed partial class`.
- Every module route starts with `/{schema}/`.
- `@layout` goes in each page file, not `_Imports.razor`.
- Work in worktree `C:/@factory/Sergin/Sergin.MeterMinder-tree-page`. Branches: host `feature/tree-page`, SharedKernel `feature/tree-page`, UserAccess `feature/tree-page-scopes`. Commit in each submodule on its branch; do not push or open PRs.
- Commits under the user's git identity; message ends with the attribution line from the session reminder.
- Build: `dotnet build Sergin.MeterMinder.slnx`. Tests: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj` (Docker Desktop running).

## Review Focus

- **Unknown node id** (`/ua/scopes/{random guid}`): the view's not-found panel shows, the tree selects nothing and throws nothing. Test in Task 4.
- **Marker hand-over between two pane pages** (detail of A → detail of B, detail → create): the new page's selection survives the old marker's dispose. Browser check in Task 6 (A → B keeps B selected).
- **Scope change in the app bar** while on `/ua/scopes/{id}`: the tree rebuilds (body keyed) and the page reloads; no stale selection. Browser check in Task 6.
- **Leaving the tree** (drawer link to `/dm/devices`): the layout switches to `SerginPageLayout`, the device page shows its own header buttons, no leftover tree header section. Test in Task 5 (`ViewHostRenderingTests`) and browser check in Task 6.
- **`parentId` missing or unknown on create**: the form renders in the pane with nothing selected; save is refused by the validator as today. Test in Task 4.

---

### Task 1: `SerginPageLayout` replaces `SerginPage` (SharedKernel + DeviceManagement + UserAccess detail page)

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Views/SerginPageLayout.razor`, `SerginPageLayout.razor.cs`
- Delete: `…/Views/SerginPage.razor`, `SerginPage.razor.cs`
- Modify: the 11 DeviceManagement pages listed by `grep -rl "<SerginPage" src/Modules/DeviceManagement --include=*.razor`; `src/Modules/UserAccess/Sergin.UserAccess.Presentation.Blazor/Scopes/Pages/ScopeDetailPage.razor` (temporarily `SerginPageLayout`; Task 4 moves it to the tree)
- Modify: doc comments naming `SerginPage` in `SerginViewHost.cs` and the views
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Shell/ViewHostRenderingTests.cs` (existing, must pass unchanged)

**Interfaces:**
- Produces: `public sealed partial class SerginPageLayout : LayoutComponentBase`, markup `@layout SerginMainLayout` + `<CascadingValue Value="host" IsFixed="true">@Body</CascadingValue>`, host = `new SerginViewHost(SerginViewHostKind.Page, Leave)` with `Leave` navigating, as `SerginPage` does now.

- [ ] **Step 1:** Run `ViewHostRenderingTests`, `PageTypeRenderingTests`, `BreadcrumbRenderingTests`; record PASS as the baseline.
- [ ] **Step 2:** Add `SerginPageLayout`; delete `SerginPage`.
- [ ] **Step 3:** In each page, replace `<SerginPage><XView … /></SerginPage>` with `@layout SerginPageLayout` (after `@page`) and `<XView … />`.
- [ ] **Step 4:** Build. Expected: 0 errors.
- [ ] **Step 5:** Run the three test classes. Expected: PASS, same counts as Step 1.
- [ ] **Step 6:** Commit in SharedKernel ("Replace SerginPage with SerginPageLayout"), UserAccess, and the host (DeviceManagement pages).

### Task 2: `SerginTree` — the tree as a layout's content (SharedKernel)

**Files:**
- Rename: `Trees/SerginTreePage.razor{,.cs}` → `Trees/SerginTree.razor{,.cs}`; `Trees/SerginAggregateTreePage.razor{,.cs}` → `Trees/SerginAggregateTree.razor{,.cs}`
- Create: `Trees/SerginTreeContext.cs`, `Trees/SerginTreeSelect.razor`, `SerginTreeSelect.razor.cs`
- Delete: `Trees/SerginTreeSelection.cs`
- Modify: `Trees/SerginTreeGroup.razor{,.cs}`, `Trees/ISerginTreeGroup.cs`, `Trees/SerginTreePageHeader.razor.cs` (close goes to `Href`)
- Delete test: `tests/…/Shell/TreeSelectionTests.cs`

**Interfaces:**
- Produces:
  - `SerginTree` parameters: as `SerginTreePage` today minus `UnselectedContent`, `SelectedChanged`, `ActiveGroupChanged`; plus `[Parameter, EditorRequired] public string Href { get; set; }` (the tree's own route, target of ×/Esc) and `ChildContent` split into `[Parameter] public RenderFragment? Groups { get; set; }` and `[Parameter] public RenderFragment? Content { get; set; }` (the layout's `@Body`).
  - `public sealed class SerginTreeContext` cascaded by `SerginTree` (`IsFixed="true"`): `public void Select(object owner, string? group, string nodeId, string? tail)`, `public void Release(object owner)`.
  - `SerginTreeSelect` (`IDisposable`): `[Parameter, EditorRequired] public string? Node`, `[Parameter] public string? Group`, `[Parameter] public string? Tail`; `OnParametersSet` calls `Select(this, …)` when `Node` is non-blank, else `Release(this)`; `Dispose` calls `Release(this)`. Renders nothing.
  - `SerginTreeGroup<TNode>`: remove `SelectedContent`; add `[Parameter, EditorRequired] public Func<TNode, string> NodeHref`. A label click / Enter calls `Navigation.NavigateTo(NodeHref(node))`.
  - Pane host from `SerginTree`: `leave(href)` = reload active group then `NavigateTo(href)`; `changed()` = reload active group.
  - `SerginTreeSelectedNode` gains `string? Tail`; `SerginTreePageHeader` renders it as the last, non-link path step.

- [ ] **Step 1:** Rename the files and types; remove `?node=` parsing and `LocationChanged` selection logic, `SerginTreeSelection`, `SelectedContent`, `ContentSectionId`, and the content `SectionOutlet`. Render `Content` where the outlet was, inside the `Pane` host cascade.
- [ ] **Step 2:** Add `SerginTreeContext` and `SerginTreeSelect`. Rules: `Select` records the owner and asks the group to `ShowAsync(nodeId)` (ancestors open through `PathTo`); `Release` clears only when `owner` is the current owner; `LocationChanged` clears the owner and the highlight before the new page renders. With one group, `Group` may be null.
- [ ] **Step 3:** `SerginTreeGroup`: add `NodeHref`; selection now arrives only from the context; × and Esc navigate to `SerginTree.Href`.
- [ ] **Step 4:** `SerginAggregateTree<TNode>`: drop `SelectedContent`/`UnselectedContent`/`SelectedChanged`; add `[Parameter, EditorRequired] public Func<TNode, string> NodeHref`, `[Parameter, EditorRequired] public string Href`, `[Parameter] public RenderFragment? ChildContent` (passed as `Content`). After a menu Delete of the node the URL shows, navigate to `Href`.
- [ ] **Step 5:** Delete `TreeSelectionTests.cs`. Build SharedKernel through the host solution. Expected: errors only in UserAccess `ScopeTreePage` (fixed in Task 3).
- [ ] **Step 6:** Commit in SharedKernel ("Make the tree a layout's content: route selection, SerginTreeSelect").

### Task 3: `ScopeTreeLayout` and the scope pages (UserAccess)

**Files:**
- Create: `src/Modules/UserAccess/Sergin.UserAccess.Presentation.Blazor/Scopes/Components/ScopeTreeLayout.razor`, `ScopeTreeLayout.razor.cs`; `Scopes/Components/ScopeCreateView.razor`, `ScopeCreateView.razor.cs`
- Modify: `Scopes/Pages/ScopeTreePage.razor{,.cs}`, `Scopes/Pages/ScopeDetailPage.razor`, `Scopes/Pages/CreateScopePage.razor{,.cs}`; `src/Modules/UserAccess/.claude/CLAUDE.md`

**Interfaces:**
- Consumes: `SerginAggregateTree<TNode>` (`Href`, `NodeHref`, `ChildContent`), `SerginTreeSelect`, `SerginFormView`.
- Produces:
  - `public sealed partial class ScopeTreeLayout : LayoutComponentBase`, `@layout SerginMainLayout`, markup `<SerginAggregateTree TNode="ScopeTreeItem" Href="/ua/scopes" NodeHref="HrefOf" …the parameters ScopeTreePage passes today…>@Body</SerginAggregateTree>`. Code-behind holds what `ScopeTreePage.razor.cs` holds today; `HrefOf(node) => $"/ua/scopes/{node.Id}"`.
  - `ScopeTreePage` (`/ua/scopes`): `@layout ScopeTreeLayout`, body = the "Select a scope" prompt; code-behind empty of tree logic.
  - `ScopeDetailPage`: `@layout ScopeTreeLayout`, `<SerginTreeSelect Node="@Id.ToString()" />` + `<ScopeDetailView Id="Id" />`.
  - `ScopeCreateView` (`[Parameter] Guid? ParentId`): the current create form on `SerginFormView` (`DeviceCreateView` is the reference); save → `Host.LeaveAsync($"/ua/scopes/{newId}")`; cancel href → `/ua/scopes/{ParentId}` or `/ua/scopes`.
  - `CreateScopePage` (`/ua/scopes/new`, `[SupplyParameterFromQuery] Guid? ParentId`): `@layout ScopeTreeLayout`, `<SerginTreeSelect Node="@ParentId?.ToString()" Tail="New scope" />` + `<ScopeCreateView ParentId="ParentId" />`.

- [ ] **Step 1:** Add `ScopeTreeLayout`, moving the tree markup and code out of `ScopeTreePage`.
- [ ] **Step 2:** Rewrite the three pages as above; add `ScopeCreateView`.
- [ ] **Step 3:** Build `Sergin.MeterMinder.slnx`. Expected: 0 errors.
- [ ] **Step 4:** Update UserAccess CLAUDE.md: scope pages render inside `ScopeTreeLayout`; create is a view.
- [ ] **Step 5:** Commit in UserAccess ("Render the scope pages inside ScopeTreeLayout; create in the pane").

### Task 4: Rendering tests for the tree layout (host)

**Files:**
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Shell/TreePageRenderingTests.cs`

- [ ] **Step 1: Rewrite the tests** (keep `Unselected_ShowsTheTreeAndThePrompt` as is):

```csharp
[Fact] public async Task Detail_ExpandsTheAncestors_AndMountsTheViewInThePane()
// GET /ua/scopes/{scenario.Feeder1}
// Contains data-node="{North}"; Matches aria-selected="true"[^>]*data-node="{Feeder1}";
// Single(BreadcrumbNav), it contains href="/ua/scopes"; PageTitle == "Scopes";
// Contains "No child scopes."; DoesNotContain "No node selected".

[Fact] public async Task Create_SelectsTheParent_AndMountsTheFormInThePane()
// GET /ua/scopes/new?parentId={scenario.North}
// Matches aria-selected="true"[^>]*data-node="{North}"; Contains "New scope" inside the tree header
// (class sergin-tree-page-header); Contains role="tree"; Contains a Create submit button.

[Fact] public async Task Create_WithoutAParent_SelectsNothing()
// GET /ua/scopes/new — Contains role="tree"; DoesNotMatch aria-selected="true".

[Fact] public async Task UnknownNode_ShowsNotFound_AndSelectsNothing()
// GET /ua/scopes/{Guid.CreateVersion7()} — 200; DoesNotMatch aria-selected="true"; Contains the not-found panel text.
```

Delete `ScopeDetailPage_StillHostsTheViewAsAPage`.

- [ ] **Step 2:** Run `TreePageRenderingTests`, `TreeIndexTests`, `ScopedRbacPageRenderingTests`. Expected: PASS. Fix code in Tasks 2–3 if not.
- [ ] **Step 3:** Commit in host ("Test the tree layout's prerender").

### Task 5: Host bump, full suite, docs

**Files:**
- Modify: submodule pointers `src/SharedKernel`, `src/Modules/UserAccess`; `.claude/CLAUDE.md` (Blazor UI conventions: page bullet now says `@layout SerginPageLayout`; add a tree layout bullet); `src/SharedKernel/.claude/CLAUDE.md` (`Trees/`, `SerginPageLayout`); `tests/…/Shell/ViewHostRenderingTests.cs` doc comment (`SerginPage` → `SerginPageLayout`)
- Modify: `.claude/skills/add-feature/SKILL.md` wherever it shows `<SerginPage>`

- [ ] **Step 1:** `grep -rn "SerginPage\b\|<SerginPage" --include=*.razor --include=*.cs --include=*.md .` Expected: no hits outside dated specs/plans.
- [ ] **Step 2:** Full integration suite and `Sergin.UserAccess.UnitTests`. Expected: all PASS; report the counts.
- [ ] **Step 3:** `graphify update .` then `python .claude/skills/graphify/scripts/graphify_repair.py`.
- [ ] **Step 4:** Commit in SharedKernel (docs), then host ("Bump SharedKernel and UserAccess: tree layout; DeviceManagement pages on SerginPageLayout").

### Task 6: Browser check (no commit)

**Files:**
- Create: `<scratchpad>/tree_layout_check.py` (from `<scratchpad>/tree_check.py`; run with `<scratchpad>/pw/Scripts/python`)

Run the worktree host on :5555 as in the earlier sessions (Compose connection string with `Host=localhost`, `Sergin__DevUser__UserName=dev.local`, `--no-launch-profile`).

- [ ] **Step 1:** Port the revision 1 checks to routes: label click goes to `/ua/scopes/{id}`; Esc and × go to `/ua/scopes`; back restores.
- [ ] **Step 2:** Add these checks:
  - expand root, open "Sub-scope" from the dynamic row: URL `/ua/scopes/new?parentId=…`, root still expanded, parent `aria-selected`, form in the pane;
  - save "Pane Child": URL `/ua/scopes/{newId}`, new row selected, parent expanded;
  - open create again and cancel: URL back on the parent;
  - detail A then detail B: B stays selected;
  - delete from the view: `/ua/scopes`, the row is gone;
  - on `/ua/scopes/{id}`, change the scope in the app bar: the tree rebuilds, the page reloads, no error;
  - the drawer link to Devices, then open a device: the device header buttons show and there is no tree;
  - no console errors.
- [ ] **Step 3:** Run it. Expected: every check PASS. Kill the host afterwards.
