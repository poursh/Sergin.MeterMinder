# Module navigation groups

Date: 2026-10-23

## Why

The drawer was one flat list. `SerginUiModuleCatalog` merged every module's `NavItems` and sorted them by
`SerginNavItem.Order`, so the module an entry came from was lost on the way to the screen. Users could not
tell that Devices and Manufacturers belong to Device Management and Users and Scopes to User Access. The goal
is context: a user always sees which module the page they are on belongs to.

## Decisions

1. **One collapsible group per module, with the module's icon and title.** The drawer renders the home entry
   and, per module, a `MudNavGroup` holding that module's entries. Home belongs to no module and stays a
   top-level link.
2. **The breadcrumb strip names the module too**: `Home > Device Management > Devices > X`. The module step is
   not a link, because a module has no page of its own.
3. **Every UI module must name its group.** `ISerginWebUiModule` gains a required `SerginNavGroup NavGroup`.
   There is no default derived from `Schema`: a schema is a database name, not a display name.
4. **`SerginNavGroup(Title, Icon, Order)`** lives in `Sergin.SharedKernel.Modules` beside `SerginNavItem`, with
   the same contract rules: `Title` goes through `ILocalizer`, `Icon` is a plain string, so the contract
   stays free of any UI library.
5. **Two orders.** `SerginNavGroup.Order` sorts the groups, with ties broken by title, and places home among
   them by home's own `Order`. A module can therefore still sort above home, as it could before.
   `SerginNavItem.Order` now sorts only within its group. Existing values keep working: Device Management
   100 (Devices 100, Manufacturers 110) and User Access 200 (Users 200, Scopes 210).
6. **Expansion.** The group of the page shown opens on the first render (prerender included) and on every
   `LocationChanged`. The match uses the prefix rule `NavLinkMatch.Prefix` applies, so a detail page opens
   its section's group. The drawer only opens groups, never closes one: a group the user opened stays open
   while they navigate elsewhere.
7. **Permission filtering stays per entry.** A group none of whose entries the user may see is not rendered.
8. **The module step needs no page change.** Every module page's first trail step is
   `SerginBreadcrumb.Of(<Module>Navigation.X)`, so `SerginBreadcrumbs` looks up that step's href with
   `SerginUiModuleCatalog.GroupOf(href)`, an exact href match over module entries. It inserts the group's
   title after home. A trail whose first step is not a module entry gets no module step.

## Shape

- `SerginUiModuleCatalog.NavSections`: `IReadOnlyCollection<SerginNavSection(Group, Items)>`, in drawer
  order. `NavItems` stays, flattened in the same order: the host-start check for unregistered
  `RequiredPermission`s reads it.
- `SerginNavMenu` is `IDisposable` and unsubscribes from `LocationChanged`.
- Device Management: `DeviceManagementNavigation.Group` is `("Device Management",
  Icons.Material.Filled.ElectricMeter, 100)`. User Access: `UserAccessNavigation.Group` is `("User Access",
  Icons.Material.Filled.AdminPanelSettings, 200)`.

## Not done

- No module landing page, so the module step is not a link.
- No mini-drawer (icon-only) mode. The group icons would make one possible later.
- No remembered expansion state across circuits.

## Tests

- `Shell/NavSectionCatalogTests`: group order, item order within a group, the flattened `NavItems`, and
  `GroupOf`.
- `Shell/ModulePageRenderingTests`: the prerendered drawer has one group per module, only the current page's
  group is open, and on `/` no group is open.
- `Shell/BreadcrumbRenderingTests`: the order Home > module > section, for list, create, nested and detail
  pages.
- Not covered by an automated test: hiding an empty group. A `DevUser` host always renders as the seeded
  administrator, so no prerendered page shows a user with no visible entry.
