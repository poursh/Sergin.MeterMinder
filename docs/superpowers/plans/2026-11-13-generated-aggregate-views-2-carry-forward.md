# Generated aggregate views, Plan 2 — carry-forward for Plans 3-4

Rulings made during Plan 2 execution and findings deferred to later plans, copied from the execution ledger.

## Rulings
- Ruling: ChildViewModel.ListBodyType is added in Task 5 (with GeneratedChildListBody), not Task 3 — T3 cannot reference a T5 type — cost if wrong: one extra model property edit in T5.
- Ruling: FieldView.Label becomes TextDefinition (the label text; a FieldText's .Label, or the declared Label(...)); all callers switch from Label.Label to Label — cost if wrong: a later need for placeholder/help from FieldText re-adds an optional FieldText property.
- Ruling: SK work on branch generated-aggregate-views-2 created from d2a4325 in Task 1; each task commits SK first, then root (pointer + root files) — cost: none.
- Task 4: implementer DONE root 8cfa718. Ruling: list rows load after first render (prerender shows Loading), so row-level assertions (links, row icons, in-use tooltip) are pinned through the AggregateViewModel/ChildViewModel plus the Task 8 browser walk, not prerender HTML — applies to Tasks 4-5 — cost if wrong: a row render bug escapes tests until the browser check.
- Task 8: dispatched (sonnet, base root 31f5bfe / SK 458fe1f). Ruling: Task 8 implementer commits but does not push or open PRs; controller asks the user after the final review — cost: none.

## Deferred findings
- Task 1: minor (deferred): GeneratedListBody ArgumentsOf evaluates IdOf(item) eagerly (lazy Func<Guid> optional)
- Task 1: minor (deferred): RunRow/DeleteRow version-then-send duplicated in list body (2 paths); form RecordStep keeps own null-title logic — add a why-comment
- Task 1: minor (deferred): GeneratedViewSupport usings not alphabetical
- Task 2: minor (deferred): GeneratedDeviceViewTests ~L98 `of \d+` may match pager text; anchor to overview markup
- Task 2: minor (deferred): BadOverviewProbeTexts category enlarges shared test catalog
- Task 3: minor (deferred): Guard_ChildCommandOfAnotherRoot_Refuses asserts "ChildHost", substring of "BareChildHost" — assert FullName or "typed to"
- Task 3: minor (deferred): ChildViewModel.Commands doc says sorted by full name; order unverified — sort or fix doc
- Task 3: minor (deferred): child List Filter(...) silently ignored — refuse or document
- Task 3: minor (deferred): SK CLAUDE.md generated-views section not updated (Task 8 owns docs); note test-only typed requests on configured roots reach the view join (RecordScopeTests hazard)
- Task 3: minor (deferred): ChildViewRoutes.Any unused
- Task 4: minor (deferred): GeneratedManufacturerViewTests RowCommands test reads registry not page — rename ViewModel_…; ">Name<"/">Address<" loose matches; circuit-level row check left to Task 8 browser walk
- Task 5: minor (deferred): read-only row-icon hiding pinned via model only — Task 8 browser check
- Task 5: minor (deferred): childSections dict never pruned; reload on unmounted section ref possible (fixed section set, unlikely)
- Task 5: minor (deferred): bulk-visibility computation duplicated in SerginAggregateList/SerginEntityList; MultiSelection may flip false->true after first render
- Task 5: minor (deferred): read-only checkbox assertion is whole-page 'mud-checkbox' match
- Task 6: minor (deferred): DisabledWhen reason not shown on detail page actions (SerginPageAction has no DisabledReason) — follow-up
- Task 6: minor (deferred): Context rebuilt per render access
- Task 6: minor (deferred): Host.LeaveAsync after Sent not asserted (only LeaveTarget); FailedAsync may be dead if no other caller
- Task 7: minor (deferred): add/edit RequiresVersion via child.Commands lookup silently false when absent — expose on command view or throw (also AggregateViewModel.cs:120)
- Task 7: minor (deferred): AddDeviceModel render test weak (Contains "Name"/"<input"; Cancel href also in trail)
- Task 7: minor (deferred): view wiring (Host.LeaveAsync, reload after stale) untested beyond seam; LoadAsync per OnParametersSet may overwrite typed input; prefill silent when edit field has no detail field; page doc-comment grammar
