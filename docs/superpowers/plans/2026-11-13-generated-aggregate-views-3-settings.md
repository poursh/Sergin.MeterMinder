# Generated Aggregate Views, Plan 3 of 4: Settings — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the Settings module's Language and Translation pages onto SharedKernel's generated views. This adds what they need:
- enum value texts,
- the Dialog and Inline edit modes for a root's set-property commands,
- reverse-pair transitions (Publish / Unpublish),
- link actions,
- a list view under a parent record, whose body a slot can replace (the translation grid).

**Architecture:**
- **Value texts.** Enum value texts are declared in Application next to the field (`Category.Values<TEnum>(…)`). `TextSource` collects them, `FieldTextGuard` refuses a shown enum with a member that has no text, and every generated field, column and select reads them.
- **Set-property commands.** A root's set-property command becomes a `CommandView` with a value field and a build from (id, values). The detail view offers it in one of two ways:
  - in a dialog (`SerginSetPropertyDialog`), or
  - inline: a form card in the body for a text value, or the field itself made editable for a choice.

  Both use one shared value form, validated through `ISerginFormValidator`.
- **Reverse pairs.** A pair of `IReverseOf` transitions shows only the one that applies. The rule is declared once with `AppliesWhen<TRecord>(…)`.
- **Translations under a language.** Translation is a root whose list query is `IUnderParent<Language>`. `view.ListUnder<Language>(…)` gives it a list route that carries the parent id. The generated list view loads the parent for the trail and hands the parent to a `ListBody` slot, which holds the translation grid.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9.12.0, MediatR, FluentValidation, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-11-12-generated-aggregate-views-design.md`. **Read with it:** `docs/superpowers/plans/2026-11-13-generated-aggregate-views-2-carry-forward.md`. It holds Plan 2's rulings and deferred findings. This plan closes the three it names in Task 3; the rest stay deferred to Plan 4.

**Series:**
- Plan 1 (landed, #57 / SK #41): framework and Device.
- Plan 2 (landed, #58 / SK #42): Manufacturer and DeviceModel.
- Plan 3 (this): Settings.
- Plan 4: UserAccess (User and assignments, Role permission picker, Scope tree, Move), deleting `SerginScheduleAction`, docs and skills.

## Global Constraints

- `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer: every line passes analysis. No `@code` blocks; all C# lives in `.razor.cs`.
- Central Package Management: no `Version` on a `PackageReference`. No new packages.
- Every visible text is a declared `TextDefinition`/`FieldText`/enum value text.
  - UI-only texts live in the RCL (`LanguageUiTexts`, `TranslationUiTexts`, `ShellTexts`).
  - Field, value and error texts live in `.Application.Configurations`.
  - A new key gets a `fa` row in `docker-compose/.files/seed/dev-seed.sql` and a line in `tests/.../Commands/Types/catalog-keys.txt`. A deleted key loses both. A renamed key moves both.
  - `PseudoLocaleSweepTests` stays green.
- The view configuration builder has **no** method for permissions, scope, version rule or schedule name. Those are read from `CommandConfigurationRegistry`. A link action carries no permission: it is shown to whoever sees the page, and the target page refuses on its own.
- Every route stays exactly as it is: `/st/languages`, `/st/languages/new`, `/st/languages/{Id:guid}`, `/st/languages/{LanguageId:guid}/translations`.
- Reflection runs once, at registry build. Components read cached delegates.
- Inject `ISerginDispatcher`, never `ISender`.
  - Use `SendVersionedAsync` only for a `Versioned()` root's GetOne and for version-guarded writes.
  - A stale answer (`VersionErrors.IsStale`) shows the errors once and reloads.
- Enum value text keys are `<category>.values.<enum-kebab>.<member-kebab>`, for example `settings.languages.values.language-status.draft` (spec, "Enum values").
- Child entities stay Page-only for set-property. Dialog and Inline are for a root's own commands. Move stays unrendered until Plan 4.
- Branches, each named `generated-aggregate-views-3`, created in Task 1:
  - SharedKernel: in `src/SharedKernel`, from `f217148`, landing as a PR in `poursh/Sergin.SharedKernel`.
  - Settings: in `src/Modules/Administration/Settings`, from `a4717f3`, landing as a PR in `poursh/Sergin.Administration.Settings`.
  - Root: in the worktree `.claude/worktrees/gav3` (already created, submodules initialized).
- PR order: SK, then Settings, then root. After each merge, bump the pointers.
- Each task commits in this order: SK first, then Settings, then the root (pointers plus root files).
- Before pushing any branch or opening a PR, stop and ask the user.
- Commits use the user's git identity only. No `Co-Authored-By` trailer (CLAUDE.md overrides the harness default).
- Tests need Docker up: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter <filter>`. Run the full suite at the end of Tasks 1, 3, 5 and 8.

## Review Focus

1. **Two people on one language: one publishes while the other's page still shows Draft.** The second person's Publish is refused stale. One error shows, the page reloads, and it now offers Unpublish. Test in Task 6: `LanguageDetail_StalePublish_ReloadsAndOffersUnpublish`.
2. **An inline rename to a blank or too-long name.** The field shows the validator's message, Rename stays disabled, and nothing is sent. Test in Task 3: `InlineEdit_InvalidValue_SendsNothing`.
3. **A reader who holds `permission.st.languages.read` only.**
   - Direction is plain text, with no rename card and no Publish, Unpublish or Delete.
   - The Translations link is still there.
   - `SettingsPageRenderingTests.LanguageDetail_ForAReader_HidesTheManageControls` passes with its assertions unchanged.
4. **The translations page for a language id that does not exist.** The trail keeps the placeholder step linking to `/st/languages/{id}`, the grid reports not-found once, and nothing throws. Test in Task 7: `TranslationList_UnknownLanguage_KeepsPlaceholderTrail`.
5. **An enum member added later with no value text.** Host start is refused naming `Enum.Member`. A raw member name never reaches the page. Tests:
   - Task 1: `FieldTextGuard_EnumMemberWithoutValueText_Refuses`.
   - Task 2: `Guard_DetailEnumFieldWithoutValueText_Refuses`.

---

## File Structure

SharedKernel:

| File | Responsibility |
|---|---|
| `Application/Localizations/EnumTexts.cs` (new), `TextCategory.cs` | `Category.Values<TEnum>(…)` gives an `EnumTexts<TEnum>` (`For(member)`, `All`) |
| `Application/Localizations/TextSource.cs`, `TextCatalog.cs` | collect `EnumTexts<>` members; `FindValue(category, enumType, member)` |
| `Hosts/Localizations/FieldTextGuard.cs` | a shown enum property needs a value text for every member |
| `Presentation.Blazor/Shell/ShellTexts.cs` | `ShellTexts.Values.Yes` / `No` (`common.values.yes`/`no`), the default bool texts |
| `Aggregates/Model/FieldView.cs`, `FormFieldView.cs` | `ValueTexts` for enum and bool values |
| `Aggregates/Configuration/FieldSetBuilder.cs` | `BoolTexts(whenTrue, whenFalse)` |
| `Aggregates/Model/CommandView.cs`, `Configuration/CommandViewBuilder.cs` | `ValueField`, `BuildFromValues`, `AppliesWhen`, `Kind(...)` |
| `Aggregates/Configuration/AggregateViewBuilder.cs`, `Model/ViewLinkView.cs` (new) | `Link(...)`, `ListUnder<TParent>(...)` |
| `Aggregates/Model/AggregateViewRoutes.cs`, `AggregateViewModel.cs`, `AggregateViewContext.cs`, `SerginViewSlot.cs` | parent list route, `Parent` model, `Links`; context gains `ParentId`, `Parent`, `Generation`; slot `ListBody` |
| `AggregateViewModelJoin.cs`, `AggregateViewGuard.cs` | the joins and the new refusals |
| `Aggregates/Runtime/GeneratedSetProperty.cs` (new) | the set-property outcome seam (send at version, stale, reload) |
| `Aggregates/Views/Parts/GeneratedValueForm.razor{,.cs}` (new) | one command value in a `MudForm`, validated through `RulesFor` |
| `Aggregates/Views/Parts/GeneratedInlineEditCard.razor{,.cs}` (new), `GeneratedField.razor{,.cs}` | inline text card; inline choice field |
| `Aggregates/Views/SerginSetPropertyDialog.razor{,.cs}` (new) | the Dialog edit mode |
| `Aggregates/Views/SerginGeneratedDetailView.*`, `SerginGeneratedListView.*`, `Parts/GeneratedListBody.*`, `Parts/GeneratedCommandActions.cs`, `Parts/GeneratedInput.*`, `Parts/FieldValueText.cs` | wiring: set-property actions, reverse pairs, links, parent trail, list body slot, hints, value texts |

Settings (`src/Modules/Administration/Settings/`):
- **Application.Configurations:** `LanguageTexts` gains `DirectionValues` and `StatusValues`. `TranslationTexts` gains `StatusValues` and `OriginValues`.
- **Application.Contracts:** `GetLanguageProgressQueryCommand` becomes `IEntityOverviewQuery<Language, LanguageId>`.
- **RCL, new:**
  - `Languages/LanguageViewConfiguration.cs`
  - `Languages/Components/LanguageProgressSlot.razor{,.cs}`
  - `Translations/TranslationViewConfiguration.cs`
  - `Translations/Components/TranslationGrid.razor{,.cs}`
- **RCL, deleted:**
  - `Languages/Components/Language{Create,Detail,List}View.*`
  - `Languages/Models/*`
  - `Languages/LanguageLabels.cs`
  - `Translations/Components/TranslationListView.*`
- **RCL, changed:** the four pages host generated views.

Tests (`tests/Sergin.MeterMinder.IntegrationTests.All/`):
- New: `Aggregates/GeneratedLanguageViewTests.cs`, `Aggregates/GeneratedTranslationViewTests.cs`.
- Extended:
  - `Aggregates/AggregateViewTestTypes.cs`: probe set-property commands, a reverse pair and a parented probe root.
  - `AggregateViewRegistryTests`, `AggregateViewGuardTests`, `RequestRuntimeTests`.
  - `Localization/FieldTextGuardTests`, `TextCatalogTests`, `SettingsPageRenderingTests`.
  - `Commands/Types/catalog-keys.txt`.

---

### Task 1: Enum value texts

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Localizations/EnumTexts.cs`
- Modify:
  - SK: `Localizations/TextCategory.cs`, `TextSource.cs`, `TextCatalog.cs`, `Hosts/Localizations/FieldTextGuard.cs`
  - Settings: `LanguageTexts.cs`, `TranslationTexts.cs`, `LanguageUiTexts.cs`, `TranslationUiTexts.cs`, `LanguageLabels.cs`, `TranslationListView.razor.cs`
  - Root: `dev-seed.sql`, `catalog-keys.txt`
- Test: `Localization/TextCatalogTests.cs`, `Localization/FieldTextGuardTests.cs`, `Localization/TextDeclarationTests.cs`

**Interfaces:**
- Produces `public sealed class EnumTexts<TEnum> where TEnum : struct, Enum`:
  - `TextDefinition For(TEnum value)` throws `ArgumentOutOfRangeException` for an undeclared member.
  - `IReadOnlyDictionary<TEnum, TextDefinition> All`.
- Produces `public EnumTexts<TEnum> Values<TEnum>(params (TEnum Value, string Default, string? Description)[] values)` on `TextCategory`:
  - Keys are `<category>.values.<enum-kebab>.<member-kebab>`.
  - A member declared twice throws `ArgumentException`.
  - Partial declarations are allowed; the guard refuses the gap.
- `TextSource` collects every public static `EnumTexts<>` member like a `TextDefinition`, with the source's origin.
- Produces `TextCatalog.FindValue(TextCategory category, Type enumType, object member) -> TextDefinition?`, and `ValueTextsOf(category, enumType) -> IReadOnlyDictionary<object, TextDefinition>`.
- `FieldTextGuard`: on a configured request, a shown property whose type is an enum, or a nullable one, needs a value text in its category for every member. The refusal names each `Type.Property (Enum.Member)`.
- Settings declares, with today's English defaults and descriptions moved over:
  - `LanguageTexts.DirectionValues` (`FromCulture` "From the culture", `LeftToRight`, `RightToLeft`)
  - `LanguageTexts.StatusValues` (`Draft`, `Published`)
  - `TranslationTexts.StatusValues` (the four statuses)
  - `TranslationTexts.OriginValues` (`TextOrigin.Ui` "Screens", `TextOrigin.Application` "Messages")
- Settings deletes the old texts:
  - `LanguageUiTexts.FromCulture`, `LeftToRight`, `RightToLeft`, `Draft`, `Published`
  - `TranslationUiTexts.Untranslated`, `Translated`, `NeedsReview`, `Orphaned`, `Screens`, `Messages`
- `LanguageLabels` and `TranslationListView` read the new value texts until Tasks 6 and 7 delete them. `LanguageUiTexts.DirectionResolved` stays (Task 6 keeps it).

- [ ] **Step 1: Write the failing tests.**
  - `TextCatalogTests.Values_KeysAreCategoryValuesEnumMember`: `LanguageTexts.StatusValues.For(LanguageStatus.Draft).Key == "settings.languages.values.language-status.draft"`, and the catalog holds it with origin `Application`.
  - `TextCatalogTests.Values_DuplicateMember_Throws`.
  - `FieldTextGuardTests.FieldTextGuard_EnumMemberWithoutValueText_Refuses`: a test-only configured request with an enum property whose category declares one of two members. The message contains `"(ProbeEnum.Second)"`.
  - `FieldTextGuardTests.FieldTextGuard_NullableEnumIsChecked_AndRealHostPasses`.
- [ ] **Step 2: Run them.** Expected: FAIL, because `Values` is not defined.
- [ ] **Step 3: Implement the SharedKernel half,** then the Settings declarations and the switched readers.
- [ ] **Step 4: Move the seed and catalog keys.**
  - In `dev-seed.sql`, rename the 12 `fa` rows (old keys `settings.languages.directions.{from-culture,left-to-right,right-to-left}`, `settings.languages.statuses.*`, `settings.translations.{statuses,origins}.*`) to the new keys, keeping their text.
  - Update `catalog-keys.txt` to match and keep it sorted.
- [ ] **Step 5: Build and run the full suite.** Expected: 0 warnings; every test that passed on `main` passes.
- [ ] **Step 6: Commit.** SK `"Enum value texts"`, Settings `"Enum value texts for language and translation"`, root `"Enum value texts: seed, catalog keys, pointers"`.

---

### Task 2: Enum and bool fields, hints in generated inputs

**Files:**
- Modify:
  - SK: `Model/FieldView.cs`, `Model/FormFieldView.cs`, `Configuration/FieldSetBuilder.cs`, `AggregateViewModelJoin.cs`, `AggregateViewGuard.cs`, `Views/Parts/FieldValueText.cs`, `Views/Parts/GeneratedField.razor.cs`, `Views/Parts/GeneratedListBody.razor`, `Views/Parts/GeneratedInput.razor{,.cs}`, `Shell/ShellTexts.cs`
  - Root: `dev-seed.sql`, `catalog-keys.txt`
- Test: `Aggregates/AggregateViewTestTypes.cs`, `AggregateViewRegistryTests.cs`, `AggregateViewGuardTests.cs`, `GeneratedViewProbeTests.cs`

**Interfaces:**
- `FieldView` gains `IReadOnlyDictionary<object, TextDefinition>? ValueTexts = null`. It is filled at join for an enum property (from `TextCatalog.ValueTextsOf`) and for a bool property (from `BoolTexts`, else `ShellTexts.Values.Yes`/`No`).
- `FormFieldView` gains `IReadOnlyDictionary<object, TextDefinition>? ValueTexts = null`. Select items show it.
- `FieldSetBuilder<TRecord>.Field(x).BoolTexts(TextDefinition whenTrue, TextDefinition whenFalse)`. A `Field(x)` with a declared `Label(...)` is a field even when the category has no `FieldText` of that name.
- `FieldValueText.Render` shows the value text when `ValueTexts` holds the value. A null value stays null.
- `GeneratedInput` shows the field's `FieldText.Hint` as `HelperText` on every input kind.
- `ShellTexts.Values.Yes` (`common.values.yes`, "Yes") and `ShellTexts.Values.No` (`common.values.no`, "No") are new keys.
- Guard: a shown detail or list field of enum type with a member that has no value text is refused, naming the configuration, `Record.Property` and `Enum.Member`.

- [ ] **Step 1: Write the failing tests** over a probe enum on the `ViewProbe` GetOne response and list item.
  - `ViewModel_EnumField_HasValueTexts` and `ViewModel_BoolField_DefaultsToYesNo_OrDeclaredTexts`.
  - `GeneratedViewProbeTests.Detail_RendersEnumValueText_NotMemberName`: the prerendered detail contains the value text's English and not the member name.
  - `GeneratedViewProbeTests.Form_ShowsFieldHint`.
  - `AggregateViewGuardTests.Guard_DetailEnumFieldWithoutValueText_Refuses`: the message names `"ProbeEnum.Second"`.
- [ ] **Step 2: Run them.** Expected: FAIL.
- [ ] **Step 3: Implement.** Add the two `fa` rows ("بله", "خیر") and the catalog keys.
- [ ] **Step 4: Run the `Aggregates`, `Shell` and `Localization` filters.** Expected: PASS.
- [ ] **Step 5: Commit** SK, then the root.

---

### Task 3: A root's set-property commands: Dialog and Inline

**Files:**
- Create: `Runtime/GeneratedSetProperty.cs`, `Views/Parts/GeneratedValueForm.razor{,.cs}`, `Views/Parts/GeneratedInlineEditCard.razor{,.cs}`, `Views/SerginSetPropertyDialog.razor{,.cs}`
- Modify: `Model/CommandView.cs`, `AggregateViewModelJoin.cs`, `AggregateViewGuard.cs`, `Model/AggregateViewModel.cs`, `Views/SerginGeneratedDetailView.razor{,.cs}`, `Views/Parts/GeneratedField.razor{,.cs}`, `Views/Parts/GeneratedCommandActions.cs`, `Views/Parts/GeneratedViewSupport.cs`, `Model/ChildViewModel.cs` (doc only)
- Test: `AggregateViewTestTypes.cs`, `AggregateViewRegistryTests.cs`, `AggregateViewGuardTests.cs`, `RequestRuntimeTests.cs`, `GeneratedViewProbeTests.cs`

**Interfaces:**
- `CommandView` gains `FormFieldView? ValueField = null` and `Func<Guid, IReadOnlyDictionary<string, object?>, object>? BuildFromValues = null`. Both are set for a root `SetProperty` command. Its constructor is `(Id, value)`: exactly one shown property besides `Id`.
- A set-property command's default label is its value field's label. A declared `Label(...)` (a verb such as `CommonTexts.Rename`) replaces it. A transition still needs a declared label.
- Edit modes on a root `SetProperty` (child rules unchanged; a `Hidden()` command is not checked):
  - `Dialog` (default): a Secondary action on the detail bar opens `SerginSetPropertyDialog`, which holds the value form, prefilled from the record's property of the same name when there is one.
  - `Inline` with a Select or Switch input: `GeneratedField` renders that detail field as the editable input for a user who holds the command's permission (`IScopeAuthorization`, `AnyScope` as configured). Anyone else gets the plain value. A change sends at once.
  - `Inline` with any other input: `GeneratedInlineEditCard` renders the value form in an outlined card, below the fields and before the entities slot. It is visible only to a user who holds the permission. The submit button is labelled by the command.
- Guard refusals, each naming the configuration and the command:
  - A root set-property command whose constructor is not `(Id, value)`.
  - `Page` on a root set-property command (no case yet).
  - `Inline` when the GetOne response has no property named after the value.
- Produces `internal sealed class GeneratedSetProperty`: `Task<SetPropertyStatus> SubmitAsync(ISerginDispatcher, CommandView, Guid id, object? value, RowVersion? version)`.
  - It sends versioned when `command.RequiresVersion`, else plainly.
  - `SetPropertyStatus { Sent, Stale, Failed }`, plus the errors.
  - The view notifies every error and reloads after each outcome. `Stale` and `Failed` keep the dialog open with the user's value.
- `GeneratedValueForm` (parameters `Command`, `Value`, `OnSubmit`, `Busy`): a `MudForm` with one `GeneratedInput` whose validation is `FormValidator.RulesFor(() => command.BuildFromValues(id, values))`. Submit runs `ValidateAsync` first and sends nothing while invalid.
- Carry-forward fixes:
  - `AggregateViewModel.SubmitFormAsync` reads the update's `CommandView.RequiresVersion` directly (a missing one throws instead of answering false). `GeneratedChildForm`'s add/edit lookup does the same.
  - The usings in `GeneratedViewSupport` are sorted.
  - `ChildViewModel.Commands`' doc matches its real order.

- [ ] **Step 1: Write the failing tests.** Add to `AggregateViewTestTypes` three set-property commands on the probe root:
  - `RenameProbeCommand(Id, Name)`, declared `Inline`, label `CommonTexts.Rename`;
  - `SetProbeColorCommand(Id, Color)`, an enum, `Inline`;
  - `RetitleProbeCommand(Id, Title)`, with no declaration (Dialog).

  Then the tests:
  - `ViewModel_SetProperty_HasValueFieldAndBuild`: `RequestRuntimeTests` builds `RenameProbeCommand` from `(id, {Name="x"})`.
  - `ViewModel_SetPropertyLabel_DefaultsToFieldLabel`.
  - `Guard_RootSetPropertyNotIdAndOneValue_Refuses`, `Guard_RootSetPropertyPage_Refuses`, `Guard_InlineWithoutResponseProperty_Refuses`.
  - `GeneratedSetProperty_Stale_ReportsStaleAndSendsOnce`: a version read, then the record changed, then a submit at the old version, which answers `Stale` with one send.
  - `InlineEdit_InvalidValue_SendsNothing`: a blank `Name` through `GeneratedValueForm`'s validation gives the validator's message and no send.
  - `Detail_InlineCardAndChoice_ShownToManager_HiddenFromReader`: prerendered for the dev user and for a read-only seeded user.
  - `Detail_DialogCommand_IsABarAction`.
- [ ] **Step 2: Run them.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Build and run the full suite.** Expected: everything that passed on `main` passes.
- [ ] **Step 5: Commit** SK `"Generated views: set-property in Dialog and Inline modes"`, then the root.

---

### Task 4: Reverse pairs, action kind and link actions

**Files:**
- Create: `Model/ViewLinkView.cs`
- Modify: `Configuration/CommandViewBuilder.cs`, `Configuration/AggregateViewBuilder.cs`, `Model/CommandView.cs`, `Model/AggregateViewModel.cs`, `AggregateViewModelJoin.cs`, `AggregateViewGuard.cs`, `Views/Parts/GeneratedCommandActions.cs`, `Views/SerginGeneratedDetailView.razor.cs`, `Views/Parts/GeneratedListBody.razor{,.cs}`
- Test: `AggregateViewTestTypes.cs`, `AggregateViewRegistryTests.cs`, `AggregateViewGuardTests.cs`, `GeneratedViewProbeTests.cs`

**Interfaces:**
- `CommandViewBuilder<TCommand>.AppliesWhen<TRecord>(Func<TRecord, bool> when)`: on one command of an `IReverseOf<>` pair.
  - Its partner applies exactly when it does not.
  - `CommandView` gains `Func<object, bool>? AppliesWhen = null`. The declared command gets the predicate and its partner the negation.
  - A view whose record is not a `TRecord` (a list row) offers neither command of the pair.
- `CommandViewBuilder<TCommand>.Kind(SerginActionKind kind)` replaces the role's default kind.
- `AggregateViewBuilder<TRoot>.Link(TextDefinition label, string icon, Func<Guid, string> href)` gives one `ViewLinkView(Label, Icon, Href)` in `AggregateViewModel.Links`.
  - It shows as a Secondary action on the detail bar, after the commands that are not Danger.
  - It shows as a row icon on the list, before the row's commands.
  - It carries no permission.
- Guard refusals, each naming the configuration:
  - An `IReverseOf` pair with `AppliesWhen` on neither command, or on both.
  - An `AppliesWhen<TRecord>` that names no record of the command's views (the same check as `DisabledWhen`).
  - A link whose `href(Guid)` matches no `[Route]` (added to `Ensure`, sampled with a fixed GUID like the child routes).

- [ ] **Step 1: Write the failing tests.** Add a probe `PublishProbeCommand` and `UnpublishProbeCommand : IReverseOf<PublishProbeCommand>`, and a probe link. Then the tests:
  - `ViewModel_ReversePair_PartnerIsNegation`.
  - `Detail_ReversePair_ShowsOnlyTheOneThatApplies`: render for a draft probe, then a published one.
  - `List_ReversePair_NotOffered`.
  - `Detail_Link_IsSecondaryActionWithHref`, `List_Link_IsRowIcon`.
  - `Guard_ReversePairWithoutAppliesWhen_Refuses`, `Guard_ReversePairAppliesWhenOnBoth_Refuses`, `Guard_LinkToUnknownRoute_Refuses`.
- [ ] **Step 2: Run them.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the `Aggregates` filter.** Expected: PASS.
- [ ] **Step 5: Commit** SK, then the root.

---

### Task 5: A list under a parent, and the list body slot

**Files:**
- Modify: `Configuration/AggregateViewBuilder.cs`, `Model/AggregateViewRoutes.cs`, `Model/AggregateViewModel.cs`, `Model/SerginViewSlot.cs`, `AggregateViewContext.cs`, `AggregateViewModelJoin.cs`, `AggregateViewGuard.cs`, `Views/SerginGeneratedListView.razor{,.cs}`, `Views/Parts/GeneratedViewSupport.cs`
- Test: `AggregateViewTestTypes.cs` (a parented probe root `ViewProbeNote` whose list query is `IUnderParent<ViewProbe, …>`, plus its view configuration and page), `AggregateViewRegistryTests.cs`, `AggregateViewGuardTests.cs`, `GeneratedViewProbeTests.cs`

**Interfaces:**
- `AggregateViewBuilder<TRoot>.ListUnder<TParent>(Func<Guid, string> list)` declares the list route of a root whose list query is `IUnderParent<TParent, …>`. Its detail, create and edit routes are optional (`Routes(...)` may be left out).
- `AggregateViewRoutes`:
  - `List` becomes `string?`.
  - It gains `Func<Guid, string>? ListUnderParent = null`.
  - Existing callers handle a null `List`; `ListStep` returns null for it.
- `AggregateViewModel` gains `Type? Parent = null`, the parent root, whose own model gives the trail.
- `SerginViewSlot.ListBody` replaces the generated table. The slot component receives `Context`.
- `AggregateViewContext` gains three members:
  - `Guid? ParentId = null`;
  - `object? Parent = null`: the parent's GetOne response, or null when its load failed;
  - `int Generation = 0`: bumped by the header's Reload, so a slot can reload its own list when it changes.
- `SerginGeneratedListView<TRoot>` gains `[Parameter] public Guid? ParentId { get; set; }`. For a parented root:
  - The view loads the parent through the parent model's `LoadRecordAsync`.
  - The trail is the parent's list step, then the parent record (`SerginBreadcrumb.Data(display, parent detail href)`, or the parent's `Singular` linking to the same href while it is not loaded), then this root's `Title`.
  - Every list query it sends carries `ParentId`.
- Guard refusals, each naming the configuration:
  - A configured root whose list query is `IUnderParent` with no `ListUnder` (or with `Routes(list: …)` instead).
  - A `ListUnder<TParent>` whose `TParent` is not the list query's parent.
  - A parent with no view configuration or no GetOne query.
  - `ListUnder` on a root whose list query has no parent.

- [ ] **Step 1: Write the failing tests.**
  - `ViewModel_ListUnder_HasParentAndRoute`.
  - `List_UnderParent_TrailHasParentRecordStep`.
  - `List_UnderParent_UnknownParent_KeepsPlaceholderStep`: the step text is the parent's `Singular`, with href `/…/{id}`.
  - `List_BodySlot_ReplacesTableAndGetsParent`: the probe slot prints `Context.ParentId` and the parent's display.
  - `Guard_ParentedListWithoutListUnder_Refuses`, `Guard_ListUnderWrongParent_Refuses`, `Guard_ListUnderWithoutParentQuery_Refuses`.
- [ ] **Step 2: Run them.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Build and run the full suite.** Expected: green.
- [ ] **Step 5: Commit** SK `"Generated views: a list under a parent, list body slot"`, then the root.

---

### Task 6: Language on generated views

**Files:**
- Create (RCL): `Languages/LanguageViewConfiguration.cs`, `Languages/Components/LanguageProgressSlot.razor{,.cs}`
- Modify:
  - Settings: `Application.Contracts/Translations/Commands/GetProgress/GetLanguageProgressQueryCommand.cs`, `Languages/Pages/LanguageListPage.razor`, `LanguageCreatePage.razor`, `LanguageDetailPage.razor`, `Languages/LanguageUiTexts.cs`
  - Root: `dev-seed.sql`, `catalog-keys.txt`
- Delete: `Languages/Components/Language{Create,Detail,List}View.*`, `Languages/Models/*`, `Languages/LanguageLabels.cs`
- Test: `Aggregates/GeneratedLanguageViewTests.cs` (new), `Localization/SettingsPageRenderingTests.cs`

**Interfaces:**
- `GetLanguageProgressQueryCommand` is retyped to `IEntityOverviewQuery<Language, LanguageId>`. Its permission and scope are unchanged, since the root is the same.
- `LanguageViewConfiguration : IAggregateViewConfiguration<Language>` declares:
  - `Title(LanguageUiTexts.Title)`
  - `Routes("/st/languages", id => $"/st/languages/{id}", create: "/st/languages/new")`
  - `Command<AddLanguageCommand>().Label(LanguageUiTexts.New)`
  - `Form<AddLanguageCommand>(form => form.Title(LanguageUiTexts.New))`
  - `Command<PublishLanguageCommand>().Label(LanguageUiTexts.Publish).Icon(Icons.Material.Outlined.Publish).Kind(SerginActionKind.Primary).AppliesWhen<LanguageQueryResponse>(l => l.Status == LanguageStatus.Draft)`
  - `Command<UnpublishLanguageCommand>().Label(LanguageUiTexts.Unpublish).Icon(Icons.Material.Outlined.Unpublished)`
  - `Command<RenameLanguageCommand>().Label(CommonTexts.Rename).Edit(SerginEditMode.Inline)`
  - `Command<SetLanguageDirectionCommand>().Edit(SerginEditMode.Inline)`
  - `Command<DeleteLanguageCommand>().ConfirmDelete(LanguageUiTexts.DeleteTitle, LanguageUiTexts.DeleteConfirm)`
  - `Link(LanguageUiTexts.Translations, Icons.Material.Outlined.Translate, id => $"/st/languages/{id}/translations")`
  - `Detail<LanguageQueryResponse>(d => d.Title(x => x.DisplayName).Field(x => x.Direction).Component<LanguageDirectionValue>())`. `LanguageDirectionValue` (new RCL component in `Languages/Components/`, `[Parameter] Context`, `[Parameter] Value`) renders `LanguageUiTexts.DirectionResolved` ("From the culture (Left to right)") for `FromCulture`, else the direction's value text. A manager sees the inline select instead (its `aria-label` is the field label, "Direction"); `IsRightToLeft` stays hidden on the detail.
  - `List<GetLanguageListItem>(l => l.Title(x => x.Culture).SearchPlaceholder(LanguageUiTexts.SearchPlaceholder).Field(x => x.IsRightToLeft).Label(LanguageTexts.Direction.Label).BoolTexts(same two))`
  - `Slot<LanguageProgressSlot>(SerginViewSlot.DetailOverview)`

  The builder method names are Plan 1/2's where they exist. Use `FieldSetBuilder`'s title-field method as named in the code.
- `LanguageProgressSlot` renders `SerginOverviewCards` with one `SerginOverviewCard`: "Translated", the percent rounded down, the caption "{Translated} of {Total} translated". All three come from `Context.Overview` as `LanguageProgressQueryResponse`, and the value is "–" when it is null.
- `LanguageUiTexts` changes:
  - Keeps `DirectionResolved` (read by `LanguageDirectionValue`); gains no key.
  - `New`, `SearchPlaceholder`, `Publish`, `Unpublish`, `DeleteTitle`, `DeleteConfirm`, `Translations`, `OverviewTranslated` and `OverviewPercent` stay.
- Each page is `@page` + `@layout SerginPageLayout` + `<SerginGenerated{List,Form,Detail}View TRoot="Language" … />`, with the routes unchanged.

- [ ] **Step 1: Write the failing tests** in `GeneratedLanguageViewTests` (through `ISerginDispatcher` from a scope, and prerendered pages):
  - `LanguageDetail_Draft_OffersPublishNotUnpublish`.
  - `LanguageDetail_Published_OffersUnpublishNotPublish`.
  - `LanguageDetail_StalePublish_ReloadsAndOffersUnpublish`: read the version, then publish through a second scope. Send `PublishLanguageCommand` at the old version with `VersionedDispatch` and expect `VersionErrors.Stale`. The prerendered detail then offers Unpublish and not Publish.
  - `LanguageDetail_InlineRename_Stale_ReloadsKeepsCurrentName`.
  - `LanguageDetail_DirectionSelect_ForManager`: the prerender holds a select labelled "Direction".
  - `LanguageList_DirectionColumnShowsValueText`.
  - `LanguageDetail_TranslationsLink`: an action with href `/st/languages/{id}/translations`, visible to a reader too.
- [ ] **Step 2: Run them.** Expected: FAIL.
- [ ] **Step 3: Implement,** then delete the old views, form models and `LanguageLabels`.
- [ ] **Step 4: Run `SettingsPageRenderingTests`, `BreadcrumbRenderingTests`, `PseudoLocaleSweepTests`, `ModulePageRenderingTests` and the new tests.** Expected: PASS. Assertions in the existing tests may change only where the generated markup differs in shape (attribute, wrapper), not in what is shown. Write down any such change in the report.
- [ ] **Step 5: Update the seed and `catalog-keys.txt`** for any key this task deletes (none is added).
- [ ] **Step 6: Commit.** Settings `"Languages on generated views"`, root `"Languages on generated views: tests, seed, pointer"`.

---

### Task 7: Translation grid in the list body slot

**Files:**
- Create (RCL): `Translations/TranslationViewConfiguration.cs`, `Translations/Components/TranslationGrid.razor{,.cs}`
- Modify (RCL): `Translations/Pages/TranslationListPage.razor`, `Translations/TranslationUiTexts.cs` (only if a text becomes unused)
- Delete (RCL): `Translations/Components/TranslationListView.*`
- Test: `Aggregates/GeneratedTranslationViewTests.cs` (new), `Localization/SettingsPageRenderingTests.cs`, `Shell/BreadcrumbRenderingTests.cs`

**Interfaces:**
- `TranslationViewConfiguration : IAggregateViewConfiguration<Translation>` declares:
  - `Title(TranslationUiTexts.Title)`
  - `ListUnder<Language>(languageId => $"/st/languages/{languageId}/translations")`
  - `Command<AddTranslationCommand>().Hidden()`, `Command<ChangeTranslationCommand>().Hidden()`, `Command<DeleteTranslationCommand>().Hidden()`: the grid sends them.
  - `Slot<TranslationGrid>(SerginViewSlot.ListBody)`
- `TranslationGrid` (`[Parameter] AggregateViewContext Context`) keeps every behaviour of today's `TranslationListView` body:
  - module, category, status and origin filters with chips;
  - search;
  - 25 rows;
  - edit in place, with add, change at version, the stale reload with "changed meanwhile", the switch from add to change, and delete of an orphan.

  What changes:
  - The language comes from `Context.Parent` as `LanguageQueryResponse` (its `IsRightToLeft` sets the text direction), and the id comes from `Context.ParentId`.
  - It reloads its list when `Context.Generation` changes.
  - Status and origin texts are `TranslationTexts.StatusValues`/`OriginValues`.
  - It renders no trail, header or page title.
- The page is `@page "/st/languages/{LanguageId:guid}/translations"` + `@layout SerginPageLayout` + `<SerginGeneratedListView TRoot="Translation" ParentId="LanguageId" />`.

- [ ] **Step 1: Write the failing tests.**
  - `TranslationList_UnknownLanguage_KeepsPlaceholderTrail`: the strip holds "Language" linking to `/st/languages/{id}` and "Translations" disabled, and the page renders.
  - `TranslationList_Reload_ReloadsGrid`: the grid's `Generation` changes on reload.
  - `SettingsPageRenderingTests.TranslationList_RendersColumnsFilterToggleAndTrail` must keep passing with its assertions unchanged.
- [ ] **Step 2: Run them.** Expected: the new tests fail.
- [ ] **Step 3: Implement,** then delete `TranslationListView`.
- [ ] **Step 4: Run the `Localization`, `Shell` and `Aggregates` filters.** Expected: PASS.
- [ ] **Step 5: Commit.** Settings `"Translations on a generated list with the grid slot"`, then the root.

---

### Task 8: Docs, browser check, carry-forward, PRs

**Files:**
- Modify:
  - `src/SharedKernel/.claude/CLAUDE.md` (the "Generated aggregate views" section)
  - `src/Modules/Administration/Settings/.claude/CLAUDE.md`
  - `.claude/CLAUDE.md` (the generated-pages bullet)
  - `.claude/skills/add-feature/SKILL.md`
- Create: `docs/superpowers/plans/2026-11-13-generated-aggregate-views-3-carry-forward.md`

- [ ] **Step 1: Update the docs.**
  - Value texts: `Values<TEnum>`, the keys, and the guard.
  - Bool texts.
  - Edit modes: Dialog and Inline for a root's set-property, child Page only, and the default label.
  - `AppliesWhen` and `Kind`.
  - `Link`.
  - `ListUnder` and the `ListBody` slot.
  - Remove the "Deferred" items this plan built; Move stays deferred.
  - Settings: its pages are generated, and the grid is a slot.
- [ ] **Step 2: Run the full suite.** Expected: everything passes (3 TickerQ skips).
- [ ] **Step 3: Run a headless browser walk** (Playwright, as in `headless-browser-checks` memory) against `/st/languages`:
  - list search and sort, and a row delete;
  - create a language;
  - detail Publish, then Unpublish;
  - inline rename, including a blank value refused;
  - a direction change;
  - the Translations link;
  - on the grid: filter by module and status, edit a row and save, cancel an edit, and the reload button;
  - as `south.viewer` (read only): no manage controls.

  Record the outcomes in the report.
- [ ] **Step 4: Write the carry-forward** for Plan 4: rulings and deferred findings from the ledger.
- [ ] **Step 5: Commit** each repo. **Stop and ask the user** before pushing or opening the three PRs (SK, Settings, root).

---

## Decisions to check

- **Text keys move.** The status, direction and origin texts move to `<category>.values.<enum>.<member>` keys, and the seed's `fa` rows move with them. A deployed database's existing translations of the old keys become orphaned (listed, deletable, never shown).
- **The resolved direction.** A reader still sees "From the culture (Left to right)". A manager's Direction select loses the helper line that named the resolved direction.
- **The progress query is retyped.** `GetLanguageProgressQueryCommand` changes from `IEntityViewQuery` to `IEntityOverviewQuery`, since it is the record's overview figure.
- **A set-property command's default label is its field's label.** Rename declares the verb.
- **The language list has no Publish or Unpublish on rows,** as today: a reverse pair is not offered where the row cannot say which applies.
- **Link actions carry no permission.** Anyone who sees the language page holds `languages.read`, which the translations list also requires.
