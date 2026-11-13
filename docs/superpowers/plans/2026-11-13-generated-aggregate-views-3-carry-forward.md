# Generated aggregate views, Plan 3 — carry-forward for Plan 4

Rulings made during Plan 3 execution and findings deferred to later plans, copied from the execution ledger.

## Rulings
- Ruling: T6 keeps LanguageUiTexts.DirectionResolved; the detail Direction field gets a Component<LanguageDirectionValue>() (rung 2) rendering "{Setting} ({Resolved})" for FromCulture, else the value text; managers get the inline select (aria-label "Direction") instead; no separate ResolvedDirection field and no new key — the plan's own Review Focus 3 and the unchanged reader test require today's reader text, spec says converted commands keep behaviour — cost if wrong: manager loses the resolved-direction helper line under the select (small visible change, noted for user).
- Ruling: plan doc committed on root branch first; SK and Settings branches generated-aggregate-views-3 created by Task 1 implementer — cost: none.
- Ruling: untracked global.json (sdk 10.0.404, rollForward latestFeature) in worktree root so builds use SDK 10, not the default 11.0.100 whose Razor generator fails; never committed, removed before PRs — cost if wrong: user may want it committed (ask at finish)
- Ruling: Task 5 extended (before review) — the compiled list builder passes each parameter's default value for an optional constructor parameter beyond Paggination/Term/Filtering/Sorting/parent id (a required extra one stays refused); test on the parented probe list query with one optional extra — general, one place, keeps generated tables usable for such queries too — cost if wrong: a query whose optional extra matters gets its default silently on a generated table (only a ListBody slot sends it otherwise)
- Ruling: Task 5's end-of-task full-suite run counted as the implementer's pre-extension 1056/3 run plus Aggregates 163/163 after fixes; Task 6 runs the full suite — cost if wrong: a non-Aggregates regression from the fixes surfaces one task later
- Ruling: accept splitting LanguageDetail_StalePublish_ReloadsAndOffersUnpublish into PublishedElsewhere_ReloadsAndOffersUnpublish (no-op publish at old version succeeds by platform rule, reload offers Unpublish) + StalePublish_IsRefusedAndReloadKeepsDraft (rename elsewhere then publish at old version = stale) — together they pin Review Focus 1's intent (stale refused once, reload shows current state); the brief's single test contradicts the platform's "version sent but nothing changed succeeds" rule — cost if wrong: none functional

## Deferred findings
- Task 1: minor (deferred): no captured RED run (process)
- Task 1: minor (deferred): FieldTextGuard_NullableEnumIsChecked_AndRealHostPasses re-calls Guard_PassesForRealHost (redundant, two-in-one name)
- Task 1: minor (deferred): FieldTextGuard reports enum with no FieldText once per field and per member (noisy)
- Task 1: minor (deferred): TextCatalog.ValueTextsOf allocates empty dict per miss, FindValue linear scan (start-time only)
- Task 1: minor (deferred): [Flags]/aliased enums unsupported in EnumTexts (none exist)
- Task 1: minor (deferred): IsRightToLeft ? RightToLeft : LeftToRight duplicated 3x in Language views (Task 6 deletes them)
- Task 1: minor (deferred): SK CLAUDE.md Localization not updated for Values/EnumTexts (Task 8)
- Task 2: minor (deferred): enum guard lives in AggregateViewModelJoin, not AggregateViewGuard (still refuses at registry build, names type)
- Task 2: minor (deferred): tests don't render a list enum column, the default Yes/No, or select value-text items
- Task 2: minor (deferred): S2344 pragma on public test enum ProbeEnum
- Task 2: minor (deferred): task-2-report header says BLOCKED (stale)
- Task 3: minor (deferred): GeneratedSetProperty.Errors is shared mutable state across dialog/cards/choices; overlapping sends can show wrong errors — return errors with status (final review to triage)
- Task 3: minor (deferred): Inline Select/Switch whose detail field is hidden / not a field / replaced by DetailFields slot renders nowhere — refuse in EnsureEditable or fall back to card
- Task 3: minor (deferred): form submit validates twice (repository rules query twice)
- Task 3: minor (deferred): inline card loses typed value after stale reload (dialog keeps it)
- Task 3: minor (deferred): InlineEdit_InvalidValue_SendsNothing bypasses GeneratedValueForm; Detail_DialogCommand_IsABarAction weak (counts text); inline Switch untested; Dialog flow unexercised (browser walk Task 8 only covers Inline)
- Task 3: minor (deferred): render tests' manager is a Read+Update caller, not dev user
- Task 4: minor (deferred): child IReverseOf pairs must declare AppliesWhen but child views never apply it (accepted then ignored) — refuse on child or wire it
- Task 4: minor (deferred): AppliesWhen on a non-pair command accepted silently
- Task 4: minor (deferred): list bulk skips AppliesWhen commands — undocumented
- Task 4: minor (deferred): List_ReversePair_NotOffered lacks positive control
- Task 4: minor (deferred): guard calls link href(Guid.Empty) unguarded
- Task 4: minor (deferred): RED captured by stash after draft (not test-first)
- Task 5: minor (deferred): overview on a ListUnder root sent without parent id, not refused
- Task 5: minor (deferred): no test that header Reload bumps Context.Generation for body slot; no end-to-end generated table under parent
- Task 5: minor (deferred): ViewProbeNoteQueries.ParentIds static shared across tests; no dedicated test for parent=null label
- Task 6: minor (deferred): GeneratedLanguageViewTests:197 uses loaded.Version without asserting !IsError; :235 DoesNotContain("IsRightToLeft") weak; "–" literal in LanguageProgressSlot (carried over)
- Task 7: minor (deferred): TranslationGrid.LanguageId throws when Context.ParentId null (page always passes it); using order; TRoot fully qualified in page; long summary line; reload test doesn't check rows/direction
- Plan 3 known gaps: child views do not apply AppliesWhen; Move still has no view.

## Final review triage (for Plan 4)
Final whole-branch review: ready to merge, no Critical. Open Plan 4 with these hardening commits before converting UserAccess:
- `GeneratedSetProperty` returns its errors with the status instead of the shared `Errors` property.
- `AggregateViewGuard.EnsureEditable` refuses an `Inline` Select/Switch command whose detail field is hidden or replaced by a `DetailFields` slot.
- Refuse `AppliesWhen` (and demand none) on child `IReverseOf` pairs until child views apply it.
- Refuse an `IOverviewQuery` on a `ListUnder` root (it would be sent without the parent id).
- Cover the Dialog edit mode end to end (headless-browser step or circuit test): open, save, stale keeps the dialog open, cancel.
- Later: a `ListUnder` root's detail view has no list to leave to after delete and no parent trail step.
Resolved since logged: IsRightToLeft duplication (views deleted), SK Localization docs, bulk-skip documentation.
