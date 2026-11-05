# Localization — design

Date: 2026-11-05. Source: brainstorming session with the product owner.

## Problem

Every text a person sees is written in English, inline, in code: page titles, breadcrumb steps, nav entries, action
labels, field labels, hints, confirm dialogs, error titles and details, validation messages. `ILocalizer` exists and
the shell already routes many of these strings through it, but its only implementation, `DefaultLocalizer`, answers
the key itself — and the "key" at most call sites is the English text (`new SerginBreadcrumb("New device")`). There
is no second language, no way to translate, and no right-to-left layout.

The product needs other languages — Persian first, which brings right-to-left layout and the Jalali calendar — and
the translations must be maintained by administrators in the running system, without a redeploy.

## Intent

- Every user-visible text has a **key**, declared once in code next to its **default English text**. Code refers to
  texts by declared key, never by a string literal.
- Keys are organised by **module**, then an optional **category** (usually the aggregate, nested for a child entity),
  so the full list is browsable.
- Form fields are handled **systematically**: one declaration per field yields its label, hint and placeholder keys,
  and the same declaration names the field in validation messages, list columns, filter chips and detail captions.
- Administrators **add languages** and **edit translations** in the running system. A change shows without restart.
- A person picks a language from a **switcher** in the app bar. Persian switches the page to right-to-left and shows
  dates in the **Jalali calendar with Persian digits**.

## Goals

1. A key type, a declaration shape for texts and fields, and a startup catalog of every declared text, grouped by
   module and category.
2. Lookup through the built-in `IStringLocalizer`, backed by translations stored in the database, falling back to the
   default English text — never to a raw key.
3. Culture selection through the built-in request-localization middleware and culture cookie, with a switcher.
4. Right-to-left layout for right-to-left languages.
5. Jalali dates and Persian digits for Persian, in display and in date input.
6. A `Platform` module (its own repository) whose first feature is Localization: languages and translations as aggregates, with admin pages.
7. Every existing hardcoded text in SharedKernel, DeviceManagement and UserAccess moved to declared keys, with a
   startup guard that refuses a host whose commands have unlabelled fields.

All three stages — mechanism, module, conversion — ship as one delivery (product owner's decision).

## Non-goals

- **Shipping Persian translations.** This delivery builds the means to add languages and translate; no language and
  no translation is seeded. Persian is added and translated by administrators afterwards (product owner's decision).
  FluentValidation's bundled Persian messages are not imported either — its texts become keys with English defaults
  like every other text. Done is tested with a test culture, not a real Persian page.

- Translating **user data**: manufacturer names, device ids, scope names, role names. These are shown as entered.
- Translating **logs, exception messages and developer diagnostics**.
- **Per-user language saved in a profile.** The cookie set by the switcher is the only store for now.
- **Import/export files** for outside translators, and **machine translation**.
- **Keycloak's own pages** (login, account). Keycloak has its own theme localization; aligning it is separate work.
- Live switch without reload. Switching language reloads the page (accepted; see decision 9).

## Anti-goals

- A raw key (`device-management.devices.fields.device-id.label`) shown to a person. A missing translation shows the
  English default.
- A second source of truth for a text. The default lives in code beside the key; the database holds translations
  only, never a copy of the English default.
- Text keys typed as string literals at call sites. A typo must fail the build.
- A half-translated language appearing in the switcher before an administrator publishes it.
- Junk rows on the admin page (keys guessed by reflection for properties nobody sees).

## Decisions

### Keys

1. **Shape: `<module>.<category…>.<name>`**, dot-separated kebab-case segments. The module segment is the module's
   **full name** in kebab case (`device-management`, `user-access`), not its schema. SharedKernel's shell texts use
   **`common`**. Category is optional and may nest:
   - `device-management.devices.new`
   - `device-management.manufacturers.device-models.actions.add`
   - `device-management.devices.fields.device-id.label`
   - `user-access.users.fields.user-name.hint`
   - `device-management.menu-title` (module only, no category)
   - `common.actions.delete`, `common.validation.not-empty`
2. **`TextKey : SerginStringKey`** (SharedKernel.Domain), so the existing format rules and PascalCase→kebab
   normalisation hold. No fixed first segment; at least two segments; a maximum length higher than `Permission`'s if
   the longest real key needs it (builder measures).
3. **`ISerginModule` gains `Name`** (`"DeviceManagement"`, `"UserAccess"`), also on `ISerginRemoteModule`. It is the
   module segment of every key the module declares. The catalog refuses, at host start, a key whose first segment is
   not its declaring module's name (or `common` for SharedKernel), the same way the permission catalogue refuses a
   foreign schema.

### Declarations

4. **Texts are declared by hand, once, in a static class per category**, in the module's
   `.Application.Configurations` project beside `<Aggregate>Permissions`:

   ```csharp
   public static class DeviceTexts
   {
       public static readonly TextCategory Category = TextCategory.Create("DeviceManagement", "Devices");

       public static readonly FieldText DeviceId = Category.Field("DeviceId", "Device ID", hint: "Unique meter serial.");
       public static readonly FieldText DeviceModelId = Category.Field("DeviceModelId", "Model");

       public static readonly TextDefinition New = Category.Text("New", "New device");
       public static readonly TextDefinition DeleteConfirm =
           Category.Text("DeleteConfirm", "Delete device {DeviceId}?");
   }
   ```

   - `TextDefinition` = key + default English text (+ optional description for the translator).
   - `FieldText` = one field: `Label` (required), `Hint`, `Placeholder` (optional), each a `TextDefinition` with keys
     `…fields.<field>.label|hint|placeholder`. The field name is the **property name**, which forms already keep
     equal between command and form model.
   - A child entity's category nests under its root's (`ManufacturerTexts.Category.Child("DeviceModels")`).
   - Shared texts (Delete, Save, Cancel, Reload, error titles, validation templates) are declared once in SharedKernel
     under `common` and reused; a module declares only what is its own.

   Why by hand and not derived from property names: machine-split names read badly (`DeviceId` → "Device id",
   `CreatedAtUtc` → "Created at utc"); the admin page needs the complete list at start, and guessing which properties
   are visible produces junk rows; and a static member gives a compile-time reference.
5. **Placeholders are named**, `{DeviceId}` not `{0}`, so a translator sees what each one is. A translation is refused
   on save if its placeholder set differs from the default's.
6. **Collection is by scan** *(my call)*: the catalog reads every public static `TextDefinition`/`FieldText` member of
   the public static classes in each module's `ConfigurationsAssembly`, and SharedKernel's own text classes. No hand
   list to forget. Host start is refused for: a duplicate key, the same key with two defaults, a malformed key, a key
   outside its module's prefix — each naming the offending declaration.
7. **`ListFieldSet` and `FieldText` meet.** A list field's column header and filter chip read the `FieldText` of the
   same name. Whether `ListField` takes a `FieldText` directly or looks it up is the builder's call; the outcome must
   be that a list column and a form field share one key.

### Lookup

8. **Retire `ILocalizer`; use `IStringLocalizer`.** `ILocalizer` already returns the built-in `LocalizedString` and
   adds nothing. SharedKernel registers one `IStringLocalizerFactory` (keys are global, so every `IStringLocalizer<T>`
   answers the same store). Lookup order for a key under `CultureInfo.CurrentUICulture`:
   1. the published translation for that culture — served even when flagged **needs review** (decision 22),
      **unless its placeholder set no longer matches the current default's**, in which case it is skipped
      *(my call)*;
   2. the translation for its parent culture (`fa-IR` → `fa`), under the same rule;
   3. the catalog's default English text;
   4. the key itself — only for a key the catalog does not know, logged as a warning.

   An extension method joins the declared key to the lookup — `localizer.Text(DeviceTexts.New)`,
   `localizer.Text(DeviceTexts.DeleteConfirm, new { DeviceId = id })`. Every shared component parameter that today
   takes a label `string` (`SerginNavItem.Label`, `SerginBreadcrumb`, `SerginPageAction`, `SerginNavGroup.Title`,
   field captions, view `Title`) takes a `TextDefinition` instead, so a literal cannot slip in.
   `Microsoft.Extensions.Localization.Abstractions` is already a central package and carries no UI dependency, so the
   Application layer may depend on it.
9. **Culture selection is the built-in pattern.** `UseRequestLocalization` with the cookie provider; a
   `/culture/set?culture=…&redirectUri=…` endpoint sets `CookieRequestCultureProvider`'s cookie and `LocalRedirect`s
   back; the switcher in `SerginMainLayout`'s app bar calls it with a full reload. Blazor Server fixes the culture
   when the circuit starts, so a switch reloads the page and unsaved input is lost — accepted. The supported-culture
   check reads the **published languages from the store** rather than a list fixed at startup (a small custom
   provider or option refresh — builder's call). The default culture is English, configurable as
   `Sergin:Localization:DefaultCulture` *(my call)*, validated at start naming the key like the other options.
   A WebApi host gets the built-in `Accept-Language` provider through the same registration.

### Right-to-left

10. Direction comes from `CultureInfo.TextInfo.IsRightToLeft`, overridable per language by an administrator.
    `<html lang dir>` is set from the culture in the host's `App.razor`, and MudBlazor's RTL mode is on for a
    right-to-left culture, so the prerender is already right. A Persian-capable font is loaded for right-to-left
    languages *(font choice is the builder's; Vazirmatn is the usual one)*.

### Persian calendar and digits

11. **Display.** Every date and number shown goes through one formatter that reads the current culture, layered on
    the existing `ILocalTimeConverter` (time zone stays its job). For Persian it formats in `PersianCalendar` — the
    builder checks whether `fa-IR` under ICU in .NET 10 already defaults to it and sets it if not — and maps digits
    0–9 to ۰–۹, which .NET does not do on its own.
12. **Ids and codes keep Latin digits** *(my call)*: digits change in dates, counts and numeric figures only; values
    a person copies (`MTR-123`, versions, GUIDs, user data) are shown as stored.
13. **Input.** Date pickers (the device list's created-between filter today) show a Jalali calendar through MudBlazor's
    culture/calendar support. The stored and transported value is unchanged — UTC `DateTimeOffset`.

### Validation

14. **Field names in messages come from `FieldText`.** One global FluentValidation `DisplayNameResolver` maps
    `(command type, property)` to the label of the matching `FieldText`. The command's category is found through its
    configuration: a configuration bound to its aggregate (`ICommandConfiguration<TRecord, TRoot>`) already names the
    root, and so the category. An unbound command names its category explicitly in its configuration. No validator
    rule gains a `.WithName`.
15. **FluentValidation's built-in messages become keys.** A custom `ILanguageManager` answers FluentValidation's
    templates from `common.validation.<validator>` keys, whose English defaults are FluentValidation's own English.
    The two repository rules (`MustExistIn`, `MustBeUniqueIn`) and every module's own `.WithMessage` text move to
    declared keys too.
16. **The guard.** Host start is refused when a public property of a command or query record in a module's Contracts
    assembly has no `FieldText` in its category, unless its configuration marks the property as not shown
    *(my call: the exclusion lives in the configuration, where policy already lives, not as an attribute on the
    record)*. Identifiers and paging records (`Id`, `Paggination`, `Sorting`, …) are excluded once, in SharedKernel.

### Errors

17. `SerginProblemFactory` keeps localising on `error.Code`, but every code that reaches a person becomes a declared
    text key with a title and a detail. Domain errors live in `.Domain`, which cannot see `.Application.Configurations`,
    so how a domain error's code and its declared text stay in step is the builder's call; the outcome must be a test
    or start-up check that fails when an error code has no declared text.

    **Validation errors are in scope, including the domain's own** (`Manufacturer.AddModel`/`RenameModel`,
    `Scope.Move`, `User`, `RoleAssignment`) *(my call)*. Their `Code` stays the **property name** — it is
    load-bearing: the WebApi `ValidationProblem` groups by it and forms match errors to fields by it. The text key
    travels separately, in the error's `Metadata` (a well-known metadata entry holding the key and its named
    arguments); `SerginProblemFactory` renders that key when present and `Description` otherwise, and the
    FluentValidation path sets it the same way. The "no declared text" check for validation errors reads that metadata
    key, not `Code`.

### Third-party texts

18. **MudBlazor** texts (pager "Rows per page", data-grid filter labels, date-picker captions) go through an
    adapter implementing MudBlazor's `MudLocalizer`, under `common.mud-blazor.<key>` with MudBlazor's own English
    as default. The builder enumerates MudBlazor 9.12's resource keys for the catalog.
19. **Permission labels** (`PermissionDefinition.Create(name, label)`) and nav group titles become `TextDefinition`s
    under their module and aggregate (`device-management.devices.permissions.read`).

### The Platform module, Localization feature

20. **A new module, `Platform`** (product owner's decision), for the features the platform itself needs:
    no business domain, usable by any host. Localization is its first feature; settings, operational monitors and
    similar platform features belong there later. Business features (meters, devices, customers) never do, and
    UserAccess stays its own module. It is not named `System`: `Sergin.System.*` would capture every `System.*`
    namespace reference inside it, and the `sys` permission prefix is reserved for SharedKernel.
    - **Its own repository**, `Sergin.Platform`, mounted as a git submodule at `src/Modules/Platform/`, embed-only
      like UserAccess (no solution or props files of its own). Its changes go through PRs in that repo; this repo adds
      the submodule, the `.slnx` entries and the host's two `ProjectReference`s (composition root and RCL).
    - Projects `Sergin.Platform.*` in the usual layering; schema **`pf`**; key segment **`platform`**
      (`platform.languages.fields.culture.label`); routes under `/pf/`; one nav group, its title a declared text.
    - Each feature's aggregates are its categories (`platform.languages.*`, `platform.translations.*`). A feature
      folder level inside the projects (`Localization/Languages/…`) is the builder's call.

    SharedKernel defines the seam the module implements —
    `ITranslationStore` (published languages; translations by culture) — and ships a default with no languages and no
    translations, so a host without the module shows English defaults and an English-only switcher (hidden when
    there is one language).
21. **`Language` aggregate**: culture code (validated against `CultureInfo`, unique), display name in its own script
    ("فارسی"), direction (derived, overridable), status `Draft`/`Published`. English is implicit: it is the code
    default, always present, not a row, not deletable. `Audited().Versioned()`.
22. **`Translation` aggregate**: language, key, text, and the **default text it was translated from**. A row whose
    stored source differs from the catalog's current default is shown as **needs review** — a code change to an
    English default flags its translations automatically. Unique on (language, key). A translation for a key the
    catalog no longer knows is kept and listed as **orphaned**, deletable, never served. `Audited().Versioned()`.
    Separate aggregate rather than a child of `Language`, since a language holds thousands of them.
23. **Pages** (routes under `/pf/`):
    - languages list and detail: add, rename, set direction, publish, unpublish, delete (with its translations);
    - translations of one language: filter by module, then category; search over key, default and translation;
      status filter (untranslated / translated / needs review / orphaned); columns key, default English,
      translation; inline edit; progress (translated / total) on the language.
24. **Permissions**, not scoped: `permission.pf.languages.read`, `permission.pf.languages.manage`,
    `permission.pf.translations.edit`. The seeded `administrator` role gets them.
25. **Draft preview for translators** (product owner's decision). A caller holding
    `permission.pf.translations.edit` sees Draft languages in the switcher too, marked as draft, and may switch to
    one to check translations in context. Everyone else sees Published languages only. So:
    - the supported-culture check is **per caller**: a Draft culture in the cookie is honoured only for a caller who
      holds the permission, and falls back to the default culture otherwise (a stale cookie after the permission is
      revoked, or a language unpublished, degrades quietly — no error page);
    - the cache loads Draft cultures on demand;
    - the switcher asks `IScopeAuthorization` (any scope, since the permission is not scoped).
26. **Cache and refresh.** Translations of every published language are held in an in-process singleton cache,
    loaded on first use per culture. A translation or language save refreshes the affected culture **after commit**,
    so the next render shows it. Multi-host invalidation (through the outbox) is not built; the cache is written so
    that adding it later is a subscriber, not a rewrite.

### Conversion

27. Every hardcoded user-visible text in SharedKernel (shell, page types, views, dialogs, error rendering),
    DeviceManagement and UserAccess moves to declared keys, including page titles, breadcrumbs, nav, actions,
    confirm dialogs, form labels/hints, list columns, filter chips, snackbars and validation messages. The guard in
    decision 16 is switched on once the conversion is complete. UserAccess and SharedKernel changes go through their
    own repositories' PRs, as usual.

## Constraints

- `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer: everything must pass analysis.
- Central Package Management: any new package is version-less in the csproj and added to `Directory.Packages.props`.
- `.razor` files stay markup-only; all C# in `.razor.cs`.
- SharedKernel stays module-agnostic; the database store is the module's, the seam is SharedKernel's.
- Blazor dispatch through `ISerginDispatcher`; admin pages follow the view/page split and the existing page types.
- A new aggregate feature (`Audited`, `Versioned`) means its migration in the same PR.
- Migrations converted to file-scoped namespaces by hand.
- Tests are integration tests against the real host with Testcontainers, plus unit tests for pure rules.

## Left to the builder

- Exact type names and namespaces (`TextKey`, `TextDefinition`, `FieldText`, `TextCategory`, `ITranslationStore`).
- How the supported-culture check reads the store (custom `RequestCultureProvider` vs option refresh).
- `ListField` ↔ `FieldText` wiring (decision 7) and domain-error ↔ text wiring (decision 17).
- Whether `fa-IR` needs its calendar set explicitly; the digit-mapping implementation; the Persian font.
- Enumerating MudBlazor's resource keys; the `MudLocalizer` adapter.
- Order of work across the three repositories and how the stages split into PRs.
- Test shapes. Expected coverage at least: catalog refusals (each one), lookup fallback order, placeholder check on
  save, cookie + redirect switch, prerendered `dir`/`lang` for an RTL culture, Jalali display and digits, display
  names in validation messages under a second culture, the field guard, needs-review and orphaned status, cache
  refresh after save, and a page rendered under a test culture with no untranslated literal left (a pseudo-locale
  that marks every looked-up text makes unmarked text detectable).

## Calls made without the product owner

- Default culture configurable as `Sergin:Localization:DefaultCulture`, default English.
- Collection of declarations by scanning `ConfigurationsAssembly`.
- Field-guard exclusions live in the command configuration.
- Ids, codes and user data keep Latin digits.
- English implicit, not a `Language` row.
- Orphaned translations kept and listed, not deleted automatically.
- Direction derived from the culture but overridable.
