# Entity command types — design

Date: 2026-11-10. Status: draft for approval.

## Intent

A command or query today says nothing about *what it acts on*. Whether `DeleteDeviceCommand` targets one
device, whether `RemoveDeviceModelCommand` targets a child of a manufacturer, whether a command may run on a
multi-selection — all of that lives in each view's hand-written code and in each command configuration's
hand-written `RequireExpectedVersion()`.

This design gives every request record a **contract-level type**: an interface on the record in
`.Application.Contracts` that names the aggregate (and child entity) it targets, fixes the shape of its target
ids, and says what kind of effect it has. The point is a later step, out of scope here: **building UI from the
types** — a view that names its aggregate and finds and places every command by type (Create in the header,
instance commands in the row menu and detail actions, selectable ones in the selection bar), with permission
hiding and version handling derived instead of wired by hand. Generating forms from the types is a further,
separate step.

Within this work the types already pay for themselves in the pipeline: the aggregate binding and the version
rule are derived from the type instead of declared per configuration.

## Goals

- One set of target and effect interfaces in SharedKernel that covers every existing request record in `dm`,
  `ua` and `st`, plus the types no slice uses yet (defined, not used).
- Every existing request record moved onto them, in all four repos, including the renames the forced shape
  requires.
- The aggregate binding comes from the type; `ICommandConfiguration<TRecord, TAggregateRoot>` is removed.
- The expected-version check is **on by default** for every command that targets an existing entity of a
  `Versioned()` root, with an explicit opt-out.
- Host start refuses a record whose type is inconsistent with the model (see "Guards").

## Non-goals

- Building UI from the types (the next design).
- Generating forms from the types.
- Job-level / background commands. A criteria command runs while the user waits; the job design comes later.
- Real slices for the unused types (criteria, import, clone, restore, purge, reorder, export, `ActivateUser`).
  Their interfaces are defined, together with the **host-start shape guards** that apply to them (e.g.
  restore/purge only on a `SoftDeletable()` root). Their **execution rules** — criteria match resolution, the
  `ExpectedCount` check, the all-or-nothing save, the soft-delete bypass for restore, per-item import
  validation — are written down in this document as the contract the first real slice must honour, and are
  **not** built now. *(Designer's call, reading the user's "define only their interfaces".)*
- A server-side selection command. Selection stays one instance command per item.

## Anti-goals

- A type that only labels. Each interface must either force a shape (member names, id types) or drive a
  derived rule; a marker nothing reads does not ship.
- Two sources of truth. Once the type names the aggregate, no configuration names it again.
- Weakening a guard. Every command that is version-guarded today stays guarded after the move.

## Constraints

- `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer: generic marker interfaces with unused type
  parameters trip S2326 — the existing `ICommandConfiguration<TCommand, TAggregateRoot>` already carries a scoped
  `#pragma warning disable S2326`; follow that precedent.
- `.Application.Contracts` references only `SharedKernel.Application` and the module's `.Domain`, so typed ids
  and aggregate types are available to the records, and the new interfaces live in
  `Sergin.SharedKernel.Application` (namespace `Sergin.SharedKernel.Application.Commands.Types`, builder's call).
- `IAggregateRoot<out TId>` exists in `SharedKernel.Domain`; child entities derive `Entity<TId>`. The interfaces
  constrain on these so a wrong id type is a compile error, not a startup one.
- SharedKernel, UserAccess and Settings are submodules: their changes are PRs in their own repos, merged in order
  SharedKernel → UserAccess, Settings → this repo (dm + submodule bumps).
- `FieldTextGuard` keys field texts by property name. Renaming a shown property (e.g. `Permission` → `Value`)
  renames its text key, which orphans existing translations (`dev-seed.sql`'s `fa` rows). See "Renames".

## Decisions (made with the user)

1. Types are contract-level, on the request record.
2. A type may force a fixed shape, renaming fields in existing records.
3. Selection: each item succeeds or fails on its own. No server-side selection command.
4. Criteria commands run while the user waits. Job-level commands are a later design.
5. Unused types: interfaces only.
6. Move every existing record now, all four repos.
7. Expected version on by default for commands on an existing entity of a versioned root; opt-out explicit.

## The interfaces

Two layers. **Target interfaces** carry the ids and name the aggregate. **Effect interfaces** derive from a
target interface and say what the command does. A record implements exactly one effect interface (or
`ISystemCommand`), plus `ICommand<TResponse>` / `IQuery<TResponse>` as today.

### Target interfaces

| Interface | Members | Meaning |
|---|---|---|
| `IEntityTarget<TRoot, TId>` where `TRoot : IAggregateRoot<TId>` | `TId Id` | acts on one existing root |
| `IChildEntityTarget<TRoot, TRootId, TChild, TChildId>` where `TChild : IEntity<TChildId>` | `TRootId RootId`, `TChildId Id` | acts on one existing child of a root |
| `IRootTarget<TRoot, TRootId>` (used by add-child) | `TRootId RootId` | acts on a root's child collection, child not yet existing |
| `ICollectionTarget<TRoot>` | — | acts on the aggregate's collection in the current scope (creation, criteria, collection queries) |
| `IUnderParent<TParent, TParentId>` (combinable with `ICollectionTarget`) | `TParentId ParentId` | the new or listed roots sit under a parent — the same aggregate (`CreateScope`) or another (`AddTranslation`, `CreateRole`'s owner scope) |

A `ParentId` is a reference, not a target: it carries no version rule.

### Effect interfaces — commands

| Interface | Target | Notes |
|---|---|---|
| `ICreateCommand<TRoot, TId>` | collection (± `IUnderParent`) | response implements `ICreatedResponse` (`Guid Id`), so a UI can navigate to the new record |
| `IAddChildCommand<TRoot, TRootId, TChild, TChildId>` | root | response implements `ICreatedResponse` |
| `IUpdateCommand<…>` | entity or child | edits several fields at once (a form) |
| `ISetPropertyCommand<…>` | entity or child | exactly one shown property besides the target ids — that property is the value; derived, no declaration |
| `ITransitionCommand<…>` | entity or child | no shown property besides the ids; a pair declares `IReverseOf<TOther>` on one side (Unpublish reverses Publish) |
| `IMoveCommand<TRoot, TId>` | entity | `TId NewParentId` — the root moves within its own tree |
| `IDeleteCommand<…>` | entity | extra shown properties allowed (`Cascade`), asked for in the confirm |
| `IRemoveChildCommand<…>` | child | as delete, for a child |
| `IAddValueCommand<TRoot, TId, TValue>` / `IRemoveValueCommand<…>` | entity | a value in the root's own collection, not an entity; like set-property, the value is the one shown non-id property of type `TValue` (its name is free, so `Permission` keeps its name and its text key) |
| `ISystemCommand` | — | no UI, no target rule (`ProvisionExternalUser`) |
| `ISelectable` | (marker, combinable) | the command may run once per selected row; allowed only on entity/child targets |

Unused — defined, no record implements them yet:

| Interface | Shape | Implied rules |
|---|---|---|
| `ICriteriaCommand<TRoot, TListQuery>` | `Term? Term`, `Filtering? Filtering`, `int ExpectedCount` | matches resolved through the list query's `ListFieldSet` and `ListQuerySql` (same meaning as the list the user saw), scope and soft-delete filters apply, permission checked per matched record's scope, refused when the match count ≠ `ExpectedCount`, one save, all or nothing, no per-item version |
| `IImportCommand<TRoot, TItem>` | collection; `IReadOnlyList<TItem> Items` | creation, N items; each item validated as one Create would be |
| `ICloneCommand<TRoot, TId>` | entity (source); response `ICreatedResponse` | creation from a source; source not version-checked |
| `IRestoreCommand<TRoot, TId>` / `IPurgeCommand<TRoot, TId>` | entity | root must be `SoftDeletable()` (host-start guard); the target is a deleted row, so the soft-delete filter is bypassed for the lookup |
| `IReorderCommand<TRoot, TRootId, TChild, TChildId>` | child; `int Position` | |
| `IExportQuery<TRoot, TListQuery>` | collection; list criteria, no paging | |

`ActivateUser` would be an `ITransitionCommand` with `IReverseOf<DeactivateUserCommand>`; it is not built.

### Effect interfaces — queries

| Interface | Target | Existing records |
|---|---|---|
| `IGetOneQuery<TRoot, TId>` | entity | 7 `Get<X>ByIdQueryCommand` |
| `IChildGetOneQuery<…>` | child | `GetDeviceModelById` |
| `IAuditQuery<TRoot, TId>` | entity | Device, Manufacturer, Language audit |
| `IListOf<TRoot>` (on the existing `ListQuery<TItem>` records) | collection (± `IUnderParent`) | Device, Manufacturer, Language, User; Translation under Language |
| `IChildListOf<TRoot, TRootId, TChild>` | root | `GetDeviceModelList` (`ManufacturerId` → `RootId`), `GetUserAssignments` (unpaged) |
| `ITreeQuery<TRoot>` | collection | `GetScopeTree` |
| `IOverviewQuery<TRoot>` / `IEntityOverviewQuery<TRoot, TId>` | collection / entity | `GetDeviceOverview` / `GetManufacturerOverview` |
| `IEntityViewQuery<TRoot, TId>` | entity | `GetLanguageProgress`, `GetUserEffectivePermissions` |

A query may stay untyped (a plain `IQuery<T>`) — `GetAssignableRoles`, a picker, is the one today. A command
may not: every `ICommand` implements an effect interface or `ISystemCommand`.

There is no separate "relation" interface: `AssignRole` is an add-child on `User` (its `RoleAssignment` child)
with plain reference fields; `RevokeAssignment` is its remove-child.

`IUnderParent` is for a structural parent only — the new record sits *under* it (a scope under a scope, a
translation under a language). A record that merely references another aggregate keeps a plain field:
`CreateRole.OwnerScopeId` stays as it is and `CreateRole` is a bare `ICreateCommand`. *(Designer's call.)*

`ICreatedResponse.Id` is a `Guid`, not the typed id, matching every query response today (they keep `Guid`
ids), the WebApi JSON shape, and every `$"/dm/devices/{response.Id}"` in the views. Typed ids are a
request-side change only. *(Designer's call.)*

## Derived rules

- **Where the type is read.** The derived rules come from the record's type whether or not it has a
  configuration, so `AddSerginCore` scans each module's `ContractsAssembly` (local and remote) for typed
  records, alongside today's `ConfigurationsAssembly` scan; an unconfigured typed record gets its root and
  version rule from the type and no permissions. The "every command is typed" guard and the shape guards run
  over Contracts assemblies only — test-only records registered through `ModuleConfigurationSource.FromTypes`
  are outside them and are not forced to take a type, though a test may give one. The `DbContext` mapping and
  child-reachability guards run for local modules only; a gateway host has no `DbContext` for a remote module,
  and the remote server host runs them there. *(Designer's call.)*
- **Aggregate binding.** The pipeline reads `TRoot` from the record's type. `ICommandConfiguration<TRecord,
  TAggregateRoot>` is deleted; every configuration becomes `ICommandConfiguration<TRecord>`. The permission scope
  rule is unchanged in meaning: a record whose `TRoot` is `Scoped()` is checked on the current scope, otherwise on
  any scope; an untyped query is checked on the current scope.
- **Expected version (decision 7).** A command whose target is an existing entity, child, or a root's child
  collection (`IEntityTarget`, `IChildEntityTarget`, `IRootTarget`) and whose `TRoot` is `Versioned()` requires
  an expected version — the root's version for a child. Creation, criteria, `ICloneCommand`'s source and
  `IUnderParent` references carry none. Opt-out: `builder.WithoutExpectedVersion()` in the configuration;
  `RequireExpectedVersion()` is removed. Queries are never guarded.
- **Selection.** `ISelectable` commands run through `SerginBulk` once per item, each with its own version; a
  child command chains the root's version from item to item (today's device-model Remove). Unchanged mechanics;
  the marker is what a future view reads.

## Guards (host start)

Added to the existing command-configuration / field-text startup checks, each naming the offending type:

- a command implements no effect interface and is not `ISystemCommand`, or implements two;
- a typed record's `TRoot` is not mapped by the module's `DbContext`, or `TChild` is not reachable from `TRoot`
  through the aggregate's EF navigations;
- `ISetPropertyCommand` with other than exactly one shown non-id property; `ITransitionCommand` with any;
- `ISelectable` on a non-entity target; `IRestoreCommand`/`IPurgeCommand` on a root that is not `SoftDeletable()`;
- `WithoutExpectedVersion()` on a command that would not require a version anyway (a dead opt-out).

`FieldTextGuard` treats `Id`, `RootId` and `ExpectedCount` from the target interfaces as not shown, so
configurations drop their `NotShown(x => x.Id)` lines for those. `ParentId` and `NewParentId` stay **shown**
when the record shows them today — `ScopeTexts.ParentId` ("Parent scope") and `ScopeTexts.NewParentId`
("Move under", the move picker's label) keep their declarations, keys and `fa` rows; `AddTranslation` keeps
marking its parent id `NotShown`.

## Migration of existing records

| Record | Type | Renames |
|---|---|---|
| `CreateDevice` | `ICreateCommand<Device, DeviceIntenralId>` (current scope) | response implements `ICreatedResponse` (stays `Guid Id`) |
| `CreateManufacturer`, `CreateUser`, `AddLanguage`, and every add-child response | `ICreateCommand` | as above |
| `CreateRole` | `ICreateCommand<Role,…>` | none — `OwnerScopeId` stays a plain reference |
| `CreateScope` | `ICreateCommand<Scope,…>` + `IUnderParent<Scope, ScopeId>` | `Guid ParentId` → `ScopeId` (name and text key unchanged) |
| `AddTranslation` | `ICreateCommand<Translation,…>` + `IUnderParent<Language, LanguageId>` | `LanguageId` → `ParentId` (not shown; `TranslationTexts.LanguageId` stays declared for the list) |
| `AddDeviceModel` | `IAddChildCommand<Manufacturer,…, DeviceModel,…>` | `ManufacturerId` → `RootId` |
| `AssignRole` | `IAddChildCommand<User,…, RoleAssignment,…>` | `UserId` → `RootId`; role/scope ids stay plain references |
| `UpdateDevice`, `UpdateManufacturer` | `IUpdateCommand` | `Guid Id` → typed |
| `RenameLanguage`, `RenameRole`, `RenameScope`, `ChangeTranslation`, `SetLanguageDirection`, `SetRoleInheritable` | `ISetPropertyCommand` | `Guid Id` → typed |
| `RenameDeviceModel` | `ISetPropertyCommand` (child) | `ManufacturerId` → `RootId`, `Guid Id` → `DeviceModelInternalId` |
| `PublishLanguage` / `UnpublishLanguage` (`IReverseOf<Publish…>`), `DeactivateUser` | `ITransitionCommand` | `Guid Id` → typed |
| `MoveScope` | `IMoveCommand<Scope, ScopeId>` + `WithoutExpectedVersion()` | ids typed |
| `DeleteDevice`, `DeleteManufacturer`, `DeleteLanguage`, `DeleteTranslation` | `IDeleteCommand` + `ISelectable` where a list offers it today | `Guid Id` → typed |
| `DeleteRole`, `DeleteScope` (`Cascade`) | `IDeleteCommand`; `DeleteScope` + `WithoutExpectedVersion()` | ids typed |
| `RemoveDeviceModel` | `IRemoveChildCommand` + `ISelectable` | `ManufacturerId` → `RootId`, id typed |
| `RevokeAssignment` | `IRemoveChildCommand<User,…, RoleAssignment,…>` | `UserId` → `RootId`, `AssignmentId` → `Id` |
| `GrantRolePermission` / `RevokeRolePermission` | `IAddValueCommand` / `IRemoveValueCommand<Role, RoleId, Permission>` | `Guid Id` → `RoleId`; `Permission` keeps its name |
| `ProvisionExternalUser` | `ISystemCommand` | none |
| every query | per the query table | `Guid Id` → typed; child list `ManufacturerId` → `RootId`; translation list `LanguageId` → `ParentId` |

Version rule check: every command guarded today maps to a type that requires the version by default; the two
deliberately unguarded ones (`MoveScope`, `DeleteScope`) get the explicit opt-out; no command gains a guard,
since every other default-guarded target's root (`User`) is not `Versioned()`. A test pins that the set of
guarded commands is identical before and after.

### Renames — knock-on work

- **Text keys.** A renamed *shown* property would rename its `FieldText` key and orphan its translations.
  The table above renames **no shown property**: every rename is an id the guard hides (`Id`, `RootId`) or a
  parent id that is already `NotShown` (`AddTranslation`). So no text key changes and `dev-seed.sql`'s `fa`
  rows stay as they are. A test pins that the set of catalog keys is identical before and after.
- **Callers.** Handlers, validators (`OverridePropertyName(nameof(...))`), WebApi endpoints and request DTOs,
  Blazor views' `ToCommand()`, the gRPC server/invoker for `GetDeviceById` (proto field stays a string GUID;
  mapping changes only), and integration tests all follow the renames. Form-model property names follow too,
  where the form models carry the renamed property (`ToCommand` must keep form and command names equal).
- **Typed ids in Contracts.** `Guid Id` → typed id on every record; WebApi endpoints keep `Guid` route
  parameters and construct the typed id.

## Order of work

1. SharedKernel PR: the interfaces, `ICreatedResponse`, derived aggregate binding and version rule,
   `WithoutExpectedVersion()`, removal of the bound configuration form and `RequireExpectedVersion()`, the
   guards, `FieldTextGuard` change, and tests using test-only records (as the existing pipeline tests do) —
   including one test-only record per unused interface, proving its shape guards only, not its execution rules.
2. UserAccess PR and Settings PR: move their records.
3. This repo: move `dm`, bump the three submodules, update CLAUDE.md files (the
   `RequireExpectedVersion()` and bound-configuration prose in all four).

Steps 1–3 cannot each build alone against `main` of the others (removing `RequireExpectedVersion()` breaks every
module), so they land together: develop on matching branches in a worktree and merge the four in one sitting,
SharedKernel first.

## Left to the builder

- Exact namespaces and file layout inside SharedKernel.
- Whether effect interfaces are generic over the target interface or each restates the target generics (the
  tables above show intent, not the final generic signatures).
- How the guards are split across existing guard classes (`CommandConfigurationRegistry`, `FieldTextGuard`,
  `AggregateFeatureGuard`) versus a new one.
