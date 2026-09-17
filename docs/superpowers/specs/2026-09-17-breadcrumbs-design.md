# Breadcrumbs declared by the page, rendered by the shell

## Motivation

Every module page tells the user where they are through a `<PageTitle>` and an `h4`/`h5` heading,
and nothing else. The four detail pages each carry a `Back to devices` / `Back to manufacturers` /
`Back to users` / `Back to manufacturer` button; the four create pages carry `Cancel`. There is no
trail showing the hierarchy, and the deepest pages —
`/dm/manufacturers/{id}/models/{id}` and `/dm/manufacturers/{id}/models/new` — are two hops below
a nav entry with no visible path back through the manufacturer that owns the model.

This spec adds one breadcrumb strip above every page, `Home > Section > … > current page`, and
removes the detail pages' back buttons, which the strip's parent link replaces.

## Scope

In:

- A SharedKernel component pair, `SerginBreadcrumbs` (page side) and `SerginBreadcrumb` (one
  step), plus one line in `SerginMainLayout` that hosts the strip.
- Every routable module page declares its trail: eight in DeviceManagement, three in UserAccess.
- Each module's `<Module>Navigation` exposes its nav entries by name, so a page's section step is
  built from the entry the drawer renders rather than a second copy of the label and href.
- The four `Back to …` buttons are removed. `Cancel` on the create pages stays: it is form
  semantics, not navigation chrome.
- Integration tests over the prerendered HTML; docs and the `/add-feature` skill updated.

Out:

- Deriving trails from the URL. A GUID segment has no label until the page loads its record, so
  the shell could only ever guess at the tail — and the page already knows.
- A `Models` crumb. Device models have no list page of their own (`DeviceModelTable` lives inside
  the manufacturer's detail page), so there is nothing for such a crumb to link to.
- Composing the browser-tab title from the trail. `PageTitle` stays as it is.
- Localisation resources. Labels go through `ILocalizer` exactly as nav labels do; nothing else
  changes there.

## Decisions

1. **The page declares, the shell renders.** A page knows its own hierarchy and the name of the
   record it loaded; the shell knows where every trail sits. So the page passes its steps to a
   shared component, and the layout owns the one slot they appear in. (Chosen over URL derivation
   and over each page rendering its own `MudBreadcrumbs` inline.)
2. **Blazor sections, not a state service.** `SectionContent` on the page and `SectionOutlet` in
   the layout is the mechanism `PageTitle`/`HeadOutlet` already use in this shell. It works under
   prerender, re-renders with the page once an async load fills the tail, and empties when a page
   that declares no trail is shown. It needs no scoped registration, no event, no unsubscribe —
   every one of which a circuit-lifetime state service would need, and each of which is a way to
   render a stale trail. The section is identified by an `object` (`SerginBreadcrumbs.SectionId`),
   not a string name, so no other section can collide with it.
3. **The home step is the shell's.** `SerginBreadcrumbs` prepends `SerginHome.NavItem` — label
   and `/` — when the host has one, under the same rule as the drawer: a host that called
   `WithoutNavItem()` gets no home crumb either. A page never names home.
4. **The last step is never a link.** The component drops the last step's href and renders it
   disabled, whatever the page passed. MudBlazor keeps a disabled item's href on its anchor, which
   is why the href is dropped rather than only disabled: the current page should not link to itself
   in any reading of the markup.
5. **The section step comes from the nav entry.** `SerginBreadcrumb.Of(SerginNavItem)` builds it,
   and `DeviceManagementNavigation.Devices`/`.Manufacturers` and `UserAccessNavigation.Users` are
   the entries by name, with `Items` now composed from them. Label and href have one home.
6. **A detail page's tail starts as its page title and becomes the record's name.** The trail is a
   computed property, so it re-reads the loaded record on every render; until the load — and on
   the not-found path, beside `SerginProblemPanel` — the tail is the `<PageTitle>` word (`Device`,
   `Manufacturer`, `User`, `Device model`).
7. **`AddDeviceModelPage` loads the manufacturer to name it.** The page had only the id from the
   route. One `GetManufacturerByIdQueryCommand` per visit, under the permission anyone reaching
   this page already holds, fills the middle step; a failure is deliberately silent — the step
   keeps its `Manufacturer` placeholder, and an unknown manufacturer is already reported by the
   submit as not-found, so a breadcrumb label is not worth a second snackbar.
8. **Back buttons go; Cancel stays.** The strip's parent link is the back button. A create form's
   `Cancel` is part of the form and stays.

## Verified before designing

- The prerendered HTML of every page shape (list, create, detail, nested create, nested detail)
  contains the strip inside `<nav aria-label="Breadcrumb">` with the expected links, and `/`
  contains no such element — captured from a running host before the tests were written.
- A live circuit (headless Chromium, Playwright): clicking a crumb navigates in-app and the strip
  re-renders for the new page without a reload; `AddDeviceModelPage` shows the manufacturer's
  name; the home page shows no strip; no `Back to` text remains anywhere.
- MudBlazor 9.9.0 renders `MudBreadcrumbs` as `<nav aria-label="Breadcrumb"><ol
  class="mud-breadcrumbs …">` with one `<li class="mud-breadcrumb-item">` per step and a disabled
  step as `<li class="mud-breadcrumb-item mud-disabled"><a href="#">…</a></li>` — the shape the
  tests assert.

## Design

### SharedKernel (`Sergin.SharedKernel.Presentation.Blazor/Navigation/`)

`SerginBreadcrumb(string Label, string? Href = null)` — a sealed record, one step, with
`static Of(SerginNavItem)`.

`SerginBreadcrumbs` — `[Parameter, EditorRequired] IReadOnlyList<SerginBreadcrumb> Trail`,
injects `SerginHome` and `ILocalizer`, and in `OnParametersSet` builds the `BreadcrumbItem` list:
home first when present, then each step with its label localised, the last step's href dropped and
the item disabled. Markup is a `SectionContent` for `SectionId` wrapping
`<MudBreadcrumbs Items="items" Class="pa-0 mb-4" />`.

`SerginMainLayout` renders `<SectionOutlet SectionId="SerginBreadcrumbs.SectionId" />` directly
above `@Body`, inside the existing `MudContainer`.

### Pages

Every routable page renders `<SerginBreadcrumbs Trail="Trail" />` right after its `<PageTitle>`
and declares a `private IReadOnlyList<SerginBreadcrumb> Trail` in the code-behind:

- List page — a get-only property: `[SerginBreadcrumb.Of(DeviceManagementNavigation.Devices)]`.
- Create page — a get-only property: the section step, then `new("New device")`.
- Detail page — an expression-bodied property: the section step, then
  `new(device?.DeviceId ?? "Device")`.
- Nested pages under a manufacturer add a middle step linking by the route parameter:
  `new(deviceModel?.ManufacturerName ?? "Manufacturer", $"/dm/manufacturers/{ManufacturerId}")`.

The home slot (`SerginHomePage` → the host's `MeterMinderHome`) declares nothing.

## Testing

`tests/Sergin.MeterMinder.IntegrationTests.All/Shell/BreadcrumbRenderingTests.cs`, in the
`ModulePageRenderingTests` shape (shared factory, prerendered HTML, string assertions), with a
regex that cuts the `<nav aria-label="Breadcrumb">` element out of the page so assertions cannot
be satisfied by the drawer, which links the same hrefs:

- list pages: a home link, the section as the disabled last step, no link to the page itself;
- create pages: a link to their list, the title as the last step;
- the nested create page: a link to its manufacturer by the route id, `Manufacturer` as the
  placeholder label;
- a detail page for a user created through `ISerginDispatcher`: the user name as the last step —
  proving prerender awaits the load and the section re-renders with it;
- a detail page for an unseeded id: the placeholder tail beside the problem panel;
- a detail page contains no `Back to` text;
- `/` contains no breadcrumb element.

## Files

SharedKernel (own repo): `Navigation/SerginBreadcrumb.cs`, `Navigation/SerginBreadcrumbs.razor`
+ `.razor.cs`, `Layout/SerginMainLayout.razor`, `_Imports.razor`, `.claude/CLAUDE.md`.

UserAccess (own repo): `UserAccessNavigation.cs`, `Users/Pages/{UserListPage,UserDetailPage,
CreateUserPage}.razor` + `.razor.cs`, `GlobalUsings.cs`, `_Imports.razor`, `.claude/CLAUDE.md`.

This repo: `DeviceManagementNavigation.cs`, the eight DeviceManagement pages, `GlobalUsings.cs`,
`_Imports.razor`, the test class, `src/Modules/DeviceManagement/CLAUDE.md`, root
`.claude/CLAUDE.md`, `.claude/skills/add-feature/SKILL.md`, and the two submodule bumps.
