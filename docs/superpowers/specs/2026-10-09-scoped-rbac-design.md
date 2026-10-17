# Scoped RBAC: design

Date: 2026-10-09. Requirements: "Scoped RBAC Authorization: Requirements" (FR-1 to FR-13, BR-1 to BR-8, NFR-1 to NFR-8, AC-1 to AC-10). Scope of this change: the UserAccess module, plus one small SharedKernel seam. DeviceManagement adopting scoped checks is a later change. The authorization audit log (BR-8, NFR-6) is deliberately not built.

## Problem

Authorization was flat. A user held roles; a role held permissions; at sign-in the union was stamped into the cookie as `sergin:permission` claims, and `PermissionCheckPipelineBehavior` checked those. Nothing could say "this user operates North Region and nothing else". The requirements ask for a tree of scopes, roles owned by scopes, assignments per scope with optional subtree coverage and validity period, and a runtime check on a target scope.

## Decisions

| Question | Decision | Why |
|---|---|---|
| What do the existing flat claims mean now? | **Global grants only**: live, untimed assignments at the root over the whole tree. | Existing roles migrate to exactly that, so nothing changes for current users. A regional user gets no global rights, which is the safe direction (NFR-5). |
| How is the tree stored? | **Postgres `ltree`** materialised path with a GiST index. | One indexed comparison answers ancestor/descendant (NFR-2). A move rewrites the subtree's paths in one save. The requirements doc assumed SQL Server and a closure table; this repo is PostgreSQL. |
| Where does the check contract live? | **SharedKernel** `IScopedPermissionChecker` and `IScopeAuthorization`, implemented by UserAccess. | Other modules and a later pipeline behavior can call it without referencing UserAccess. |
| Are inheritable roles the default? | **Opt-in per role** (`IsInheritable`). | FR-4 reads "a role marks itself". |
| Is "this scope only" needed? | **Yes**, `IncludeSubtree` per assignment. | FR-7 is a must. |
| Does a conflicting move block or flag? | **Block** (validation error). | BR-6, AC-8. |
| Hard or soft delete? | **Soft** for scopes and roles. | Revoked assignments keep naming what they were for. |

## Model (schema `ua`)

- `scopes(id, name, parent_id, path ltree, audit, soft delete, row_version)`. One root, seeded with id `01920000-0000-7000-8000-0000000000b1`. Sibling names unique among live rows.
- `roles` gains `owner_scope_id` and `is_inheritable`. The global unique index on `name` becomes `(owner_scope_id, name)` among live rows. BR-4 (no repeat along the ancestor line) is a validator rule, advisory like every repository rule.
- `role_assignments(id, user_id, role_id, scope_id, include_subtree, valid_from_utc, valid_until_utc, revoked_at_utc, revoked_by)` replaces `user_roles`. A child entity of `User`.

Keys to soft-deletable rows use `DeleteBehavior.ClientNoAction`. With `Restrict`, removing a role while its assignments were tracked made EF null the key before the soft-delete interceptor turned the delete into an update.

### Migration `AddScopedRbac`

Every existing role becomes owned by the root and inheritable (a global role, BR-7). Every `user_roles` row becomes an assignment at the root over the subtree with no end date. A row naming a role that no longer existed (the old table had no key to roles) is dropped. Effective permissions are unchanged; the existing provisioning tests prove it.

## Rules and where they are enforced

| Rule | Where |
|---|---|
| BR-1 permission ceiling | Role create and grant handlers: the actor must hold each permission in the owner scope. Validation error `Permissions`. |
| BR-2 assignment scope | `User.AssignRole` via `Role.IsAssignableAt`. |
| BR-3 visibility | `GetAssignableRoles` query. |
| BR-4 code uniqueness along the line | Create and rename validators (ltree query); owner-scope part guaranteed by index. |
| BR-5 delete in use | Validators refuse unless `Cascade`; the cascade revokes assignments and soft-deletes owned roles. Only a leaf scope can be deleted. |
| BR-6 move | `MoveScopeCommandValidator` query: no live assignment in the moved subtree whose role owner is outside it and not above the new parent. `Scope.MoveUnder` refuses the root and cycles. |
| BR-7 global roles | Root-owned inheritable roles; provisioning assigns `viewer` that way. |
| FR-13 deactivation | The grant query joins `users.is_active`; sign-in claims are empty for an inactive user. Assignments are kept. |

## The check

`ScopedPermissionChecker` loads a user's live grants in one indexed query (assignment scope path, subtree flag, validity window, permissions), caches them per user, and evaluates the target in memory: a grant applies on its own scope, and below it when it covers the subtree. `permission.sys.platform-all` in a role grants everything the role reaches. The cache carries a generation; every write handler calls `Invalidate()` after its save commits, so a revocation is denied on the next check (NFR-3, AC-9). Entries also expire after five minutes.

`IScopeAuthorization` is what handlers call for the signed-in user: it answers by scope only. The claims are never consulted; every check asks the checker by user id, and any exception is a denial (AC-10). The one exception is a system admin (the outbox relay identity, which has no user row). In DevUser mode the bootstrap seeds the configured dev user with the `administrator` role at the root (`IDevUserSeeder`), so the dev host passes scoped checks through a stored grant.

Management permissions (`permission.ua.scopes.*`, `roles.*`, `assignments.manage`) are checked against the target scope inside each handler, not by `RequirePermissions`, because a regional administrator holds them only there.

## Current scope

`IAuthorizationScopeContext` (SharedKernel) holds the scope the caller is working in, per DI scope. It is the root until code sets it: a scope picker for an explicit choice (deferred), or the code opening the scope when the system knows the target. Nothing derives it from the user's grants. It travels into a send's scope the way `ConcurrencyContext` does: the Blazor dispatcher and form validator copy the circuit's value, WebApi reads the `X-Sergin-Scope` header, gRPC the `sergin-scope` metadata. Outbox consumers run at the root.

`PermissionCheckPipelineBehavior` checks every permission a configuration requires through `IScopeAuthorization` on the context scope (by scope only; any error denies), or on the root for a configuration that adds `AtRoot()`. There is no opt-in any more: the `InCurrentScope()` flag was removed once records could carry a scope (see `2026-10-17-scoped-aggregates-design.md`), which also narrows reads to the caller's reach and checks every write on the record's own scope. UserAccess management handlers keep checking the record's own scope, since their target is the record, not where the caller is working.

## Version guards

Scope rename and every role edit carry an expected version. Scope move and scope delete do not: a move re-paths several versioned scopes and a cascading delete also deletes roles, and an expected version may guard only one aggregate per save. Each row is still protected by its own concurrency token.

## Known gaps

- Claims are still stamped at sign-in and copied forward by `CookieOidcRefresher`, but they authorize nothing: every check reads stored grants, so a change applies on the next request.
- The grant cache is per process. A second host instance would need the invalidation broadcast (the outbox is the natural carrier).
- The drawer's Scopes entry is gated on the global `scopes.read`; a regional administrator reaches the pages by link.
- The pipeline runs validators before the handler, so before the handler's scoped check: a validator's message (a name in use, a stranding move) can reach a caller the handler would have refused. The pipeline's check on the current scope runs ahead of validation; checks on a record's own scope stay in the handler or in the save's record check.

## Verification

- `tests/Sergin.UserAccess.UnitTests`: 12 domain rule tests (NFR-7).
- `tests/.../Authorization/`: AC-1 to AC-10 and the business rules, end to end against Postgres.
- `tests/.../Shell/ScopedRbacPageRenderingTests`: the new pages prerender with data.
- A headless-browser walk on a throwaway database: create scope and child, create a role with a permission, assign it to a user, see effective permissions, revoke.
