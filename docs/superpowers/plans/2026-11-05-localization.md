# Localization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every user-visible text gets a declared key with an English default in code; administrators add languages and translate in the running system through a new Platform module; a switcher, right-to-left layout and Jalali dates complete it.

**Architecture:** SharedKernel gains the key type, the declaration types, a startup `TextCatalog`, an `IStringLocalizerFactory` over a translation cache fed by an `ITranslationStore` seam, culture selection through the built-in request-localization middleware, and adapters for FluentValidation and MudBlazor. A new `Platform` module (own repo, submodule) implements the store with `Language` and `Translation` aggregates and admin pages. Every existing text in SharedKernel, DeviceManagement and UserAccess is then converted and a startup guard locks it in.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9.12.0, FluentValidation 12.2.0, Microsoft.Extensions.Localization(.Abstractions) 10.0.14, EF Core + Npgsql, Dapper, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-11-05-localization-design.md` (decision numbers below refer to it).

## Global Constraints

- Build with `dotnet build Sergin.MeterMinder.slnx`: `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer — zero warnings.
- Central Package Management: `PackageReference` without `Version`; versions in `Directory.Packages.props`, alphabetical.
- `.razor` files markup-only; all C# in `.razor.cs`. No `@code` blocks.
- Never write a `Co-Authored-By: Claude` trailer (repo CLAUDE.md). Commit under the user's git identity.
- Key format: dot-separated kebab-case segments; module segment = module's full name in kebab case (`device-management`, `user-access`, `platform`); SharedKernel uses `common`.
- Placeholders are named (`{DeviceId}`), never positional.
- A missing translation renders the English default, never a raw key.
- English is the code default; the database holds translations only.
- Platform module: name `Platform`, schema `pf`, routes `/pf/`, key segment `platform`, repo `poursh/Sergin.Platform` mounted at `src/Modules/Platform/`, embed-only.
- Permissions (not scoped): `permission.pf.languages.read`, `permission.pf.languages.manage`, `permission.pf.translations.edit`. `administrator` already holds `permission.sys.platform-all`, which covers them; no seed change.
- Default culture option `Sergin:Localization:DefaultCulture`, default `en`; a culture that does not resolve fails start naming the key.
- Ids, codes, versions, GUIDs and user data keep Latin digits; Persian digits only in dates, counts and numeric figures.
- New aggregate features ship their migration in the same PR; migrations converted to file-scoped namespaces by hand.
- SharedKernel and UserAccess changes are committed in their submodules on a `feature/localization` branch and go through PRs in their repos; this repo bumps the pointers.
- All tests live in `tests/Sergin.MeterMinder.IntegrationTests.All/Localization/` (pure-rule tests need no database but sit in the same project, like `Lists/ListQuerySqlTests`), sharing `SerginWebApiFactory<Program>` through `[Collection(nameof(IntegrationTestCollection))]`.

## Review Focus

1. **A culture cookie naming a language that was deleted, unpublished, or is a Draft the caller may no longer preview** — the page renders in the default culture, no error page, no exception logged as an error. Test in Task 6.
2. **A translation saved with a placeholder typo (`{DeviceID}` for `{DeviceId}`) or a missing one** — refused as a validation error naming `Text`; and a stored translation whose placeholders drifted after a code change is skipped at lookup, so formatting never throws. Tests in Tasks 15 and 3.
3. **Two admins editing the same translation** — the second save at a stale version is refused with 412, the first edit survives. Test in Task 15.
4. **A culture code with odd casing or a neutral/specific pair (`FA-ir`, `fa` and `fa-IR` both added)** — normalised to `CultureInfo.Name`; a duplicate after normalisation is refused; lookup for `fa-IR` falls back to `fa`. Tests in Tasks 14 and 3.
5. **Right-to-left first paint** — the prerendered HTML already carries `dir="rtl"` and `lang="fa-IR"`, so the page does not flash left-to-right before the circuit starts. Test in Task 7.

---

## Phase 0 — Workspace

### Task 0: Prepare the worktree and submodule branches

**Files:** none (git only).

- [ ] **Step 1:** In `C:/@factory/Sergin/Sergin.MeterMinder-localization`, run `git submodule update --init --recursive`.
- [ ] **Step 2:** In `src/SharedKernel` and `src/Modules/UserAccess`, run `git switch -c feature/localization`.
- [ ] **Step 3:** Run `dotnet build Sergin.MeterMinder.slnx`. Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.
- [ ] **Step 4:** Start Docker Desktop if `docker info` fails (see memory: poll until it answers), then run the full suite once to record the baseline: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`. Expected: all pass.

---

## Phase 1 — SharedKernel mechanism

### Task 1: Key and declaration types

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Domain/Localizations/TextKey.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Domain/Localizations/ErrorText.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Localizations/TextDefinition.cs`, `TextCategory.cs`, `FieldText.cs`, `TextPlaceholders.cs`
- Test: `tests/.../Localization/TextDeclarationTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record TextKey : SerginStringKey` — `public static TextKey Create(string value)`; rules `new SerginStringKeyRules(FirstSegment: null, MinSegments: 2, MaxLength: 300)`; `string Module => Segments[0]`.
  - `public static class ErrorText` (Domain) — `public const string MetadataKey = "sergin:text"; public const string ArgumentsKey = "sergin:text-args"; public static Dictionary<string, object> Of(string key, IReadOnlyDictionary<string, object?>? arguments = null)` (validates `key` through `TextKey.Create`).
  - `public sealed record TextDefinition(TextKey Key, string Default, string? Description = null)` — `IReadOnlySet<string> Placeholders` (computed from `Default`).
  - `public static class TextPlaceholders` — `IReadOnlySet<string> In(string text)`; `string Format(string template, IReadOnlyDictionary<string, object?> arguments, CultureInfo culture)` (unknown placeholder left as written; `{{`/`}}` escape a brace).
  - `public sealed class TextCategory` — `static TextCategory Create(string module, params string[] path)` (PascalCase accepted, normalised to kebab); `TextCategory Child(string name)`; `TextDefinition Text(string name, string @default, string? description = null)`; `FieldText Field(string propertyName, string label, string? hint = null, string? placeholder = null)`; `string Module`, `IReadOnlyList<string> Path`, `string Prefix` (e.g. `device-management.devices`).
  - `public sealed record FieldText(TextCategory Category, string PropertyName, TextDefinition Label, TextDefinition? Hint, TextDefinition? Placeholder)` — keys `<prefix>.fields.<kebab(property)>.label|hint|placeholder`.

- [ ] **Step 1: Write the failing tests** in `TextDeclarationTests`:
  - `Category_KeysAreKebabUnderModule`: `TextCategory.Create("DeviceManagement", "Devices").Text("New", "New device").Key.Value == "device-management.devices.new"`.
  - `ChildCategory_Nests`: `Create("DeviceManagement","Manufacturers").Child("DeviceModels").Text("Add","Add model").Key.Value == "device-management.manufacturers.device-models.add"`.
  - `ModuleOnlyCategory`: `Create("DeviceManagement").Text("MenuTitle","Device Management").Key.Value == "device-management.menu-title"`.
  - `Field_DerivesThreeKeys`: `Field("DeviceId","Device ID", hint:"Unique meter serial.")` → label key `device-management.devices.fields.device-id.label`, hint key `….hint`, `Placeholder` null.
  - `Placeholders_AreNamed`: `TextPlaceholders.In("Delete device {DeviceId}? {{literal}}")` equals `{ "DeviceId" }`.
  - `Format_ReplacesNamed_LeavesUnknown`: `Format("{A} and {B}", {A:"x"}, en)` == `"x and {B}"`.
  - `TextKey_RejectsSingleSegment`: `Assert.Throws<ArgumentException>(() => TextKey.Create("devices"))`.
  - `ErrorText_Of_RejectsMalformedKey`: throws `ArgumentException` for `"Not A Key"`.
- [ ] **Step 2:** Run `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All --filter "FullyQualifiedName~Localization.TextDeclarationTests"`. Expected: build FAIL (types missing).
- [ ] **Step 3:** Implement the types above. Reuse `SerginStringKey.Normalize` for kebab conversion of every segment.
- [ ] **Step 4:** Run the filter again. Expected: PASS.
- [ ] **Step 5:** Commit in `src/SharedKernel`: `git add -A && git commit -m "Text keys and declaration types"`.

### Task 2: Module name and the text catalog

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Modules/ISerginModule.cs`, `ISerginRemoteModule.cs` — add `string Name { get; }`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement/DeviceManagementModule.cs` (`Name => "DeviceManagement"`), `src/Modules/UserAccess/Sergin.UserAccess/UserAccessModule.cs` (`"UserAccess"`), and every test-only `ISerginModule`/`ISerginRemoteModule` implementation in `tests/` (grep `: ISerginModule`, `: ISerginRemoteModule`)
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Localizations/TextSource.cs`, `TextCatalog.cs`, `TextCatalogEntry.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Localizations/Common/CommonTexts.cs` (category `Create("Common")`, initially `Save`, `Cancel`, `Delete`, `Reload` — Task 10 fills the rest)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs` — register `TextSource` per module and `TextCatalog` singleton; refuse two modules with the same `Name`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs` and `Hosts.WebApi/SerginWebApiExtensions.cs` — resolve `TextCatalog` at start next to `PermissionCatalog`
- Test: `tests/.../Localization/TextCatalogTests.cs`

**Interfaces:**
- Consumes: Task 1 types.
- Produces:
  - `public sealed class TextSource` — `static TextSource FromAssembly(Assembly assembly, string moduleName)`; `static TextSource FromTypes(string moduleName, params Type[] declaringTypes)` (tests); `static TextSource Common` (SharedKernel's own classes, module segment `common`). Collects every public static field or property of type `TextDefinition` or `FieldText` (and `TextDefinition`s of each `FieldText`) on public static classes, including nested static classes.
  - `public sealed class TextCatalog` — `static TextCatalog FromSources(IEnumerable<TextSource>)`; `IReadOnlyCollection<TextCatalogEntry> Entries`; `bool TryGet(string key, [NotNullWhen(true)] out TextDefinition? definition)`; `IReadOnlyList<FieldText> FieldsOf(TextCategory category)`; `FieldText? FindField(TextCategory category, string propertyName)`.
  - `public sealed record TextCatalogEntry(TextDefinition Definition, string Module, IReadOnlyList<string> Category, Type DeclaringType)`.

- [ ] **Step 1: Write the failing tests** in `TextCatalogTests` (test-only static classes nested in the test file):
  - `Catalog_CollectsTextsAndFieldParts` — a class with one `TextDefinition` and one `FieldText` (label + hint) yields 3 entries.
  - `Catalog_RefusesDuplicateKey` — two classes declaring `device-management.devices.new` → `InvalidOperationException` whose message names both declaring types and the key.
  - `Catalog_RefusesSameKeyTwoDefaults` — message names the key and both defaults.
  - `Catalog_RefusesForeignModule` — a `device-management.*` text in a source named `UserAccess` → message names the type and `user-access`.
  - `Catalog_AcceptsCommonFromSharedKernelOnly` — `common.*` in a module source is refused.
  - `Host_ResolvesCatalogAtStart` — `factory.Services.GetRequiredService<TextCatalog>().TryGet("common.save", out _)` is true.
  - `Core_RefusesTwoModulesWithOneName` — building a host with two test modules named `X` throws naming `X`.
- [ ] **Step 2:** Run `--filter "FullyQualifiedName~Localization.TextCatalogTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement. A malformed key surfaces as the `TypeInitializationException` of the declaring class; catch it in the scan and rethrow `InvalidOperationException` naming the type (same as permissions).
- [ ] **Step 4:** Run filter, then the whole suite. Expected: PASS.
- [ ] **Step 5:** Commit in `src/SharedKernel` ("Text catalog and module names"), in `src/Modules/UserAccess` ("Module name"), and in the root repo (DeviceManagement + tests + submodule pointers).

### Task 3: Translation store seam, cache, and the string localizer

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Localizations/ITranslationStore.cs`, `LanguageInfo.cs`, `StoredTranslation.cs`, `ITranslationCache.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure/Localizations/EmptyTranslationStore.cs`, `TranslationCache.cs`, `SerginStringLocalizer.cs`, `SerginStringLocalizerFactory.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Localizations/LocalizerTextExtensions.cs`
- Modify: `SerginCoreExtensions.cs` — `TryAddSingleton<ITranslationStore, EmptyTranslationStore>()`, `AddSingleton<ITranslationCache, TranslationCache>()`, `AddSingleton<IStringLocalizerFactory, SerginStringLocalizerFactory>()`, `AddTransient(typeof(IStringLocalizer<>), typeof(StringLocalizer<>))`, `AddSingleton<IStringLocalizer>(sp => sp.GetRequiredService<IStringLocalizerFactory>().Create(typeof(TextCatalog)))`
- Modify: `Directory.Packages.props` / SharedKernel.Infrastructure csproj — reference `Microsoft.Extensions.Localization` (add `PackageVersion` 10.0.14 if missing)
- Test: `tests/.../Localization/LookupTests.cs`

**Interfaces:**
- Consumes: `TextCatalog`, `TextPlaceholders`.
- Produces:
  - `public sealed record LanguageInfo(string Culture, string DisplayName, bool IsRightToLeft, bool IsPublished)`.
  - `public sealed record StoredTranslation(string Text, string SourceText)`.
  - `public interface ITranslationStore` — `Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync(CancellationToken ct)`; `Task<IReadOnlyDictionary<string, StoredTranslation>> GetTranslationsAsync(string culture, CancellationToken ct)` (draft languages included; the cache decides).
  - `public interface ITranslationCache` — `Task EnsureLoadedAsync(string culture, CancellationToken ct)`; `Task RefreshAsync(string culture, CancellationToken ct)`; `Task RefreshLanguagesAsync(CancellationToken ct)`; `IReadOnlyList<LanguageInfo> Languages { get; }` (after first load); `bool TryGet(string culture, string key, [NotNullWhen(true)] out StoredTranslation? translation)` (synchronous; returns false for a culture not loaded).
  - `public static class LocalizerTextExtensions` — `string Text(this IStringLocalizer localizer, TextDefinition text, object? arguments = null)` (anonymous object or `IReadOnlyDictionary<string, object?>`).
  - Lookup order (decision 8): culture translation if placeholder sets match the definition's, else parent culture under the same rule, else `Default`, else (unknown key) the key with `ResourceNotFound = true` and a `LogWarning` once per key.

- [ ] **Step 1: Write the failing tests** in `LookupTests` (a fake `ITranslationStore` registered in a derived factory via `WithWebHostBuilder`, cultures set with `CultureInfo.CurrentUICulture` inside the test):
  - `NoTranslation_ShowsDefault` — `localizer.Text(CommonTexts.Save)` == `"Save"` under `de-DE`.
  - `Translation_Shown` — store has `de` → `common.save` = `"Speichern"`; after `EnsureLoadedAsync("de")` returns `"Speichern"` under `de`.
  - `ParentCulture_Fallback` — store has only `fa`; lookup under `fa-IR` returns the `fa` text.
  - `NeedsReview_StillShown` — `SourceText` differs from default but placeholders equal → translation shown.
  - `PlaceholderDrift_Skipped` — default `"Delete {DeviceId}?"`, translation `"حذف {Device}؟"` → default shown, formatted.
  - `UnknownKey_ReturnsKeyNotFound` — `localizer["nope.key"]` has `ResourceNotFound == true`.
  - `Arguments_Formatted` — `Text(def, new { DeviceId = "MTR-1" })` == `"Delete device MTR-1?"`.
  - `Refresh_ShowsNewTextOnNextLookup` — change fake store, `RefreshAsync("de")`, lookup returns new text.
- [ ] **Step 2:** Run `--filter "FullyQualifiedName~Localization.LookupTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement. Cache holds `ConcurrentDictionary<string, FrozenDictionary<string, StoredTranslation>>`; `RefreshAsync` swaps the entry atomically. The localizer reads `CultureInfo.CurrentUICulture` per call.
- [ ] **Step 4:** Run filter. Expected: PASS.
- [ ] **Step 5:** Commit in `src/SharedKernel` ("String localizer over a translation cache").

### Task 4: Retire `ILocalizer`

**Files:**
- Delete: `src/SharedKernel/Sergin.SharedKernel.Application/Localizations/ILocalizer.cs`, `LocalizerExtensions.cs`, `src/SharedKernel/Sergin.SharedKernel.Infrastructure/Localizations/DefaultLocalizer.cs`
- Modify: every file using `ILocalizer` (39 in SharedKernel — `grep -rl ILocalizer src/SharedKernel --include=*.cs`; plus UserAccess `RoleCreateView.razor.cs`, `RoleDetailView.razor.cs`, `PermissionLabels.cs`) — inject `IStringLocalizer`; `SerginProblemFactory.Create(Error, IStringLocalizer)`
- Modify: `SerginCoreExtensions.cs` — drop the `ILocalizer` registration
- Test: existing suite (no new behaviour)

**Interfaces:**
- Consumes: Task 3 `IStringLocalizer` registration.
- Produces: `SerginProblemFactory.Create(Error error, IStringLocalizer localizer)`. String-keyed calls (`localizer[label]`) stay string-keyed for now; Task 10 turns them into `TextDefinition`s.

- [ ] **Step 1:** Replace the type at every call site; replace `LocalizedOrDefault(key, fallback)` with `localizer[key] is { ResourceNotFound: false } t ? t.Value : fallback` via a private helper where used more than once.
- [ ] **Step 2:** Run `dotnet build Sergin.MeterMinder.slnx`. Expected: 0 warnings, 0 errors; `grep -rn "ILocalizer" src --include=*.cs` returns nothing.
- [ ] **Step 3:** Run the full suite. Expected: same pass count as Task 0's baseline.
- [ ] **Step 4:** Commit in `src/SharedKernel` and `src/Modules/UserAccess` ("Use IStringLocalizer"); bump pointers in root.

### Task 5: Error texts and validation display names

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation/Errors/SerginProblemFactory.cs` — when `error.Metadata` holds `ErrorText.MetadataKey`, render that key (with `ErrorText.ArgumentsKey`) as the detail; else today's rule
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Localizations/Common/CommonValidationTexts.cs` — one `TextDefinition` per FluentValidation built-in validator, key `common.validation.<kebab(validator name)>` (`NotEmptyValidator` → `common.validation.not-empty-validator`), default = FluentValidation's English (`new LanguageManager().GetString(name, CultureInfo.GetCultureInfo("en"))`), plus `MustExistIn`/`MustBeUniqueIn` keys `common.validation.must-exist-in` / `must-be-unique-in`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Localizations/Validation/TextLanguageManager.cs` (`: ILanguageManager`), `FieldDisplayNameResolver.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Validations/RepositoryRuleBuilderExtensions.cs` — messages from the two new keys
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Aggregates/AggregateFeatureBuilder.cs` — `Texts(TextCategory category)`; `AggregateFeatureRegistry.TextsOf(Type root) : TextCategory?`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/CommandConfigurationBuilder.cs` — `Texts(TextCategory category)`; `NotShown<TProperty>(Expression<Func<TCommand, TProperty>> property)`; registry exposes `CategoryOf(Type request) : TextCategory?` (own, else bound root's) and `HiddenProperties(Type request) : IReadOnlySet<string>`
- Modify: `SerginCoreExtensions.cs` — set `ValidatorOptions.Global.LanguageManager` and `ValidatorOptions.Global.DisplayNameResolver` from the built provider (once, at start, idempotent)
- Test: `tests/.../Localization/ValidationTextTests.cs`, `tests/.../Localization/ErrorTextTests.cs`

**Interfaces:**
- Consumes: `TextCatalog.FindField`, `IStringLocalizer`, `ErrorText`.
- Produces:
  - `AggregateFeatureBuilder<TRoot>.Texts(TextCategory)`, `CommandConfigurationBuilder<TCommand>.Texts(TextCategory)`, `CommandConfigurationBuilder<TCommand>.NotShown(...)`.
  - `CommandConfigurationRegistry.CategoryOf(Type)`, `.HiddenProperties(Type)`.
  - Display name: `FieldDisplayNameResolver.Resolve(Type type, MemberInfo member, LambdaExpression expression) : string?` — the localized `FieldText.Label` of `(CategoryOf(type), member.Name)`, else `null` (FluentValidation's default splitting applies).

- [ ] **Step 1: Write the failing tests:**
  - `ValidationTextTests.DisplayName_ComesFromFieldText` — test command bound to a test category with `Field("Name","Display name")`; validator `RuleFor(x => x.Name).NotEmpty()`; sending empty yields description `"'Display name' must not be empty."`.
  - `…DisplayName_Translated` — fake store `de`: field label `"Anzeigename"`, `common.validation.not-empty-validator` = `"'{PropertyName}' darf nicht leer sein."`; under `de` the description is `"'Anzeigename' darf nicht leer sein."`.
  - `…MustBeUniqueIn_UsesKey` — duplicate `UserName` on `CreateUserCommand` returns description from `common.validation.must-be-unique-in` with the user-name label.
  - `ErrorTextTests.MetadataKey_RendersDeclaredText` — `Error.Validation("Name", "fallback", ErrorText.Of("common.save"))` through `SerginProblemFactory` → detail `"Save"`; with a `de` translation → translated.
  - `ErrorTextTests.NoMetadata_KeepsDescription` — today's behaviour unchanged.
- [ ] **Step 2:** Run `--filter "FullyQualifiedName~Localization.ValidationTextTests|FullyQualifiedName~Localization.ErrorTextTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement. FluentValidation evaluates messages when the validator runs, inside the dispatch scope, so `CultureInfo.CurrentUICulture` there is the circuit's culture.
- [ ] **Step 4:** Run filter, then `Validation/` folder tests (`--filter "FullyQualifiedName~Validation"`). Expected: PASS — existing message assertions that quoted FluentValidation's English still pass because defaults are its English.
- [ ] **Step 5:** Commit in `src/SharedKernel` ("Validation and error texts through the catalog").

### Task 6: Culture selection, draft preview, default culture

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation/Localizations/SerginLocalizationOptions.cs` (+ `SerginLocalizationOptionsValidator`) — `DefaultCulture = "en"`, section `Sergin:Localization`
- Create: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/Localizations/SerginCultureProvider.cs` (`: RequestCultureProvider`), `CultureEndpoints.cs` (`MapGet("/culture/set", …)`)
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Localizations/IDraftLanguagePreviewPolicy.cs` — `Task<bool> CanPreviewAsync(CancellationToken ct)`; and `src/SharedKernel/Sergin.SharedKernel.Infrastructure/Localizations/DenyDraftPreview.cs` (always false). SharedKernel defines no permission for it; Platform supplies the real policy in Task 17.
- Modify: `SerginWebUiExtensions.cs` — bind/validate options; `AddRequestLocalization`; in `UseSerginWebUiAsync`, `UseRequestLocalization` with `SerginCultureProvider` as the only provider, placed after authentication and before `MapRazorComponents`; map `/culture/set`
- Modify: `SerginWebApiExtensions.cs` — `UseRequestLocalization` with the built-in `AcceptLanguageHeaderRequestCultureProvider` restricted to published cultures (unhosted, compile + one test)
- Test: `tests/.../Localization/CultureSelectionTests.cs`

**Interfaces:**
- Consumes: `ITranslationCache` (`Languages`, `EnsureLoadedAsync`), `IDraftLanguagePreviewPolicy`.
- Produces:
  - `/culture/set?culture=<name>&redirectUri=<local path>` — writes `CookieRequestCultureProvider.DefaultCookieName` and `Results.LocalRedirect(redirectUri)`; an unknown culture writes nothing and still redirects.
  - `SerginCultureProvider` result: cookie culture if `en`-family default, or a published language, or a draft language and `CanPreviewAsync()`; otherwise `null` (default culture). It calls `EnsureLoadedAsync` for the chosen culture before returning.
  - `IDraftLanguagePreviewPolicy` (Application) and `DenyDraftPreview` (Infrastructure, `TryAddScoped`).

- [ ] **Step 1: Write the failing tests:**
  - `SetCulture_WritesCookieAndRedirects` — `GET /culture/set?culture=de&redirectUri=/dm/devices` with published `de` → 302 to `/dm/devices`, `Set-Cookie` contains `.AspNetCore.Culture=c%3Dde%7Cuic%3Dde`.
  - `SetCulture_RejectsOffsiteRedirect` — `redirectUri=https://evil.example` → not a redirect to that host.
  - `PublishedCookie_RendersInThatCulture` — cookie `de`, `GET /` → `<html lang="de"`. (Task 7 adds `lang`; until then assert a translated `common.*` text in the prerender.)
  - `DeletedLanguageCookie_FallsBackQuietly` (Review Focus 1) — cookie `xx-unknown` → 200, default culture, no error-level log entry.
  - `DraftCookie_WithoutPreview_FallsBack` / `DraftCookie_WithPreview_Honoured` — fake policy returning false / true.
  - `DefaultCulture_Invalid_FailsStart` — `Sergin:Localization:DefaultCulture = "zz-ZZ-bad"` → start throws naming `Sergin:Localization:DefaultCulture`.
- [ ] **Step 2:** Run `--filter "FullyQualifiedName~Localization.CultureSelectionTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run filter. Expected: PASS.
- [ ] **Step 5:** Commit in `src/SharedKernel` ("Culture selection with draft preview").

### Task 7: Switcher and right-to-left

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Localizations/SerginLanguageSwitcher.razor` + `.razor.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Localizations/SerginDocumentCulture.cs` — `static string Lang => CultureInfo.CurrentUICulture.Name`, `static string Dir => …IsRightToLeft ? "rtl" : "ltr"` (direction from `ITranslationCache.Languages` override when present)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Layout/SerginMainLayout.razor{,.cs}` — switcher in the app bar; wrap content in `<MudRTLProvider RightToLeft="isRightToLeft">`
- Modify: `src/Hosts/Sergin.MeterMinder.Hosts.All/Components/App.razor` — `<html lang="@SerginDocumentCulture.Lang" dir="@SerginDocumentCulture.Dir">`; load Vazirmatn from Google Fonts only when `Dir == "rtl"`, applied through a `[dir=rtl]` rule in `app.css`
- Modify: `CommonTexts.cs` — `Language` ("Language"), `DraftSuffix` ("draft")
- Test: `tests/.../Localization/RightToLeftRenderingTests.cs`

**Interfaces:**
- Consumes: `ITranslationCache.Languages`, `IDraftLanguagePreviewPolicy`, `/culture/set`.
- Produces: `SerginLanguageSwitcher` — lists English + published languages (+ drafts marked `CommonTexts.DraftSuffix` when `CanPreviewAsync`), hidden when only English is listed; selecting navigates with `forceLoad: true` to `/culture/set?culture=…&redirectUri=<current relative uri>`.

- [ ] **Step 1: Write the failing tests:**
  - `RtlCulture_PrerendersDirAndLang` (Review Focus 5) — published `fa-IR` (`IsRightToLeft` true), cookie `fa-IR`, `GET /dm/devices` → HTML contains `<html lang="fa-IR" dir="rtl"` and `mud-application-layout-rtl`.
  - `LtrCulture_Dir_Ltr` — default → `dir="ltr"`.
  - `DirectionOverride_Wins` — store marks `fa-IR` `IsRightToLeft=false` → `dir="ltr"`.
  - `Switcher_HiddenWithOnlyEnglish` / `Switcher_ListsPublished_NotDrafts` — prerendered app bar markup.
- [ ] **Step 2:** Run `--filter "FullyQualifiedName~Localization.RightToLeftRenderingTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run filter and `Shell/` tests. Expected: PASS.
- [ ] **Step 5:** Commit in `src/SharedKernel` and root (`App.razor`, `app.css`).

### Task 8: Jalali dates and Persian digits

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation/Localizations/ICultureFormatter.cs`, `src/SharedKernel/Sergin.SharedKernel.Infrastructure/Localizations/CultureFormatter.cs`
- Modify: every place that formats a date or number for display (grep `ToString("` and `:yyyy`/`:g`/`:N0` in `*.razor` and `*.razor.cs` across SharedKernel and both modules; `SerginInfoDialog`, list cells, overview cards, `SerginEntityList` counts) — go through `ICultureFormatter`
- Modify: date pickers (`MudDatePicker`/`MudDateRangePicker` in the device list filter) — `Culture="@Formatter.Culture"`
- Test: `tests/.../Localization/CultureFormatterTests.cs`

**Interfaces:**
- Consumes: `ILocalTimeConverter`.
- Produces: `public interface ICultureFormatter` — `CultureInfo Culture { get; }` (for `fa*`, a clone with `DateTimeFormat.Calendar = new PersianCalendar()` if ICU did not already set it); `string Date(DateTimeOffset utc)`; `string DateTime(DateTimeOffset utc)`; `string Number(long value)`; `string Number(decimal value, int decimals)`. Digits mapped `0-9 → ۰-۹` when `Culture.TwoLetterISOLanguageName == "fa"`. Registered scoped.

- [ ] **Step 1: Write the failing tests** (`CultureInfo.CurrentUICulture`/`CurrentCulture` set in the test; time zone UTC):
  - `Persian_Date_IsJalaliWithPersianDigits` — `Date(2026-11-05T00:00Z)` under `fa-IR` == `"۱۴۰۵/۰۸/۱۴"`.
  - `Persian_Number_PersianDigits` — `Number(1000)` == `"۱٬۰۰۰"`.
  - `English_Date_Gregorian` — `"11/5/2026"` under `en-US`.
  - `Persian_Culture_CalendarIsPersian` — `Culture.DateTimeFormat.Calendar is PersianCalendar`.
  - `DeviceId_KeepsLatinDigits` — render device list under `fa-IR`; `MTR-1` appears with Latin digits.
- [ ] **Step 2:** Run `--filter "FullyQualifiedName~Localization.CultureFormatterTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement and convert the display sites.
- [ ] **Step 4:** Run filter, `Shell/` and `Lists/`. Expected: PASS.
- [ ] **Step 5:** Commit in each touched repo.

### Task 9: MudBlazor texts

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Localizations/SerginMudLocalizer.cs` (`: MudLocalizer`), `MudBlazorTexts.cs` (builds `TextDefinition`s from MudBlazor 9.12's `LanguageResource` English resource set: key `common.mud-blazor.<kebab(segment)>` per `_`-separated part, default = MudBlazor's English)
- Modify: `SerginBlazorKitExtensions.cs` — `AddTransient<MudLocalizer, SerginMudLocalizer>()`
- Modify: `TextSource.Common` — include `MudBlazorTexts` (built, not static fields: expose `TextSource.Common` as `FromDefinitions(...)`)
- Test: `tests/.../Localization/MudBlazorTextTests.cs`

- [ ] **Step 1: Write the failing tests:**
  - `Catalog_HoldsMudPagerText` — catalog contains a `common.mud-blazor.*` entry whose default is `"Rows per page:"` (the `MudDataGrid_RowsPerPage`/`MudTablePager` key in 9.12).
  - `MudPager_RendersTranslation` — `de` translation for that key; device list under `de` prerenders the German text.
- [ ] **Step 2:** Run filter. Expected: FAIL.
- [ ] **Step 3:** Implement. `SerginMudLocalizer` maps MudBlazor's key → catalog key → `IStringLocalizer`.
- [ ] **Step 4:** Run filter. Expected: PASS.
- [ ] **Step 5:** Commit in `src/SharedKernel`.

### Task 10: Shared components take `TextDefinition`; convert the shell

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Modules/SerginNavItem.cs` (`Label` → `TextDefinition`), `SerginNavGroup.cs` (`Title` → `TextDefinition`); `Presentation.Blazor/Navigation/SerginBreadcrumb.cs` (`Label` → `TextDefinition`; add `static SerginBreadcrumb Data(string value, string? href = null)` for user data like a device id, rendered unlocalized); `Pages/SerginPageAction.cs` (`Label`); view parameters `Title`, `FieldsTitle`, `SerginDetailField.Label`, list column headers, `SubmitLabel`, confirm dialog texts, `SerginHome.UseNavItem(label: TextDefinition, …)`
- Permission labels: `PermissionDefinition` (Domain) keeps its `string` label as the English default. At start, `TextSource.FromPermissions(PermissionCatalog catalog, Func<Type, TextCategory?> categoryOfRoot)` (the second argument is `AggregateFeatureRegistry.TextsOf`) derives one `TextDefinition` per definition — key `<root's category prefix>.permissions.<last segment of the permission name>` (`device-management.devices.permissions.read`), default = the label — and the `TextCatalog` includes them. `PermissionCatalogEntry` gains `TextDefinition Label`; the role pages render through it.
- Modify: `CommonTexts.cs` and new `Common/ShellTexts.cs`, `Common/ErrorTexts.cs` — every literal in SharedKernel's Blazor shell, page types, views, dialogs, `SerginWelcome`, and every error title/detail key `SerginProblemFactory` looks up (`<code>.title`, `<code>.detail`, validation title) re-declared under `common.errors.*`
- Test: `tests/.../Localization/ShellTextTests.cs` + existing `Shell/` tests

**Interfaces:**
- Produces: the parameter types above. Modules that still pass strings stop compiling — Tasks 11–12 convert them; do Tasks 10–12 on one branch and run the suite after Task 12. A module-wide build break between commits is acceptable inside this branch; each commit must still build its own repo's SharedKernel projects.

- [ ] **Step 1: Write the failing tests:**
  - `Shell_RendersTranslatedChrome` — `de` translations for `common.actions.reload` and the breadcrumb home label; `/dm/devices` prerender shows both.
  - `NotFound_TitleTranslated` — `de` for `common.errors.not-found.title`; `/dm/devices/<random guid>` shows it.
  - `PermissionLabel_IsCatalogText` — catalog holds `device-management.devices.permissions.read` with default equal to `DevicePermissions.Read`'s label.
- [ ] **Step 2:** Run filter. Expected: FAIL.
- [ ] **Step 3:** Change the types and convert SharedKernel's literals.
- [ ] **Step 4:** Build SharedKernel projects only: `dotnet build src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor`. Expected: 0 warnings.
- [ ] **Step 5:** Commit in `src/SharedKernel` ("Shell texts declared").

---

## Phase 2 — Convert the modules

### Task 11: Convert DeviceManagement

**Files:**
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Configurations/DeviceManagementTexts.cs` (module-only category: menu title, nav group), `Devices/DeviceTexts.cs`, `Manufacturers/ManufacturerTexts.cs`, `Manufacturers/DeviceModels/DeviceModelTexts.cs` — one `FieldText` per command property shown to a person, one `TextDefinition` per page title, action, confirm, snackbar, empty state and domain error
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Domain/**/<Aggregate>ErrorKeys.cs` — `public const string` keys the domain's `Error.Validation(..., metadata: ErrorText.Of(...))` calls use (`Manufacturer.AddModel`/`RenameModel`)
- Modify: each `<Root>AggregateFeatureConfiguration` — `builder.Texts(<Aggregate>Texts.Category)`; each configuration of a command with an unshown property — `.NotShown(x => x.Id)` etc.
- Modify: all 24 `.razor`/`.razor.cs` in `Sergin.MeterMinder.DeviceManagement.Presentation.Blazor`, `DeviceManagementNavigation`, the 9 validators' `.WithMessage` texts, `DeviceListFields`/`ManufacturerListFields`/`DeviceModelListFields` column headers (look up the `FieldText` of the same id — decision 7)
- Test: `tests/.../Localization/DeviceManagementTextTests.cs`

- [ ] **Step 1: Write the failing tests:**
  - `EveryDomainErrorKey_IsDeclared` — every `const string` in `*ErrorKeys` classes of the DeviceManagement Domain assembly is in `TextCatalog` (reflection).
  - `DeviceCreateForm_LabelsFromCatalog` — `de` translation for `device-management.devices.fields.device-id.label`; `/dm/devices/new` prerender contains it.
  - `DuplicateModelName_TranslatedMessage` — `AddDeviceModel` with a duplicate name under `de` returns the translated text in the error rendered by `SerginProblemFactory`, code still `Name`.
- [ ] **Step 2:** Run filter. Expected: FAIL.
- [ ] **Step 3:** Convert.
- [ ] **Step 4:** Run filter. Expected: PASS.
- [ ] **Step 5:** Commit in root ("DeviceManagement texts declared").

### Task 12: Convert UserAccess

**Files:** same shape as Task 11 in `src/Modules/UserAccess/` — `UserAccessTexts`, `Users/UserTexts`, `Roles/RoleTexts`, `Scopes/ScopeTexts`, assignment texts; `*ErrorKeys` in `Sergin.UserAccess.Domain` (`Scope.Move`, `User`, `RoleAssignment`); 20 razor files; 15 validators; `PermissionLabels.cs` replaced by the catalog's permission texts; `ScopeTreeLayout` texts.
- Test: `tests/.../Localization/UserAccessTextTests.cs` — `EveryDomainErrorKey_IsDeclared` (UserAccess Domain), `UserCreateForm_LabelsFromCatalog`, `ScopeMoveRefusal_TranslatedMessage`.

- [ ] **Step 1:** Write the three tests. **Step 2:** run, FAIL. **Step 3:** convert. **Step 4:** run filter, then the **full suite**. Expected: PASS, pass count ≥ baseline. Update assertions that quoted an old literal only where the literal itself changed (e.g. "Device id" → "Device ID"); list each changed assertion in the commit message.
- [ ] **Step 5:** Commit in `src/Modules/UserAccess` ("UserAccess texts declared"); bump both submodule pointers in root.

---

## Phase 3 — Platform module

### Task 13: Scaffold the Platform module in its own repo

**Files:**
- Create (via `/add-module` skill, module `Platform`, schema `pf`, with Blazor RCL): `src/Modules/Platform/Sergin.Platform{,.Domain,.Application,.Application.Contracts,.Application.Configurations,.Infrastructure,.Infrastructure.Data,.Presentation.WebApi,.Presentation.Blazor}`
- Create: `src/Modules/Platform/.claude/CLAUDE.md`, `README.md`, `LICENSE` (copy UserAccess's), `.gitignore`
- Modify: `.gitmodules`, `Sergin.MeterMinder.slnx` (folder `/src/Modules/Platform/` with the nested `Presentation/` grouping), `src/Hosts/Sergin.MeterMinder.Hosts.All/*.csproj` (two `ProjectReference`s: composition root + RCL), `Program.cs` (`new PlatformModule()` in `modules`)
- Test: `tests/.../Localization/PlatformModuleTests.cs`

**Interfaces:**
- Produces: `PlatformModule : ISerginWebApiModule, ISerginWebUiModule` — `Name "Platform"`, `Schema "pf"`, nav group title `PlatformTexts.MenuTitle` ("Platform"), Order 300; `PlatformDbContext` (`IOutboxDbContext` not needed); `PlatformTexts` category `Create("Platform")`.

- [ ] **Step 1:** Create the module folder, `git init`, `git remote add origin https://github.com/poursh/Sergin.Platform.git`. Scaffold per `/add-module`. Initial commit; `git push -u origin main`.
- [ ] **Step 2:** In root: remove the folder, `git submodule add https://github.com/poursh/Sergin.Platform.git src/Modules/Platform`.
- [ ] **Step 3: Write the failing test** `PlatformModule_IsComposed` — `SerginUiModuleCatalog` contains a module with schema `pf`, and `TextCatalog.TryGet("platform.menu-title", out _)`.
- [ ] **Step 4:** Build + run test. Expected: PASS after wiring; migrations: initial `pf` schema migration (`InitialCreate`, file-scoped namespace).
- [ ] **Step 5:** Commit in `src/Modules/Platform` and root ("Platform module").

### Task 14: `Language` aggregate and its slices

**Files (Platform repo, `Languages/` folders in every layer, following `/add-feature`):**
- Domain: `Languages/Language.cs` — `LanguageId(Guid)`, `CultureName(string)` (`MaxLength 20`; normalised to `CultureInfo.GetCultureInfo(value).Name`; refuses a culture .NET does not know and the invariant culture and any `en*` culture), `LanguageDisplayName(string)` (`MaxLength 100`), `LanguageDirection` enum `FromCulture | LeftToRight | RightToLeft`, `LanguageStatus` enum `Draft | Published`; behaviours `Create(culture, displayName)` (Draft), `Rename`, `SetDirection`, `Publish`, `Unpublish`; `bool IsRightToLeft` (derived unless overridden); `ILanguageRepository : IRepository<Language, LanguageId>, IUniqueKeyRepository<CultureName>`
- Configurations: `Languages/LanguagePermissions.cs` (`Read`, `Manage`), `Languages/LanguageTexts.cs`, `LanguageAggregateFeatureConfiguration` (`Audited().Versioned()`, permissions, `Texts`), one configuration per command (`RequirePermissions(LanguagePermissions.Manage)`; all but Add `RequireExpectedVersion()`)
- Contracts + Application: `AddLanguage`, `RenameLanguage`, `SetLanguageDirection`, `PublishLanguage`, `UnpublishLanguage`, `DeleteLanguage` (deletes its translations in the same save), `GetLanguageById`, `GetLanguageList` (with `LanguageListFields`: search culture + display name; sort by culture), validators for each write
- Infrastructure.Data: configuration, unique index `ix_language_culture`, migration `AddLanguages`
- After each write: `ITranslationCache.RefreshLanguagesAsync` after `SaveChangesAsync` succeeds (in the handler)
- Test: `tests/.../Localization/LanguageTests.cs`

- [ ] **Step 1: Write the failing tests** (through `ISerginDispatcher` from a scope):
  - `AddLanguage_NormalisesCulture` (Review Focus 4) — `"FA-ir"` stored as `"fa-IR"`.
  - `AddLanguage_DuplicateAfterNormalisation_Refused` — validation error code `Culture`.
  - `AddLanguage_UnknownCulture_Refused`, `AddLanguage_English_Refused`.
  - `Direction_DerivedFromCulture_AndOverridable` — `fa-IR` → RTL; `SetDirection(LeftToRight)` → LTR.
  - `Publish_ShowsInCacheLanguages` — after publish, `ITranslationCache.Languages` has it with `IsPublished`.
  - `Delete_RemovesTranslations` — translations of the language gone.
  - `Manage_RequiresPermission` — viewer user → `Forbidden`.
  - `Rename_StaleVersion_412`.
- [ ] **Step 2:** Run filter. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run filter. Expected: PASS.
- [ ] **Step 5:** Commit in Platform repo and bump pointer.

### Task 15: `Translation` aggregate, store implementation, cache refresh

**Files (Platform repo, `Translations/`):**
- Domain: `Translation.cs` — `TranslationId(Guid)`, `LanguageId`, `TranslationKey(string)` (wraps a `TextKey` value), `TranslationText(string)` (`MaxLength 4000`), `SourceText(string)`; `Create(languageId, key, text, sourceText)`, `Change(text, sourceText)`; `ITranslationRepository : IRepository<Translation, TranslationId>` + `GetAsync(LanguageId, TranslationKey)`
- Configurations: `TranslationPermissions.Edit`, `TranslationTexts`, feature config `Audited().Versioned()`
- Contracts + Application: `AddTranslationCommand(LanguageId LanguageId, string Key, string Text)` (refused if the row exists) and `ChangeTranslationCommand(TranslationId Id, string Text)` (`RequireExpectedVersion()`), both `RequirePermissions(TranslationPermissions.Edit)` and both storing `SourceText` = the catalog default at save time; `DeleteTranslationCommand` (only an orphaned key; versioned), `GetTranslationListQueryCommand(LanguageId, Module?, Category?, Status?)` → `ListQueryResponse<GetTranslationListItem>` with `GetTranslationListItem(string Key, string Module, string Category, string Default, string? Text, TranslationStatus Status, Guid? TranslationId)` and `TranslationStatus` enum `Untranslated | Translated | NeedsReview | Orphaned`; progress query `GetLanguageProgress(LanguageId)` → `(int Translated, int Total)`
- Validators: key in catalog (Add/Change), text not empty, **placeholder set equals the default's** (code `Text`, message key `platform.translations.errors.placeholders-differ` with `{Expected}` argument)
- The list is merged in memory (catalog × the language's rows), then filtered, searched (`Term` over key, default, text) and paged by the handler; rows read by Dapper (`SELECT key, text, source_text, id FROM pf.translation WHERE language_id = @Id`)
- Infrastructure: `PlatformTranslationStore : ITranslationStore` (Dapper, `pf.language` + `pf.translation`), registered by `PlatformModule.AddServices` with `Replace`
- Handlers call `ITranslationCache.RefreshAsync(culture)` after a successful save
- Migration `AddTranslations` (unique index `ix_translation_language_id_key`, FK to `language` cascade)
- Test: `tests/.../Localization/TranslationTests.cs`

- [ ] **Step 1: Write the failing tests:**
  - `Add_ShowsOnNextRender` — published `de`, add `common.save` = `"Speichern"`, cookie `de`, prerender contains `Speichern`.
  - `Add_PlaceholderTypo_Refused` (Review Focus 2) — default `"Delete device {DeviceId}?"`, text `"Gerät {DeviceID} löschen?"` → validation error code `Text`.
  - `Change_StaleVersion_412` (Review Focus 3) — two loads, two changes; second → `VersionErrors.Stale`; stored text is the first.
  - `Status_NeedsReview_WhenDefaultChanged` — stored `SourceText` differs from catalog default → list item `NeedsReview`.
  - `Status_Orphaned_ForUnknownKey` — row with `platform.gone.key` inserted by SQL → `Orphaned`; `DeleteTranslation` succeeds; deleting a non-orphan is refused.
  - `List_FiltersByModuleAndStatus`, `Progress_CountsTranslated`.
  - `Edit_RequiresPermission`.
- [ ] **Step 2:** Run filter. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run filter. Expected: PASS.
- [ ] **Step 5:** Commit in Platform repo and bump pointer.

### Task 16: Admin pages

**Files (Platform `.Presentation.Blazor`, view/page split per root CLAUDE.md):**
- `Languages/Pages/LanguageListPage` `/pf/languages`, `LanguageCreatePage` `/pf/languages/new`, `LanguageDetailPage` `/pf/languages/{Id:guid}`; `Languages/Components/LanguageListView`, `LanguageCreateView`, `LanguageDetailView` (fields, progress overview card, actions Rename / Direction / Publish|Unpublish / Delete)
- `Translations/Pages/TranslationListPage` `/pf/languages/{LanguageId:guid}/translations`; `Translations/Components/TranslationListView` — `SerginAggregateList` with filter panel (module, category, status), quick search, columns key / default / translation, inline edit row command (a `MudTextField` + Save that sends Add or Change at the row's version), Delete for orphaned rows
- `PlatformNavigation` — entry "Languages" requiring `LanguagePermissions.Read`
- Test: `tests/.../Localization/PlatformPageRenderingTests.cs`

- [ ] **Step 1: Write the failing tests:** `LanguageList_Renders`, `LanguageDetail_ShowsProgress`, `TranslationList_Renders_WithStatusFilter`, `NavEntry_HiddenWithoutRead` (viewer user).
- [ ] **Step 2:** Run filter. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run filter and `Shell/` tests. Expected: PASS. Manually check one inline edit with the headless-browser recipe (memory `headless-browser-checks`) on port 5555.
- [ ] **Step 5:** Commit in Platform repo and bump pointer.

### Task 17: Draft preview policy

**Files:**
- Create: `src/Modules/Platform/Sergin.Platform.Infrastructure/Translations/TranslatorDraftPreviewPolicy.cs` — `: IDraftLanguagePreviewPolicy`, `IScopeAuthorization` on any scope for `TranslationPermissions.Edit`; registered by `PlatformModule.AddServices` with `Replace`
- Test: `tests/.../Localization/DraftPreviewTests.cs`

- [ ] **Step 1: Write the failing tests:** `Translator_SeesDraftInSwitcher_AndCanSwitch` (dev user, draft `de` → switcher lists it marked draft; cookie `de` honoured); `Viewer_DraftCookie_FallsBack` (seeded viewer, cookie `de` → default culture); `RevokedTranslator_FallsBackQuietly`.
- [ ] **Step 2:** Run filter. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run filter. Expected: PASS.
- [ ] **Step 5:** Commit in Platform repo and bump pointer.

---

## Phase 4 — Lock it in

### Task 18: Field guard on, pseudo-locale sweep, docs

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Hosts/Localizations/FieldTextGuard.cs` — at host start, for every request type in every local and remote `ContractsAssembly` that has a configuration, each public instance property not in `HiddenProperties` and not of a SharedKernel paging/criteria type (`Paggination`, `Sorting`, `Filtering`, `Term`) must have `FindField(CategoryOf(type), name)`; otherwise throw `InvalidOperationException` listing every `Type.Property` missing
- Create: `tests/.../Localization/PseudoLocaleSweepTests.cs` — fake store publishing culture `qps-ploc` with every catalog key translated as `⟦<default>⟧`; renders every parameterless module route plus one detail route per aggregate (seeded data); asserts every visible text node inside `<main>` and the app bar is either wrapped in `⟦⟧`, whitespace, or one of the seeded data values
- Modify: root `.claude/CLAUDE.md` (new "Localization" convention bullet; `ILocalizer` mentions replaced; Platform in the module list and three-repo → four-repo wording), SharedKernel and UserAccess `CLAUDE.md` where they mention `ILocalizer`; `/add-feature` and `/add-module` skills (declare `<Aggregate>Texts`, `builder.Texts(...)`, `NotShown`)
- Test: `tests/.../Localization/FieldTextGuardTests.cs`

- [ ] **Step 1: Write the failing tests:** `Guard_PassesForRealHost`; `Guard_RefusesUnlabelledProperty` (test command with an unlabelled `Extra` property via `ModuleConfigurationSource.FromTypes`) — message contains `Extra`; `Guard_HonoursNotShown`; the pseudo-locale sweep.
- [ ] **Step 2:** Run `--filter "FullyQualifiedName~Localization"`. Expected: FAIL (guard missing; sweep lists leftovers).
- [ ] **Step 3:** Implement the guard; fix every literal the sweep reports.
- [ ] **Step 4:** Run the **full suite** and `dotnet build Sergin.MeterMinder.slnx`. Expected: all PASS, 0 warnings.
- [ ] **Step 5:** Commit in each repo; run `graphify update .` and `python .claude/skills/graphify/scripts/graphify_repair.py` if the graph exists in the worktree; open PRs in SharedKernel, UserAccess and Platform, then the root PR that bumps all three pointers.
