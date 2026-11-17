# Generated tree views — design

Date: 2026-11-17. Follows plan 4 of the generated aggregate views
(`2026-11-12-generated-aggregate-views-design.md`, carry-forward
`docs/superpowers/plans/2026-11-15-generated-aggregate-views-4-carry-forward.md`).

## Intent

A tree-shaped aggregate (one whose root is read through an `ITreeQuery`, today only UserAccess's `Scope`) still
needs about eight hand-written Blazor files around its generated views: a tree layout that hands
`SerginAggregateTree` some twenty parameters one function at a time, a prompt page, the "child scopes" table, and a
parent-link field component. Almost every value those files pass is already known to the aggregate's view
configuration (its tree query, routes, create command, delete command, permissions, title).

Two reasons, both the user's:

1. **Now:** remove the scope tree's hand-written pieces, so Scope is generated like every other aggregate.
2. **Next:** a second tree aggregate (e.g. a DeviceManagement hierarchy) should cost one view configuration, a
   three-line layout and three one-line route pages.

## Goals

- `view.Tree<TItem>(tree => …)` carries the few facts the configuration cannot infer: node icon, texts for the
  filter, empty tree, prompt, and the children section.
- A SharedKernel component `SerginGeneratedTree<TRoot>` renders the whole tree panel from the configuration.
- A SharedKernel component `SerginGeneratedTreePrompt<TRoot>` renders the "select a node" page.
- The generated detail and form views select their node in a surrounding tree themselves; pages stop rendering
  `SerginTreeSelect`.
- Every tree aggregate's detail view gets a generated **children section** (user's choice, option A): the node's
  descendants, read from the tree query.
- The parent field renders as a link to the parent, or a configured "no parent" text (through the existing field
  `Link`, see below).

## Non-goals

- No source generator. `@page` routes and the `@layout` class stay written by the module (Blazor needs both as
  compile-time names).
- No change to `SerginTree`/`SerginAggregateTree`/`SerginTreeBranch` behaviour (keyboard, lazy levels, filter,
  persistence). The generated component is a consumer of `SerginAggregateTree`, not a rewrite.
- No multi-group trees (several `SerginTreeGroup`s in one tree) generated; `AdditionalGroups` stays a hand-written
  escape hatch.
- No drag-to-move, multi-select, Shift+F10 (still not built).
- The "roles usable here" table stays a scope-only slot; it is not tree-generic.

## Anti-goals

- A tree that looks or behaves differently from today's scope tree after the change (same icons, same menu, same
  permissions, same selection and reload behaviour).
- Config that only one aggregate could ever use. Each new builder method must make sense for any tree.

## Approaches considered

1. **Generated layout base class** (`public sealed class ScopeTreeLayout : SerginGeneratedTreeLayout<Scope>;`).
   Smallest module code, but no place for a module to add anything around the tree.
2. **Generated tree component inside a small module layout** — **chosen by the user.** The layout is three lines
   of markup (`@inherits LayoutComponentBase`, `@layout SerginMainLayout`, `<SerginGeneratedTree TRoot="Scope">@Body</SerginGeneratedTree>`),
   the same "routable file + one generated component" pattern every generated view already uses, and the module
   can wrap or add markup when it needs to.
3. **Source generator** emitting layout and pages. Rejected: new build tooling for a saving of ~8 lines per
   aggregate.

## The configuration

`TreeViewBuilder<TItem>` today has only `Title(...)`. It gains (names are the builder's to finalize; the facts are
fixed):

| Method | Meaning | Default |
|---|---|---|
| `Title(x => …)` | node label (exists) | first `string` property |
| `Icon(x => string?)` | node icon | none |
| `Filter(TextDefinition)` | filter placeholder | a new common text ("Filter…") |
| `Empty(TextDefinition)` | text when the caller sees no node | a new common text |
| `Prompt(TextDefinition title, TextDefinition? hint)` | the prompt page's texts | a new common text, no hint |
| `Children(TextDefinition title, TextDefinition empty)` | the children section's title and empty text | new common texts ("Children", "None.") |

`Prompt` also takes an optional icon (`Prompt(title, hint, icon)`); without one the prompt shows a generic tree icon.
Scope passes `Icons.Material.Outlined.AccountTree`, today's icon (designer's call).

Scope's configuration after the change, for orientation:

```csharp
view.Tree<ScopeTreeItem>(tree => tree
    .Title(x => x.Name)
    .Icon(x => x.ParentId is null ? Icons.Material.Outlined.AccountTree : Icons.Material.Outlined.FolderOpen)
    .Filter(ScopeUiTexts.FilterPlaceholder)
    .Empty(ScopeUiTexts.TreeEmpty)
    .Prompt(ScopeUiTexts.SelectPrompt, ScopeUiTexts.SelectHint)
    .Children(ScopeUiTexts.ChildScopes, ScopeUiTexts.NoChildScopes));
```

## What is derived, not configured

`SerginGeneratedTree<TRoot>` reads `AggregateViewRegistry.For<TRoot>()` and passes `SerginAggregateTree`:

- **Load**: the model's `TreeView.Query` (exists since plan 4), sent through `ISerginDispatcher`.
- **NodeId / ParentId / NodeLabel**: `TreeView.IdOf`, `ParentIdOf`, `TitleOf`.
- **NodeHref**: the detail route. **Href** (the tree's own page): the list route (`Routes(list: …)`).
- **TitleText**: `view.Title(...)`. **Trail**: one step, the tree's title linking to the list route (today it is
  `SerginBreadcrumb.Of(UserAccessNavigation.Scopes)`, the same label and href).
- **AddChild**: `AggregateViewModel.ChildCreate` (the self-parented create command: its label, icon, permission and
  `AnyScope`), linking to `ChildCreateHref`. No create route or no `IUnderParent` create: no add-child.
- **Delete**: the root's delete command, with the delete permission and `AnyScope` from its command view (for Scope:
  `ScopePermissions.Manage`, any scope, as today) and `DeleteTitleText` from its `ConfirmDelete` title. No shown
  delete: no Delete in the node menu. The tree deletes leaves only (`SerginAggregateTree` already disables Delete on a
  node with children), so a delete whose constructor is `(Id, values…)` is built with each extra value at its type's
  default — `DeleteScopeCommand(id, Cascade: false)`, exactly today's call — and sent plainly or at a freshly read
  version when guarded, as a list-row delete is. The detail view keeps the full values dialog. (Designer's call.)
- **LoadAudit**: the root's `IAuditQuery`, when it has one.
- **LayoutKey / GroupKey**: LayoutKey is the schema plus the list route's last segment (`/ua/scopes` gives
  `ua.scopes`); GroupKey is the root type name in lower case (`scope`). Both equal today's values, so stored panel
  widths survive. (Designer's call.)

## Pages after the change

```razor
@* ScopeTreePage.razor *@
@page "/ua/scopes"
@layout ScopeTreeLayout
<SerginGeneratedTreePrompt TRoot="Scope" />

@* ScopeDetailPage.razor *@
@page "/ua/scopes/{Id:guid}"
@layout ScopeTreeLayout
<SerginGeneratedDetailView TRoot="Scope" @key="Id" Id="Id" />

@* CreateScopePage.razor *@
@page "/ua/scopes/new"
@layout ScopeTreeLayout
<SerginGeneratedFormView TRoot="Scope" ParentId="ParentId" />
```

The detail and form views select their node when a tree is cascaded to them (the detail its `Id`; the create form
its `ParentId`, with the form's title as the trail's tail) and render nothing extra when there is none. Mechanism
(designer's call): each view renders a `SerginTreeSelect` itself, which already reads the cascaded `SerginTree` and
does nothing without one; no new cascade, and `SerginTree`/`SerginAggregateTree` stay unchanged. The form passes its
title as `TailText`. Selection hand-over between pages keeps today's rule (only the owning marker's release clears).

## The children section

On a root that has a `Tree`, the generated detail view renders, in the detail-entities area before any configured
`DetailEntities` slot (so above Scope's roles table, today's order), a titled section listing the node's descendants
(all of them, as today's `ScopeTreeTable` with `UnderId` does; the "Level" column is dropped, indentation shows depth): depth first, siblings by title in the
current culture, indented by depth below the node, each title linking to its detail route; the empty text when it
has none. It reads the tree query when the view loads and again whenever `AggregateViewContext.Generation` moves
(as `ScopeRelationsSlot` does today). A tree that cannot be read shows its problem inside the section (no snackbar),
as the move dialog does.

## The parent field (designer's call, revised after the builder check)

No new `BelongsTo` behaviour. The existing field `Link(Func<T, string> href)` (Role already uses it for "Owned by")
gains a companion `WhenEmpty(TextDefinition)`: when the field's value is null the text is shown, unlinked. Scope's
detail becomes
`detail.Field(x => x.ParentName).Label(ScopeUiTexts.Under).Link(s => $"/ua/scopes/{s.ParentId}").WhenEmpty(ScopeUiTexts.TreeRoot)`,
replacing `ScopeParentValue`. Role's page is unchanged. (The earlier draft's "no parent" text on `BelongsTo` is
dropped.)

## UserAccess after the change

Deleted: `ScopeTreeLayout.razor.cs`, `ScopeTreeTable.razor{,.cs}`, `ScopeParentValue.razor{,.cs}`, the child-scopes
half of `ScopeRelationsSlot` (it keeps the roles table and is renamed to match, e.g. `ScopeRolesSlot`).
`ScopeTreeLayout.razor` shrinks to the three lines above. Texts no longer read (`NoScopes`, `Level`, `SubScope` if
the child-create label replaces it) are removed from `ScopeUiTexts`, from `catalog-keys.txt` (`MigrationPinTests`)
and from the `fa` rows in `dev-seed.sql`.

## Refusals

- On render, not at host start (no host-start check can know which component a layout renders):
  `SerginGeneratedTree<TRoot>` or `SerginGeneratedTreePrompt<TRoot>` for a root with no `Tree` or no list route throws,
  naming the root, as `SerginGeneratedListView` does for a tree root today.
- Existing host-start tree refusals in `AggregateViewGuard` stay.

## Tests (designer's call)

The model and the refusals are proved on test-only probe types beside `ParentProbe`/`ChildProbe` (model-level, no
database needed for a tree that is never read); the rendered tree, prompt, selection, children section and parent
field are proved on the real Scope pages, extending `GeneratedScopeViewTests` and `TreePageRenderingTests`.

## Constraints

- All rules in the root `CLAUDE.md` apply: warnings are errors, markup-only `.razor`, every visible text declared,
  pseudo-locale sweep green, scope pages still under `/ua/`.
- Three repos: SharedKernel PR, UserAccess PR, then the root PR bumping both (plus `catalog-keys.txt`, seed, tests).
- Behaviour must match today's scope tree; `TreePageRenderingTests`, `TreeIndexTests`, `GeneratedScopeViewTests`
  stay green, extended where the generated pieces need proof.

## Left to the builder

- Final method names on `TreeViewBuilder<TItem>` and `BelongsTo`.
- Where the common default texts live (`CommonTexts` or a tree texts class) and their keys.
- Internal shape of `SerginGeneratedTree` (it may wrap `SerginAggregateTree` or construct its parameters in code).
- Test-only probe types for the generated tree, following `ParentProbe`/`ChildProbe`.
