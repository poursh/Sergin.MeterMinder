# UI texts live in the presentation layer

Date: 2026-11-07. Status: draft, for review.

## Intent

Today every text a module declares sits in its `.Application.Configurations` project (`DeviceTexts`, `ManufacturerTexts`, `UserTexts`, …). A usage scan of the nine module text classes shows that most of their members are read only by the Blazor RCL: page titles, "New …" captions, search placeholders, filter chips, delete confirms, overview figures, empty states, tooltips. No handler, validator, WebApi endpoint or gRPC service reads them. They are UI copy living in the Application layer, so every page tweak touches the Configurations project, and a non-UI host carries texts it never shows.

SharedKernel has the same split already half done: `MudBlazorTexts` lives in `Sergin.SharedKernel.Presentation.Blazor` and is registered by `AddSerginBlazorKit`, while `ShellTexts` (app bar, drawer, lists, pages, trees — read only by the Blazor kit) still lives in `Sergin.SharedKernel.Application`.

This design moves each text to the layer that reads it. The Blazor RCL declares its own texts; the Application layer keeps only the texts the Application layer (and the catalog machinery) needs.

## Goals

- A module's `.Presentation.Blazor` RCL declares the texts only its pages and components show, in `<Aggregate>UiTexts` classes next to the views that use them.
- `.Application.Configurations` keeps the texts that the application layer, the validators, the field-text guard, the permission pickers or the error rendering need.
- SharedKernel's `ShellTexts` moves to `Sergin.SharedKernel.Presentation.Blazor`, beside `MudBlazorTexts`.
- **No key changes.** Every key stays exactly as it is, so stored translations (the seeded `fa` texts, and anything a translator saved) keep working with no data migration.
- The catalog's existing start-up refusals (duplicate key, one key with two defaults, malformed key, key outside its module) cover the new sources unchanged.

## Non-goals

- No text classes for `.Presentation.WebApi` or the gRPC projects. They show no copy of their own today; problem details render through `ErrorTexts` and the module's error keys, which stay in Application. Empty classes would be ceremony.
- `CommonTexts` (`common.actions.*` and friends) is not split in this round. It mixes UI verbs with texts Application reads (`PermissionGroupOther`, read by `PermissionCatalog`). Splitting it is a possible follow-up under the same rule.
- No change to `FieldText`, `TextCategory`, `TextDefinition`, the lookup (`IStringLocalizer`), culture handling or the Settings module's data model.
- No remote-module UI. `UiAssembly` is on `ISerginWebUiModule`, which only local modules implement; a remote module with pages does not exist and is out of scope.

## Anti-goals

- Changing a key, even to add a `ui` segment. That would orphan every stored translation of it.
- A text declared twice (once per layer) "for convenience". The catalog refuses one key from two declaring types, and that refusal must stay.
- Making the RCL a second home for field labels. Fields belong to commands and list queries; see the placement rule.

## The placement rule

A text stays in `.Application.Configurations` when any of these holds:

1. It is the aggregate's `TextCategory` (`builder.Texts(...)` takes it; the RCL reuses it).
2. It is a `FieldText`. The field-text guard checks command and list-query properties against them, FluentValidation messages use their labels, and the Blazor forms reuse them by reference. A field's `placeholder:` stays on the field: it is UI-shaped, but splitting one field across two layers is worse.
3. It is `Singular` (or any text passed to `builder.Named(...)`). `MustExistIn` and the permission-picker headings read it.
4. It is an error text: a key a domain `ErrorText.Of(...)` names, or a message a validator or handler produces (`InUse`, `NameInUse`, `UnknownCulture`, `PlaceholdersDiffer`, …). WebApi problem details render these too.
5. Code in `.Application`, `.Application.Configurations` or `.Infrastructure` reads it.

Everything else moves to the RCL: `Title`, `New`, `EditTitle`, `RenameTitle`, search and filter placeholders, filter chips and date-range formats, delete/remove/deactivate titles and confirms (single and many), overview and summary figures, empty states, column headers, tooltips, "saved" and "changed meanwhile" notices, status labels shown only in the UI (`Draft`, `Published`, `LeftToRight`, …), and a module's menu title (`DeviceManagementTexts.MenuTitle`).

`Title` is the one judgement call: today only the drawer, breadcrumbs and `<PageTitle>` read it, so it moves. If an API host later wants it (an OpenAPI tag, say), it moves back. **Because keys never change, moving a text between layers is free and reversible** — no translation is touched. The rule can be applied by reading code, not by guessing future use.

## Shape

### Module RCL

One public static class per aggregate (and per child entity) that has UI-only texts, in the aggregate's folder of the RCL, beside `Pages/`, `Components/` and `Models/`:

```
Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/
  Devices/DeviceUiTexts.cs
  Manufacturers/ManufacturerUiTexts.cs
  Manufacturers/DeviceModels/DeviceModelUiTexts.cs
  DeviceManagementUiTexts.cs          // MenuTitle
```

The class builds on the Application category, so its keys are the keys that exist today:

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices;

/// <summary>The device texts only the Blazor pages show. Fields, the name and the errors are in DeviceTexts.</summary>
public static class DeviceUiTexts
{
    private static readonly TextCategory category = DeviceTexts.Category;

    public static readonly TextDefinition Title = category.Text("Title", "Devices");

    public static readonly TextDefinition New = category.Text("New", "New device");

    public static readonly TextDefinition DeleteConfirm =
        category.Text("DeleteConfirm", "Delete {DeviceId}? It disappears from every list and page.");

    // …
}
```

Each member moves verbatim: same name argument, same English default, same description argument. The catalog then produces the same key (`device-management.devices.delete-confirm`) from a different declaring type. The member is deleted from `DeviceTexts` in the same commit — keeping both is a duplicate-key refusal at host start.

Call sites in the RCL change from `DeviceTexts.New` to `DeviceUiTexts.New`. Field references (`DeviceTexts.DeviceId`) and `DeviceTexts.Singular` stay as they are. The RCL already references `.Application.Configurations`, so no project reference is added.

The class must be `public static`: `TextSource` collects texts only from public static classes (nested ones included), so an `internal` class would be skipped silently and its texts would render as raw keys.

`DeviceManagementTexts` holds only its `Category` and `MenuTitle` (the drawer group title), and nothing outside the RCL reads either. It moves whole, as `DeviceManagementUiTexts` in the RCL root, with its own `TextCategory.Create("DeviceManagement")`; `.Application.Configurations` then has no module-level text class. A module-level class that also holds texts Application reads stays and is split by the rule like any other.

### Registration

`AddSerginBlazorApp` (in `Sergin.SharedKernel.Hosts.WebUi`) registers one more `TextSource` per UI module, beside the one `AddSerginCore` already registers from the module's `ConfigurationsAssembly`:

```csharp
foreach (ISerginWebUiModule module in modules.OfType<ISerginWebUiModule>())
{
    builder.Services.AddSingleton(TextSource.FromAssembly(module.UiAssembly, module.Name));
}
```

`TextCatalog` already takes every registered `TextSource`, so nothing else changes. Same module name, so the module-segment check applies: an RCL cannot declare a key outside its module. Scanning the RCL runs the static constructors of its public static classes (`<Module>Navigation` among them); that is harmless, and those classes declare no texts so they contribute nothing.

`UiAssembly` is the assembly the shell already maps routes from, so a module cannot forget to register its UI texts: shipping pages means shipping the texts with them.

### SharedKernel

`ShellTexts` moves from `Sergin.SharedKernel.Application/Localizations/Common/` to `Sergin.SharedKernel.Presentation.Blazor/Localizations/`, keys unchanged (`common.*`). `AddSerginBlazorKit` registers it the way it registers `MudBlazorTexts.Source`: `services.AddSingleton(TextSource.FromTypes("common", typeof(ShellTexts)))`. `TextSource.Common` keeps scanning `Sergin.SharedKernel.Application` and so no longer finds it. The scan for this design found `ShellTexts` read only by `Sergin.SharedKernel.Presentation.Blazor` and by this repo's `Components/MeterMinderHome.razor` (`ShellTexts.Welcome.Prompt`), which is Blazor too. The namespace changes, so the host's `_Imports.razor` picks up the new one in step 4. Its keys are `common.*`, so registering it separately from `TextSource.Common` keeps it inside the `common` module segment.

`TextSource.FromAssembly`/`FromTypes` are reused as they are; no new SharedKernel API is needed beyond the registration loop.

## Consequences

- **A host with no UI has no UI texts in its catalog.** That is the point, with one side effect: the Settings module marks a stored translation whose key is not in the catalog as "Orphaned". If Settings ever ran in a host without the Blazor RCLs, every UI translation would show as orphaned there. There is no such host today (Settings' pages run in the UI host, whose catalog has everything). Recorded so a future API host does not delete them through an orphan clean-up.
- **The pseudo-locale sweep is unaffected**: it renders pages and checks the output, and the moved texts are still in the catalog.
- **`ModuleConfigurationsAssemblyTests`** pins the public surface of each Configurations assembly (marker, `<Aggregate>Permissions`, `<Aggregate>Texts`). The aggregate text classes shrink but none disappear (each keeps its category, fields, `Singular` and errors). `DeviceManagementTexts` does disappear; check whether that test names it, and drop it from the expected surface if so.
- **Developer cost**: two text classes per aggregate, and an author must pick one. The placement rule above is the answer; `/add-feature`, `/add-module` and the CLAUDE.md "Localization" bullet say it in one sentence each.

## Guard test

One new integration test, `Localization/UiTextPlacementTests`, over every `ISerginWebUiModule.UiAssembly` of the real host (and SharedKernel's Blazor assembly):

- no public static text class in a UI assembly declares a `FieldText` or an `ErrorTextPair` — fields and error texts belong in Application (rules 2 and 4);
- every key a UI assembly declares is in the host's `TextCatalog` (proves the registration loop).

Rules 3 and 5 (who reads a text) are not mechanically testable; they are left to review.

## Rollout

Four repos, in this order:

1. **Sergin.SharedKernel** — the registration loop in `AddSerginBlazorApp`; move `ShellTexts` and register it from `AddSerginBlazorKit`. Additive for the modules: until a module moves a text, its RCL declares none and the new source is empty.
2. **Sergin.Administration.UserAccess** — `UserUiTexts`, `RoleUiTexts`, `ScopeUiTexts`; call sites; its CLAUDE.md.
3. **Sergin.Administration.Settings** — `LanguageUiTexts`, `TranslationUiTexts`; call sites; its CLAUDE.md.
4. **Sergin.MeterMinder** — DeviceManagement (`DeviceUiTexts`, `ManufacturerUiTexts`, `DeviceModelUiTexts`, `DeviceManagementUiTexts`), the guard test, submodule bumps, root CLAUDE.md ("Localization" > Declare), the `/add-feature` and `/add-module` skills.

Steps 2–4 are independent of each other once step 1 has merged. Each module step is mechanical: move members, fix call sites, build (warnings are errors), run the localization tests and the pseudo-locale sweep. The host build is the check that nothing is left behind: a member deleted from `<Aggregate>Texts` and still referenced fails to compile; a member left in both fails host start.

## Left to the builder

- Exact member lists per class, applying the placement rule. The usage scan behind this design (which project references each member) is the starting point; a member with no direct reference (an error text named by a domain `ErrorKeys` string) stays in Application.
- Whether a child entity with only one or two UI texts gets its own `<Child>UiTexts` class or none (prefer its own class, matching the Application side).
- XML doc wording on the new classes.
- Order of commits within each repo.
