# Scoped aggregates: design

Date: 2026-10-17. Builds on `2026-10-09-scoped-rbac-design.md`. Scope of this change: SharedKernel (the feature, the checks, the filter), UserAccess (path resolver, reach, move event) and DeviceManagement (`Device` adopts it).

## Problem

Scoped RBAC could answer "may this user do P on scope S", but records had no scope. A command could only opt in to a check on the caller's current scope (`InCurrentScope()`), and doing that on device commands would have let a North operator reach every device, since nothing tied a device to North. So device checks stayed global, and claims stamped at sign-in decided them.

## Decisions

| Question | Decision | Why |
|---|---|---|
| Where is "this aggregate lives in a scope" declared? | An aggregate feature, `builder.Scoped()`, next to `Audited()`, `SoftDeletable()`, `Versioned()`. | It is application policy over the whole aggregate; the domain type and the commands do not change. |
| What does a row store? | `scope_id uuid` and `scope_path ltree` (GiST indexed). | The path makes "inside this subtree" one indexed comparison; the id finds rows after a scope move. |
| How does a record get its scope? | Stamped from `IAuthorizationScopeContext` on insert; an update never changes it. | No record move in this change. |
| Which permissions are checked where? | Every configured permission on the current scope. `AtRoot()` checks on the root instead. `InCurrentScope()` is removed. | Once rows carry a scope, there is nothing left for the opt-in to protect, and an opt-in leaves every unconfigured read unfiltered. |
| What about global data? | An unscoped aggregate lives at the root. Its writes are checked there by the save; global requests say `AtRoot()`. | A regional holder of `manufacturers.update` cannot change the catalog, even when a configuration forgets `AtRoot()`. |
| What may a read see? | Rows inside the current scope's subtree **and** inside the caller's reach for every required permission. | A scope-only grant at North must not list Feeder 1's devices just because Feeder 1 is below North. |
| What does an unconfigured request see? | No scoped row (a system admin sees everything). | Fail closed. |
| Claims? | None. `IUserContext` carries identity only, and sign-in stamps only `sergin:user_id`. | Every check reads stored grants, so a change applies on the next request. |
| Which aggregates? | `Device` only. `Manufacturer` and its `DeviceModel`s stay a global catalog. | `CreateDevicePage` needs the catalog at any scope. |
| Unprotected creates? | `CreateDevice` requires `devices.update` on the current scope; `CreateManufacturer` and `AddDeviceModel` require `manufacturers.update` `AtRoot()`. | A new device lands in the caller's scope, so creating one is a write there. |

## Mechanism

**Pipeline.** `PermissionCheckPipelineBehavior` checks each permission through `IScopeAuthorization` on the context scope, or the root for `AtRoot()`. On success it publishes the permissions to `RequiredPermissionsContext` and fills `ScopeFilter` with the current scope's path (`IScopePathResolver`) and the intersection of the caller's `ScopeReach` for each permission. Both are restored when the request ends, so a nested send leaves the outer values in place.

**Reach.** `IScopedPermissionChecker.GetReachAsync(userId, permission)` returns one `ScopeReachEntry(ScopeId, Path, IncludeSubtree)` per active grant holding the permission (or `AllPlatform`). A system admin's reach is `Everything`; a failed lookup is `Nothing`. Two reaches intersect entry by entry: two subtrees meet in the deeper one, a single scope survives when the other side covers it.

**Read filter.** The named EF query filter `Sergin:Scope`:

```
ScopeVisible
AND scope_path <@ ScopeCurrentPath
AND (ScopeUnrestricted OR scope_path <@ ANY(ScopeSubtreePaths) OR scope_id = ANY(ScopeExactIds))
```

The values are internal properties of `SerginDbContext` read from the request's `ScopeFilter`; the convention references the context instance, which EF replaces with the running context per query. Raw-SQL reads add `ScopeColumns.ReachSql` with `ScopeColumns.AddParameters`. Out of reach is `Error.NotFound()`, so existence does not leak.

**Exceptions to the filter.** Uniqueness and in-use checks (`IsTakenAsync`, `IsTakenByOtherAsync`, `AnyUsingModelAsync`, `AnyUsingManufacturerAsync`) ignore `Sergin:Scope` and keep soft delete: a key taken in another scope is still taken, and a model used by a device the caller cannot see is still used. Existence checks keep it: out of reach counts as missing.

**Stamping.** `ScopeStampInterceptor` sets `scope_id` and `scope_path` on a new scoped root; an unknown scope is `ForbiddenException`. On an update both columns are left out.

**Record check.** `RecordScopeInterceptor` checks the published permissions on every record the save writes: a scoped root on its stored scope, a child on its root's, an unscoped record at the root. A new scoped root is skipped, since the pipeline checked the scope it is stamped with. A denial is `ForbiddenException`, answered `Error.Forbidden()`; nothing is saved. With nothing published (unconfigured requests, outbox consumers, system code) nothing is checked.

**Scope moves.** `Scope.MoveUnder` raises `ScopesMovedDomainEvent` with the moved scope and its subtree; UserAccess translates it to SharedKernel's `ScopesMovedIntegrationEvent` (`sk.scopes.moved.v1`, ids only). Every module context has a `ScopePathRewriteHandler<TContext>`, which re-resolves each id's current path and runs one `UPDATE … SET scope_path WHERE scope_id` per scoped table. It writes the current path, not one carried by the event, so redelivery and reordering are harmless and no inbox is needed. It bypasses interceptors: no row-version bump, no modification stamp.

**Navigation.** `SerginNavMenu` shows an entry when the user holds its `RequiredPermission` on any scope (`IScopeAuthorization.CurrentUserReachAsync` non-empty), so the drawer does not depend on the scope the circuit works in; the page answers for the current scope. `SerginNavItem.AtRoot` checks on the root instead (UserAccess's Users entry, whose requests are `AtRoot()`).

## Migration `AddDeviceScope` (dm)

Enables `ltree`, adds both columns nullable, backfills every existing device with the root (`01920000-0000-7000-8000-0000000000b1`, path `019200000000700080000000000000b1`), makes them `NOT NULL`, then adds the b-tree index on `scope_id` and the GiST index on `scope_path`.

## Known gaps

- Deleting a scope does not see other modules' records; a device left in a deleted scope is unreachable except by a system admin. A per-module usage probe could refuse the delete later.
- Path rewrites are eventual, within the relay poll (5 s by default).
- An unscoped read without `AtRoot()` (the catalog reads) is visible at any scope where the caller holds its permission. Deliberate for the catalog; decided per configuration.
- A remote gateway without UserAccess refuses every non-system caller: it needs a real checker before any module runs remotely.
- An unconfigured command's writes are not record-checked. Every write command that touches a scoped or global aggregate should carry a configuration.

## Verification

- `tests/.../Scoping/DeviceScopeTests`: stamping and update keeping the scope; subtree and reach on list and GetOne (subtree grant, scope-only grant, system admin, pipeline refusal); the EF filter per query and per context; an update out of reach changing nothing; a device id taken in another scope as a validation error; `CreateDevice` on the current scope only; a scope move rewriting the stored path, with a repeated event harmless.
- `tests/.../Scoping/RecordScopeTests`: a manufacturer write without `AtRoot()` refused for a North holder and allowed for a root holder and the relay identity.
- `tests/.../Authorization/CurrentScopeTests`: every configured command follows the current scope; `AtRoot()` checks the root; a caller with no stored grant passes nothing.
