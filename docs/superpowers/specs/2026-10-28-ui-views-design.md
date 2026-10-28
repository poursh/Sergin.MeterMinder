# UI views: the page / view split

Date: 2026-10-28. Status: implemented for SharedKernel and DeviceManagement. UserAccess follows in its own PR.

## Why

Before this change, each module page did two jobs. It was *a page*: a route, a `<PageTitle>`, and a
`SerginPageHeader` projected into the layout. It was also *the content*: load, row version, problem panel, overview,
actions, fields and child collections. SharedKernel's page types (`SerginAggregatePage`, `SerginEntityListSection`)
only arranged slots. Because of this, aggregate or entity content could only appear on its own route. It could not
appear in a modal or inside another component.

The goal is a base that later features can build on: a modal detail view, or a view embedded elsewhere. It does not
serve one particular screen today. The design follows a hand sketch with three parts:

- a **host**: `SerginPage`, which has a header (breadcrumb and header actions) and a body;
- **content components** for an aggregate or an entity;
- content that can be rendered "as a page, a modal, or inside another component".

## Decisions

- **Composition, not inheritance.** Every type is a wrapper component. The sketch said "Base", but these are not base
  classes, so they take a `View` suffix.
- **Smart views.** A view loads its own record, holds the row version, shows its problem, builds its actions and
  declares its title, trail and header buttons. The host decides where those parts go. A dumb view (render-only, fed
  by its host) was rejected: every host would then repeat the loading code.
- **`SerginAggregateView` is a pass-through.** Today a root's view and an entity's view do the same things. The type
  exists so that root-only behaviour (audit, version, scope) can land in one place later.
- **Child-entity detail open mode is configurable.** The only mode now is `Page`.
- **Forms are views too** (`SerginFormView`), so that create and edit can open in a modal later.
- **Scope:** SharedKernel plus DeviceManagement. UserAccess keeps the old components until a follow-up PR in its own
  repo. The old wrappers stay, marked as superseded.

## Component tree

```
SerginPage                         host: cascades SerginViewHost(Page)
SerginView                         Title/Trail/HeaderActions/Problem/Ready + Overview/Actions/ChildContent/Count
 ├─ SerginAggregateView            pass-through seam
 │   ├─ SerginAggregateListView    body = SerginAggregateList (search, filter panel, bulk)
 │   └─ SerginAggregateDetailView  Fields (in SerginDetailFields) + Entities
 ├─ SerginEntityListView           body = SerginEntityList; cascades SerginDetailOpenMode to SerginEntityLink
 └─ SerginEntityDetailView         Fields
SerginFormView                     MudForm + card + submit/cancel
```

All new types live in `Sergin.SharedKernel.Presentation.Blazor.Views`. `SerginViewBody` draws the overview, the
actions bar and the body. The page shape and the embedded shape both use it.

## Host mechanism

- `SerginViewHost` has a `Kind` (`Page` or `Embedded`) and a `LeaveAsync(href)` method. A view calls `LeaveAsync`
  after a delete, a save or a cancel. It does not navigate by itself. A page host navigates. A dialog host, when one
  is added, will close.
- `SerginPage` cascades a fixed `Page` host.
- Every `SerginView` cascades `Host.Embedded()` to its body. A nested view therefore renders as a section and never
  claims the page header.
- A view declares `[CascadingParameter] SerginViewHost Host { get; set; } = SerginViewHost.Unhosted;`. A view
  rendered outside any host is embedded. If it calls `LeaveAsync`, it fails with an error that names the mistake.

## Rendering per host

- **Page:** `<PageTitle>` from `Title`. Then `SerginPageHeader` (trail plus `HeaderActions`), projected into the
  layout through the same section as before. Then the problem panel, then the body parts once `Ready` is true. The
  prerendered markup keeps the old class names, so the existing rendering tests pass unchanged.
- **Embedded:** the outlined, titled, collapsible section that `SerginEntityListSection` used to draw, showing
  `Count`. Collapsing it keeps the body rendered. The title and trail are not shown, and neither are the header
  buttons.

## Module side (DeviceManagement)

The content of every page moved to `<Aggregate>[/<ChildEntity>]/Components/<Name>View.razor{,.cs}`. A page is now
only `@page` plus `<SerginPage><NameView … /></SerginPage>`. Its code-behind holds only the route parameters.
`DeviceModelSection` became `DeviceModelListView`, embedded in `ManufacturerDetailView`'s `Entities` slot.
`DeviceModelDetailView` became a `SerginEntityDetailView`: Rename and Remove are now permission-gated actions
(`ManufacturerPermissions.Update`, checked on any scope) instead of card buttons. The headings of the Add and Rename
model forms changed from h4 to h5, the size every `SerginFormView` uses.

## Left for later

- A dialog host (`SerginViewHostKind.Dialog`) and the `SerginDetailOpenMode.Modal` value. They ship together, so
  there is never an open mode that does nothing.
- Moving UserAccess to views. After that, delete `SerginAggregatePage` and `SerginEntityListSection`, and rename the
  table components `SerginAggregateList` and `SerginEntityList` to `…Table`.

## Verification

- `dotnet build Sergin.MeterMinder.slnx` is clean.
- The integration suite passes (427 tests), including the new `Shell/ViewHostRenderingTests`:
  - one header row on the manufacturer detail page;
  - the embedded model list renders as a section;
  - the entity detail actions;
  - the form view shape.
- A headless walk on a live circuit passed. It covered:
  - creating a manufacturer with Enter;
  - adding a model;
  - opening the model through the entity link;
  - Remove, which leaves to the manufacturer;
  - Cancel on the edit form;
  - deleting the manufacturer, which leaves to the list.
