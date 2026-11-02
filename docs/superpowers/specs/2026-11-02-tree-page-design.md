# Tree layout — design

Date: 2026-11-02. Source: `Meter Minder Wireframes.dc.html`, round 7 ("SerginTreePage · a SerginPage with a tree panel"), claude.ai/design project `d78c52ce-cc94-4051-9389-4706718b93fd`.

Revision 2 (same day). The first cut was a page host, `SerginTreePage`, which kept its selection in `?node=` and rendered the selected node's view itself. Creating a child scope still navigated away to `/ua/scopes/new`, a separate page with no tree. Revision 2 makes the tree a **layout**, so every scope route — detail and insert included — renders inside it. `SerginPage` becomes a layout too, so both hosts work the same way.

## Problem

The kit has list, detail and form views (the 2026-10-28 page/view split), but nothing for hierarchical data. `/ua/scopes` drew the scope tree as an indented `MudSimpleTable`, and every scope, and the create form, opened on a separate page. Moving around the tree, or adding to it, meant leaving it.

## Goals

- A tree on the left and, on the right, whatever page is at the current route: a node's detail, a create form, or the unselected prompt.
- **Insert opens inside the tree.** `/ua/scopes/new?parentId=…` renders in the pane, with the parent highlighted and its branch open.
- The tree keeps its state (expanded branches, scroll, filter text, panel width) while the user moves between those pages.
- The route is the selection. Links, reload and back/forward restore it with no extra state.
- A view does not know whether a page or a tree is showing it. Its host comes from the layout it sits in.
- DeviceManagement pages move to the page layout in the same round, so the kit has one way to host a view.

## Non-goals

- **No edit route for scopes.** Rename and move stay as inline forms inside `ScopeDetailView` (user's decision). A write there calls `Host.ChangedAsync()`, and the tree reloads.
- No "discard unsaved changes?" prompt when leaving a dirty form. Every create page navigates without one today.
- No dialog host. `SerginDetailOpenMode.Modal` is still not built.
- Not built from round 7: Shift+F10 to open a row menu, multi-select, drag to move.

## Anti-goals

- A route that renders both as a full page and as a pane. Each route has exactly one layout. `/ua/scopes/{id}` now always opens inside the tree (user confirmed).
- Losing the tree's expanded state on navigation. If the tree re-mounts on each page, the design has failed, even if every page renders.

## Shape

Two layouts, nested in the shell's `SerginMainLayout`:

```
SerginMainLayout                      app bar, drawer, breadcrumb outlet, page body
├── SerginPageLayout                  cascades a Page host to @Body
│     └── DeviceDetailPage → DeviceDetailView
└── ScopeTreeLayout (module)          renders <SerginAggregateTree …>@Body</SerginAggregateTree>
      └── ScopeDetailPage → ScopeDetailView        (Pane host)
      └── CreateScopePage → ScopeCreateView        (Pane host)
      └── ScopeTreePage   → the unselected prompt
```

| Part | Where | Role |
|---|---|---|
| `SerginPageLayout` | SharedKernel `Views/` | `LayoutComponentBase`, `@layout SerginMainLayout`. Cascades the `Page` host (navigate on leave) around `@Body`. Replaces the `<SerginPage>` wrapper, which is deleted. |
| `SerginTree` | SharedKernel `Trees/` | The tree UI, formerly `SerginTreePage`: own `<PageTitle>` and `SerginPageHeader`, panel (static row, dynamic row, filter, groups), resize, hide, layout store. Content side = `SerginTreePageHeader` + its `ChildContent` (the layout's `@Body`). Cascades the `Pane` host and a `SerginTreeContext` to the content. |
| `SerginTreeGroup<TNode>` | `Trees/` | One data source as before: roots, children, path, labels, icons, menus, dynamic actions, add action. Loses `SelectedContent`. Gains `NodeHref` (`Func<TNode, string>`): a label click navigates there. |
| `SerginAggregateTree<TNode>` | `Trees/` | Formerly `SerginAggregateTreePage<TNode>`: one self-referencing aggregate, loaded flat into `SerginTreeIndex<TNode>`. Same parameters, minus `SelectedContent`/`UnselectedContent`, plus `NodeHref`. |
| `SerginTreeSelect` | `Trees/` | Marker a page renders to say which node it shows: `<SerginTreeSelect Node="@Id.ToString()" />`. Optional `Group` (with several groups) and `Tail` (a label after the node's path, e.g. "New scope"). Renders nothing. |
| `ScopeTreeLayout` | UserAccess `Scopes/Components/` | `@inherits LayoutComponentBase`, `@layout SerginMainLayout`, markup = `<SerginAggregateTree TNode="ScopeTreeItem" …>@Body</SerginAggregateTree>`, code-behind = what `ScopeTreePage.razor.cs` holds today (load, labels, add child, delete). |

Unchanged: `SerginTreeBranch<TNode>`, `SerginTreeIndex<TNode>`, `SerginTreePageHeader` (path, header buttons section, ⋮ menu, ×), `IUiTreeLayoutStore`, `SerginActionMenu`, `SerginActionIconButton`, `SerginActionVisibility`, the `Pane` host kind and `ChangedAsync`.

Removed: `SerginTreeSelection` and `?node=`, `SerginTreeSelectedNode` if nothing else needs it, group `SelectedContent` and the content `SectionOutlet` it projected into, `SerginPage`.

## Mechanisms

**The layout instance survives navigation.** Blazor's `LayoutView` re-renders the same layout chain; when two pages name the same layout type, the diff keeps the layout component and everything under it, so only `@Body` changes. That is what keeps the tree's state. `SerginMainLayout` keys its body on the scope version, so a scope change still rebuilds the tree, on purpose.

**A thin module layout, not a generic base class.** A layout receives no parameters from the page, so the tree's configuration must live in a layout type. A module writes a small `.razor` layout that renders `SerginAggregateTree` with markup parameters and `@Body` as its child content. A derived `.razor` with `@inherits SomeBase<T>` would replace the base's render tree, so subclassing is the wrong tool here.

**Selection comes from the page.** The layout cannot read the page's route parameters. Each pane page renders a `SerginTreeSelect` marker; on parameters set, it calls `SerginTreeContext.Select(group, node, tail, owner: this)`, and the tree highlights the node, loads and opens its ancestors through `PathTo` if needed, and draws the tree header path. On dispose, a marker clears the selection **only if it is still the owner**, so the next page's marker, which may initialize before the old one is disposed, is not undone. A page with no marker (`/ua/scopes`) leaves the selection empty: the tree clears its selection on `LocationChanged` before the new page renders, and an incoming marker sets it again.

**Clicks navigate.** A label click, a path link in the tree header, Enter on a row: `NavigateTo(group.NodeHref(node))`. The tree header's × and Esc navigate to the tree's own root route (`SerginTree.Href`, `/ua/scopes`). Back/forward are plain browser history.

**The `Pane` host.** `SerginTree` cascades `new SerginViewHost(Pane, leave, headerSectionId, changed)`:
- `leave(href)`: reload the tree, then navigate to `href`. A delete from the detail view leaves to `/ua/scopes`; a create's save leaves to the new node; cancel leaves to the parent.
- `changed()`: reload the tree (rename, move).
- The view's header buttons still project into the tree header's section.

**Insert in the pane.** `CreateScopePage` (`/ua/scopes/new?parentId=…`) takes `@layout ScopeTreeLayout`, renders `<SerginTreeSelect Node="@ParentId" Tail="New scope" />` and `ScopeCreateView`. `ScopeCreateView` is the existing form moved into a view on `SerginFormView`, the way `DeviceCreateView` is built. Save leaves to `/ua/scopes/{newId}`; the tree reloads, opens the parent and selects the new node through that page's marker. Cancel leaves to `/ua/scopes/{parentId}`. "Add root" is the same route with no `parentId` (if the module allows it), with no node selected.

**Page title and header.** The tree owns the `<PageTitle>` and the page header trail (`Scopes`), as now. The tree header shows the selected path plus `Tail`. A pane view renders no title or trail, as now.

**Loading, filter, keyboard, layout, resize.** Unchanged from revision 1: roots on first render, children on expand with skeletons, `Eager`, `PathTo`; filter over loaded labels in the active group; roving tabindex with arrows, Enter, Esc on the row; panel fills the page body with fixed rows and scrolling groups, content side with fixed header and scrolling body, stacked below md; pointer overlay resize clamped 200–480 px, saved on release.

## DeviceManagement and UserAccess pages

- **Every routable view page** drops `<SerginPage>` and declares `@layout SerginPageLayout`: the 11 DeviceManagement pages that use the wrapper now. UserAccess's `ScopeDetailPage` moves to `@layout ScopeTreeLayout`.
- **Where `@layout` goes.** In each page file, not in `_Imports.razor`. A `_Imports.razor` `@layout` applies to every component in the folder tree, layouts and non-page components included, and UserAccess still has plain pages that are not views. My call; a builder can move DeviceManagement's to its `_Imports.razor` if every component there is safe under it.
- **UserAccess plain pages** (users, roles, the old-style ones) keep the shell's default layout and `SerginBreadcrumbs`. Unchanged.
- **`ScopeTreePage`** (`/ua/scopes`) becomes a page with `@layout ScopeTreeLayout` whose body is the "Select a scope" prompt.

## Constraints

- Markup-only `.razor`, C# in `.razor.cs`, `public sealed partial class`, no `@code`; warnings are errors.
- Three repos: SharedKernel PR, UserAccess PR, host PR (DeviceManagement pages, tests, docs, submodule bumps). Work continues on the existing unmerged branches in the worktree `Sergin.MeterMinder-tree-page`.
- Every module route still starts with `/{schema}/`; the startup guard is untouched.

## Left to the builder

- Whether `SerginTreeContext` is the `SerginTree` itself cascaded or a small separate object.
- Whether `SerginTree` takes `Href` explicitly or reads it from the first group.
- Names of internal types and CSS classes.

## Deviations from the wireframe

- **No `MudTreeView`.** The rows are a recursive component, for label-selects/chevron-expands, lazy levels with skeletons, filter ancestor expansion and the keyboard model. The markup carries `role="tree"`/`treeitem`/`group`, `aria-level`, `aria-expanded`, `aria-selected`.
- **Menus and dynamic actions are `Func<TNode, IReadOnlyList<SerginPageAction>>`**, so the permission rule hides items the way it hides the actions bar.
- **The route is the binding**, not `@bind-Selected` and not `?node=`.
- **Several groups** still work: each group's `NodeHref` names its own route prefix under the same layout, and a marker names its group.

## Verification

- `Shell/TreeIndexTests`: unchanged. `Shell/TreeSelectionTests`: deleted with `SerginTreeSelection`.
- `Shell/TreePageRenderingTests`, rewritten:
  - `/ua/scopes` prerenders the tree and the prompt;
  - `/ua/scopes/{id}` prerenders the tree with ancestors open, the node `aria-selected`, the view in the pane, one header strip and one title;
  - `/ua/scopes/new?parentId={id}` prerenders the tree with the parent selected, "New scope" in the tree header and the form in the pane;
  - an unknown id renders the view's not-found panel with nothing selected.
- `Shell/ViewHostRenderingTests`: DeviceManagement pages still render their title and header through `SerginPageLayout`.
- `ScopedRbacPageRenderingTests` and every DeviceManagement rendering test pass unchanged.
- Full integration suite and `Sergin.UserAccess.UnitTests`.
- Headless browser (Playwright on :5555): the revision 1 checks rewritten for routes, plus:
  - expand a branch, open "Sub-scope", and confirm the branch stays open;
  - save, and confirm the new node is selected and the URL is its route;
  - cancel returns to the parent;
  - delete from the view returns to `/ua/scopes`;
  - back/forward walk detail → create → detail;
  - a DeviceManagement detail page still shows its header buttons;
  - no console errors.
