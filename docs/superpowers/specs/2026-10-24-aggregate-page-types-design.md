# Aggregate page types — design

Date: 2026-10-24. Source: the Claude Design project "Meter Minder Wireframes" (`Meter Minder Wireframes.dc.html`),
round 4 — options 2b, 3a/3b/3d and 4a, 4c, 4d, 4e, 4f.

## Problem

Every Device Management page hand-rolled its layout: an h4, a `MudTable` or `MudCard`, buttons placed by hand.
Search, sort and filter were absent because the list read side ignored `Term`, `Filtering` and `Sorting`. Audit
stamps and row versions were stored but never shown. The wireframes converged on a small set of reusable page types
so that every aggregate gets the same frame.

## Decisions

1. **Round 4 only.** Built: BasePage (2b), DefaultAggregatePage (3a, collapsed form 3b, action rules 3d),
   AggregateListPage (4a, 4c) and AggregateDetailPage (4d, 4e, 4f). Not built:
   - multiselect mode and bulk commands (4b);
   - Export;
   - the round 1 alternatives;
   - the UserAccess pages.
2. **Structure, not look.** The wireframes are drawn in the Industry wireframe kit (steel-blue, Barlow, `+` corner
   marks). That is the kit's look, not the product brand, so the components use MudBlazor defaults.
3. **The page types live in SharedKernel** (`Presentation.Blazor/Pages/`), so every module and every future host
   gets them. The read-side helpers live there too: `ListFieldSet`, `ListQueryValidator`, `ListQuerySql`,
   `AuditInfo` and `IUserDirectory`.
4. **The header reuses the breadcrumb section.** `SerginPageHeader` projects into the existing `SectionOutlet`. The
   trail and its `MudBreadcrumbs` markup are unchanged, so the breadcrumb tests still hold. `SerginBreadcrumbs` is
   now the header with no icons.
5. **Responsive fold by CSS, not by a breakpoint listener.** The icons render twice: in a row from md up, and inside
   one `Page actions` menu below md. The prerender is right without JS.
6. **Hide, don't disable, a forbidden action** (3d allowed either). The check goes through `IScopeAuthorization`:
   - on the current scope for a `Scoped()` aggregate;
   - with `AnyScope` on any scope, mirroring how the pipeline checks a configuration bound to an unscoped aggregate.

   This is presentation only; the pipeline still enforces the permission.
7. **List criteria are declared once.** A feature's `ListFieldSet` (in `.Application.Contracts`) names its sortable
   and filterable fields. The validator refuses everything else as `Error.Validation` (code `Sorting`/`Filtering`).
   The query repository maps each id to an expression in `ListQueryColumns`. `ListQuerySql` composes the result:
   - only declared expressions reach the SQL text, and every value is a parameter;
   - one WHERE goes to the count and to the page, so the total matches the filtered list;
   - the id is always the tie-breaker in `ORDER BY`;
   - LIKE wildcards in a search are escaped;
   - `between` is half-open and `betweenInclusive` is closed. The UI sends a day range as
     [start of first day, start of day after last) in the viewer's time zone.
8. **The filter panel's fields belong to the page.** The list asks the page to build the applied filters, shows them
   as chips, and tells the page when a chip is removed.
9. **Audit loads on open.** Each aggregate has a `Get<Aggregate>AuditQueryCommand`, under the same `Read` permission
   and reach rules as GetOne. `DeviceQueryResponse` is unchanged because it is on the gRPC wire. User ids become
   names through `IUserDirectory`:
   - UserAccess implements it;
   - `EmptyUserDirectory` is the fallback, which shows ids.
10. **Counts that decide Remove are in every scope.** `ManufacturerOverview.Devices` and
    `GetDeviceModelListItem.DeviceCount` count all live devices, because `RemoveDeviceModelCommandValidator` counts
    the same way. A count within the caller's scope could show 0 for a model that Remove then refuses.
    `DeviceOverview` counts devices within the caller's reach and the catalog unscoped.
11. **A row delete follows the detail page's version rule.** It reads the version just before it sends. Both delete
    paths share one confirm dialog (`ConfirmDeleteAsync`).

## Out of scope / follow-ups

- Multiselect, bulk delete and Export (4b).
- The "Created by" filter. It needs a user picker that crosses modules.
- Moving UserAccess pages to the page types. `UserQueryRepository` and `DeviceModelQueryRepository` still read
  paging only.
- Help content. `SerginHelpButton` renders only when it has an `Href`.

## Verification

- 416 integration tests pass. New tests:
  - `Lists/ListQuerySqlTests`, with no database;
  - `Lists/DeviceListQueryTests`;
  - `Lists/OverviewAndAuditTests`;
  - `Shell/PageTypeRenderingTests`;
  - the configuration count and binding assertions.
- 12 unit tests pass.
- A headless-browser walk against a live circuit passed 13 of 13 checks:
  - audit popover;
  - copy link;
  - search with its chip;
  - sort descending;
  - manufacturer filter with its chip;
  - row delete with confirm;
  - clear all;
  - Remove disabled for a model in use;
  - device counts;
  - the narrow-viewport fold.
