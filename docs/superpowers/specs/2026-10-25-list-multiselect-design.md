# List multiselect and bulk delete — design

Date: 2026-10-25. Follows `2026-10-24-aggregate-page-types-design.md`, which left wireframe 4b (multiselect mode
and bulk commands) out of scope.

## Problem

`SerginAggregateList<TItem>` and `SerginEntityList<TItem>` had per-row commands only. Deleting ten devices took ten
confirm dialogs.

## Decisions

1. **Multiselect is on whenever a list has a bulk action.** A page passes `BulkActions`, a list of
   `SerginBulkAction<TItem>` (label, icon, kind, `OnRun(selected)`, permission, `AnyScope`). Any one turns on
   MudTable's checkbox column. There is no Select mode toggle.
2. **A row click does not toggle the checkbox** (`SelectOnRowClick="false"`). Rows are not click targets; only the
   ID column links.
3. **The selection covers the page on screen only.** Every load clears it: page, page size, search, filter, sort,
   and the reload after a bulk run. The header checkbox selects the visible rows.
4. **While rows are selected, `SerginSelectionBar` shows above the table**: "N selected", the bulk actions, and
   Clear. The actions go through `SerginActionBar`, so a forbidden one is hidden and Danger actions sit at the far
   end, the same as the page's actions bar. The bar's buttons are disabled while a run is going.
5. **No bulk command on the server.** `SerginBulk.RunAsync` sends one command per item, in order, as the row command
   would. Each item gets the full pipeline: permission, version and validation. One refusal does not undo the
   others. An optional stop rule ends the run, and the items after it count as skipped.
6. **One report per run.** `SerginBulkOutcome.Report` shows one success snackbar with the count (`3 deleted`), each
   distinct error once (ten identical "in use" refusals show as one), and the number of items not sent.
7. **The version rules are the row command's.**
   - Device and manufacturer: each item reads its version just before its delete, the same as the row delete.
   - Device models: every remove moves the manufacturer's version, so each send carries the version the previous
     one returned. A stale answer stops the run, because every later send would carry the same stale version.
     After the run the page reloads its version.

## Where it is used

| Page | Bulk action | Permission |
|---|---|---|
| `DeviceListPage` | Delete | `DevicePermissions.Delete`, current scope |
| `ManufacturerListPage` | Delete | `ManufacturerPermissions.Delete`, any scope |
| `DeviceModelSection` | Remove | `ManufacturerPermissions.Update`, any scope |

## Out of scope

- A selection kept across pages, and "select all N matching".
- Bulk commands on the server that run in one transaction.
- Export (4b).

## Verification

- 423 integration tests pass. New tests:
  - `Lists/SerginBulkTests`, with no database: order, refusals collected, the stop rule and skipped count, the
    report's distinct errors;
  - `Shell/PageTypeRenderingTests`: the device list prerenders the checkbox column and no selection bar.
- A headless-browser walk against a live circuit passed 18 of 18 checks:
  - the bar's count, Clear, and the header checkbox;
  - a sort clears the selection;
  - bulk delete of two devices, one confirm, one `2 deleted` snackbar;
  - bulk remove of a free model and a model in use: `1 removed`, the refusal once, and a following single remove
    is not stale;
  - bulk delete of three manufacturers, one in use: `2 deleted` and the refusal.
