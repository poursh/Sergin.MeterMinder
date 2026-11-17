# Generated aggregate views, Plan 4 — carry-forward for Plan 5

Rulings made during Plan 4 execution and findings deferred to later plans, copied from the execution ledger. Plan 4 converted UserAccess's User, Role and Scope to generated views and deleted `SerginScheduleAction` (its `SerginScheduleRequest` and `ShowScheduleAsync` stay, in `SerginScheduleRequest.cs`).

## Rulings
- Ruling: Cascade checkbox moves into delete confirm dialog; RoleUiTexts.DeleteCascadeConfirm deleted — only the delete reads Cascade — cost: confirm text no longer varies.
- Ruling: RoleUiTexts.InheritableOnDetail deleted; InheritableOnCreate becomes form label override — cost: one helper line.
- Ruling: "New child scope" = generated self-parented create (create permission); "New role here" stays Link (no permission).
- Ruling: AppliesWhen on non-pair command is a documented feature (DeleteScope/MoveScope use it).
- Ruling (T6-T7 order): Task 6 adds a minimal ScopeViewConfiguration (Routes list/detail/create only, empty otherwise) if the guard needs it; Task 7 completes it. Hosting pages stay on old Scope views until T7 — a view config without page change is harmless (routes match existing [Route]s). — cost if wrong: small churn in T7.
- Ruling: T1 Dialog test ran in-process (no fallback needed); EditAsync/SubmitAsync/Cancel internal as seams — cost: none.
- Ruling: accept extra refusal "Confirm on a command no view asks before sending" — prevents silent no-op — cost: low.
- Ruling: accept extra refusals (Parent on update/child form; form Field naming no shown field) — catch dead config — cost: low.
- Ruling: keep child AppliesWhen in SK CLAUDE.md Deferred (still unbuilt, only refused) — accurate — cost: none.
- Ruling: accept extra move/tree/section refusals — catch dead config — cost: low.
Task 6: opus implementer a373cd0 hit 429 weekly budget twice, no changes made; re-dispatched fresh on sonnet aee2c76f11d8e94f8. Ruling: sonnet for remaining implementers — opus budget exhausted — cost: more turns.
- Ruling (T6 concern 1): role commands get RequirePermissions(RolePermissions.Manage) in their Application configurations (Create, Rename, SetInheritable, Grant, Revoke, Delete). Role is not Scoped() so the pipeline checks it on any scope (= old view's Manage+AnyScope hiding); handlers keep the owner-scope check. Restores reader hiding through config, not view config (spec anti-goal) — cost: pipeline refuses a caller with no Manage anywhere one step earlier (handler would refuse anyway). Same applies to Scope commands in Task 7.
- Ruling (T6 concern 2): CreateRolePage defaults ScopeId to Scope.RootId when absent (as CreateScopePage does); revert the PseudoLocaleSweep route edit — keeps "/ua/roles/new" rendering — cost: none.
- Ruling (T7 ⚠️ trail): moot — scope pages render in the tree's Pane host, which renders no view trail (the tree's header shows the node path); TreePageRenderingTests unchanged and green — cost: none.
- Ruling (T8): the SerginScheduleAction class is deleted but its file also held SerginScheduleRequest and ShowScheduleAsync, which the generated views and ScheduleProbePage still use; the file is renamed SerginScheduleRequest.cs — cost: none.

## Deferred findings
- Task 1: minor (deferred): EditDetailHost.View static mutable test field; Dialog test ~10s, cancel checked on stale dialog; root CLAUDE.md test list lacks Aggregates/HardenedGuards (Task 8 docs).
- Task 2: minor (deferred): RefuseUnshownConfirmValues suggests Hidden() but hidden delete still refused (skip check when hidden or drop advice); List_DeleteWithValues bulk comment overstated / ConfirmFields clause redundant; ConfirmDelete on transition asks w/o guard, on update silently ignored; DeleteWithValuesAsync duplicates FailedAsync notify+reload.
- Task 3: minor (deferred): BlankAware passes no-primitive value unconverted (cast error at send); list CreateAction lacks parentId for self-parented root (no guard); CountOf==2 test brittle; GeneratedInput.ComponentParameters allocs per render; plain delete confirm glue tested via seam only.
- Task 4: minor (deferred): MoveToAsync duplicates versioned-or-plain send choice of SendFromIdAsync; no re-read assert on refused move; no refusal tests for move ctor shape / NewParentId text / Tree<T> wrong item / Section without Context; using order AggregateViewTestTypes.cs:3; move dialog tree-read failure only snackbar.
- Task 5: minor (deferred): GeneratedUserViewTests HtmlOnceAsync polls 5s (view reload doesn't await section load).
- Task 5: minor (deferred): html.IndexOf(">Id<") loose substring order assertions.
- Task 6: minor (deferred): heading-leak test green per implementer's ScopedRbac filter run (210/210).
- Task 8 (resolved): ScopeTreeLayout stale `ScopeDetailView` comment; stale hand-written-view prose in add-feature/add-module skills and the UserAccess and root CLAUDE.md; root test-folder list. Not resolved: the open walk items below; every deferred line above is still open.
- Task 8 walk, fixed (final wave): after a scope rename or move the header breadcrumb kept the old node path; `SerginTree.ReloadActiveAsync` now re-renders the tree after the group reload. No render-level test (no circuit harness for the tree layout); covered by the one-line change.
- Task 8 walk, carry: Deactivate is still offered on a deactivated user (`UserQueryResponse` has no `IsActive`; adding it is the fix).
- Task 8 walk, closed: Read profile shows no snackbar within 3 s because `SimulatedReadDuration` is 3 s; the snackbar comes after the send.
- Task 8 walk, expected: deleting a role without Cascade while it is assigned is refused, the error is notified and the role stays (`GeneratedRoleViewTests.RoleDelete_WithoutCascade_IsRefusedWhileAssigned_AndTheRoleStays` asserts one notification).

## Final review triage


## Leftovers pass (2026-11-17)

Fixed:
- Walk carry: `UserQueryResponse` gains `IsActive`; the user detail shows "Active" and offers Deactivate only on an active user (`AppliesWhen`). New key `user-access.users.fields.is-active.label` (catalog pin and the `fa` seed row added).
- Task 2: `ConfirmDelete(...)`/`ConfirmDeleteMany(...)` on anything but a root's delete or a child's removal is now a host-start refusal (it asked with delete texts on a transition and was ignored on an update).
- Task 2: `RefuseUnshownConfirmValues` advice — already right: a `Hidden()` delete builds no confirm fields (`Guard_HiddenDeleteWithHiddenExtraValue_Builds`).
- Task 3: a field component's value of the wrong type for a non-primitive parameter is refused naming the request and parameter, not a bare cast error in the compiled constructor.
- Task 3: the BelongsTo create-trail test asserts the Cancel anchor's href instead of counting parent links.
- Task 4: one `GeneratedViewSupport.SendAsync` holds the versioned-or-plain send choice (`SendFromIdAsync`, `MoveToAsync`, `DeleteWithValuesAsync`).
- Task 4: a tree that cannot be read shows its problem inside the move dialog, select and move disabled, no snackbar.
- Task 4: refusal tests for a move constructor other than `(Id, NewParentId)`, a move with no `NewParentId` text, `Tree<T>` naming another item, and a Section component with no `Context`.

Set aside (ruling — why — cost if wrong):
- Task 1 static `EditDetailHost.View`, ~10 s Dialog test; Task 5 `HtmlOnceAsync` 5 s poll and `IndexOf(">Id<")` order checks — test-only, collection-serialized, bounded waits — a flaky test later.
- Task 2 `DeleteWithValuesAsync` vs `FailedAsync` — they differ on purpose (always re-read vs re-read on stale) — none.
- Task 2 bulk comment wording — cosmetic — none.
- Task 3 list `CreateAction` without `parentId` for a self-parented root — no such root has a list page (Scope's list route is its tree) — the create form throws on render if one appears.
- Task 3 `GeneratedInput.ComponentParameters` per-render dictionary — `DynamicComponent` takes fresh parameters each render anyway — negligible.
- Task 4 re-read assert on a refused move — the probe test already checks the error once and no generation change — a missed re-read regression.
