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
- Task 8 (resolved): stale ScopeDetailView comment in ScopeTreeLayout, stale add-feature and UserAccess CLAUDE.md prose, root test-folder list.

## Final review triage

