# Soft deletion as an aggregate feature

**Date:** 2026-09-28
**Status:** Implemented on `feat/soft-delete` (host) and `feat/soft-delete` (Sergin.SharedKernel)
**Builds on:** [2026-09-23-aggregate-configuration-audit-design.md](2026-09-23-aggregate-configuration-audit-design.md)

## Problem

Nothing in the platform could delete anything. `IRepository.Remove` existed, but no slice called it. A
hard delete would also lose the row a meter-data platform needs to keep: who removed a device, when, and
what it was. The audit design already set the pattern for a behaviour that applies to a whole aggregate —
declare it once in `<Root>AggregateFeatureConfiguration`, and let a SharedKernel convention and interceptor
do the rest — and said the builder "grows one method each" when the next feature is needed. Soft deletion
is that next feature.

## Decisions

1. **Declared, not hand-written.** `builder.SoftDeletable()` on `AggregateFeatureBuilder<TRoot>`, chainable
   with `Audited()`. No per-aggregate mapping, filter or interceptor code in a module.
2. **The timestamp is the flag.** Two nullable shadow columns, `deleted_at_utc timestamptz` and
   `deleted_by uuid`. A row is live exactly when `deleted_at_utc IS NULL`. No `is_deleted` bool: it would
   be a second source of truth that can drift, and a generated column buys nothing v1 needs.
3. **The pair can't be half-set.** CHECK `ck_<table>_soft_delete`:
   `(deleted_at_utc IS NULL) = (deleted_by IS NULL)`, added by the convention, so raw SQL is held to it too.
4. **The performer is the scope's `IUserContext`**, like audit: the signed-in user through
   `ISerginDispatcher`, the relay identity during outbox delivery. A delete stamps `deleted_*` only;
   `modified_*` keeps describing the last edit.
5. **A deleted row frees its alternate key.** The convention adds `deleted_at_utc IS NULL` as the filter of
   every unfiltered unique index on a soft-deletable type. `IsTakenAsync` goes through EF and so already
   ignores deleted rows — the validator and the index agree.
6. **Named EF query filter** `Sergin:SoftDelete` (EF 10). Named so a later tenant filter can coexist, and so
   `IgnoreQueryFilters([SoftDeleteColumns.QueryFilterName])` lifts only this one.
7. **Children always inherit.** No `ExceptChild` for soft delete in v1 — a child that could be hard-deleted
   while its root is soft-deleted has no use case yet.
8. **The delete cascades through the aggregate in the interceptor.** No DELETE reaches Postgres, so its
   `ON DELETE CASCADE` never fires. The interceptor walks principal-to-dependent navigations, loading the
   ones not loaded, and stamps every child with the same instant and actor.
9. **Owned types get no columns.** EF refuses a query filter on an owned type, and an owned type is only
   loaded through its owner. The interceptor puts owned entries EF cascaded to `Deleted` back to
   `Unchanged`, so owned rows survive with their owner.
10. **No restore, no purge** in v1.

## Mechanism

| Piece | Where | What |
|---|---|---|
| `AggregateFeatures.SoftDeletable` | SharedKernel.Application | Flag; `ForChild` carries it unchanged |
| `SoftDeleteColumns` | SharedKernel EFCore `Aggregates/` | Names, `NotDeletedSql`, `PairCheckSql`, `IsSoftDeletable` |
| `AggregateFeatureConvention` | same | Columns, annotation, named filter, CHECK, partial unique indexes |
| `SoftDeleteInterceptor` | SharedKernel EFCore `Interceptors/` | `Deleted` → `Unchanged` + stamps, cascade, owned rows kept |
| `AggregateFeatureGuard` | same `Aggregates/` | Refuses start when a context doesn't apply `SoftDeletable()` |
| `EfRepository.SetOf<T>()` | SharedKernel EFCore `Repositories/` | Cross-set yes/no lookups without CS9107 |

Interceptor order in `AddModuleDbContext`: `EventDispatcherInterceptor` → `AuditStampInterceptor` →
`SoftDeleteInterceptor`. Audit skips `Deleted` entries, so running soft delete last keeps `modified_*` out
of a delete.

## What a module still writes by hand

- **Raw-SQL reads.** Dapper queries are not reached by EF's filter. Each query repository adds
  `deleted_at_utc IS NULL` for the table it reads — never for a joined table, so a live row still shows
  the name of a deleted row it points at.
- **References that `RESTRICT` used to protect.** A soft delete fires no foreign key. DeviceManagement's
  `DeleteManufacturerCommandValidator` refuses while a live device uses one of the manufacturer's models
  (`IDeviceRepository.AnyUsingManufacturerAsync`). Advisory, like every repository rule.
- **A migration** in the same PR that turns the feature on (`AddSoftDeleteColumns` is the reference).

## DeviceManagement

- `Device` and `Manufacturer`: `builder.Audited().SoftDeletable()`; `DeviceModel` inherits.
- `DeleteDevice` / `DeleteManufacturer`: `Guid Id` command, `<Feature>CommandResponse(Guid Id)`,
  `[RequiredPermissions("permission.dm.{devices|manufacturers}.delete")]`, `DELETE` endpoints, and a Delete
  button with a `MudMessageBox` confirm on each detail page. Handler: load, `NotFound` if missing,
  `repository.Remove`, save. A second delete is `NotFound` — the filter hides the row.
- Permissions: granted to the dev user in `appsettings.json`. The seeded `administrator` role already holds
  `permission.sys.platform-all`; `viewer` deliberately gets no delete.

## Verification

- `SoftDelete/SoftDeleteColumnsTests` — design-time `dm` model shape (check constraints exist only in
  `IDesignTimeModel`, not the runtime model).
- `SoftDelete/SoftDeleteInterceptorTests` — test-only aggregate with a child, an owned collection and a
  unique key: stamping, hiding, unloaded-child cascade, owned rows kept, dropped child, key reuse, 23514.
- `SoftDelete/DeviceManagementSoftDeleteTests` — both slices through `ISerginDispatcher`.
- `Aggregates/` registry and guard tests extended for `SoftDeletable`.
- Headless browser walk (2026-09-28): in-use manufacturer refused with a snackbar, Cancel keeps the page,
  device and manufacturer deletes navigate to their lists, a deleted device's page shows the problem panel,
  and all three rows carry `deleted_by` with `modified_at_utc` still `NULL`.

## Later

- Restore (`IgnoreQueryFilters` lookup plus a behaviour that clears the pair).
- Purge of rows deleted longer ago than a retention window.
- A `SoftDeletable(Action<…>)` overload if a child ever needs a real delete.
- Translating Postgres 23514 into `ErrorOr`, with the other SqlStates.
