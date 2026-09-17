# Child-entity folders nested under their aggregate

## Motivation

`DeviceModel` is an entity inside the `Manufacturer` aggregate (`2026-09-16-device-models-design.md`).
That design put every model file *flat* in each layer's `Manufacturers/` folder, told apart only by
name: `Manufacturers/Commands/{AddDeviceModel,GetDeviceModel,GetDeviceModelList}/` in Application and
Contracts, `Manufacturers/Pages/{AddDeviceModelPage,DeviceModelDetailPage}` in Blazor,
`Manufacturers/Converters/DeviceModel*Converter.cs` in Infrastructure.Data, and the two model reads
inside `ManufacturerQueryRepository`. It said "no `DeviceModels` folder anywhere" on purpose: a
*top-level* `DeviceModels/` sibling of `Manufacturers/` would read as a second aggregate, and the whole
point of that design was that a model is not one.

A *nested* folder says the opposite. `Manufacturers/DeviceModels/` reads as "inside Manufacturers" —
the aggregate boundary becomes visible on disk instead of living only in the type names and the
module `CLAUDE.md`. The device-models spec's own motivation says models will carry capabilities,
registers and protocols, which means more slices on the same entity; a flat `Manufacturers/` folder
only gets noisier as they land. Doing the move now, with six slices and two pages, is cheaper than
doing it with twenty.

Blazor gets one more thing from the same change: a home for components that are not pages. Every
`.razor` in a module RCL today is a routable page; the manufacturer detail page grew a models table
inline because there was nowhere else to put it. A `Components/` folder next to `Pages/` and
`Models/` fixes that, and the models table is its first occupant.

## Scope

In:

- The rule: a child entity's files live in `<Aggregate>/<ChildEntity>/` in **every** layer, mirroring
  the aggregate's own layout one level down.
- Moving every `DeviceModel` file into `Manufacturers/DeviceModels/` across Domain, Application.Contracts,
  Application, Infrastructure, Infrastructure.Data, Presentation.WebApi, Presentation.Blazor and the
  integration tests; namespaces follow.
- Normalising the nested feature-folder names to the aggregate vocabulary (`Add`, `GetOne`, `GetList`).
- Splitting the model reads out of `ManufacturerQueryRepository` into their own `DeviceModelQueryRepository`.
- A `Components/` convention for module RCLs, with `DeviceModelTable` extracted from `ManufacturerDetailPage`
  as the first instance.
- Regenerating the EF model snapshot, whose entity names are CLR full names.
- Root and module `CLAUDE.md`, the `/add-feature` and `/add-module` skills.

Out:

- Any type rename. `AddDeviceModelCommand`, `GetDeviceModelByIdQueryCommand`, `DeviceModelQueryResponse`,
  `GetDeviceModelListItem`, `NewDeviceModelFormModel` and the rest keep their names; only folders and
  namespaces move.
- Routes, SQL, permissions, the aggregate's behaviour, the write-side repository shape. `Manufacturer.AddModel`
  is still the only way a model comes into being and `ManufacturerRepository` is still the only write path.
- UserAccess and SharedKernel. UserAccess has no child entity with slices of its own (`Role` and `User` are
  separate aggregates; `User.Roles` and `Role.Permissions` are owned value collections), and SharedKernel's
  Blazor project is organised by concern (`Errors/`, `Layout/`, `Home/`), not by aggregate. No submodule PR.
- The gRPC trio, which carries Devices only.
- A `DeviceModelInstallationExtensions`. One installation class per aggregate stays the rule; the child's
  registrations live in `ManufacturerInstallationExtensions`.

## The rule

**A child entity mirrors its aggregate's layout one level down.** Wherever an aggregate has
`<Aggregate>/X/`, a child entity of that aggregate has `<Aggregate>/<ChildEntity>/X/` — `Commands/<Feature>/`
in Application and Contracts, `Endpoints/<Feature>/` in WebApi, `Models/` + `Pages/` + `Components/` in
Blazor, `Converters/` next to the entity's `IEntityTypeConfiguration` in Infrastructure.Data,
`Repositories/Queries/` in Infrastructure. Namespaces follow folders, as they do everywhere else in the
repo (`dotnet_style_namespace_match_folder` is a suggestion, not a build error, so this is convention,
not enforcement).

**Feature folders use the aggregate vocabulary.** The nested folder already names the entity, so the
feature folder does not repeat it: `DeviceModels/Commands/Add/`, not `DeviceModels/Commands/AddDeviceModel/`;
`GetOne/` and `GetList/` exactly as under `Devices/` and `Manufacturers/`. The *type* inside still carries
the entity name (`AddDeviceModelCommand`) because type names are assembly-wide and the aggregate's own
`GetOne/` folder holds a `GetManufacturerByIdQueryCommand`.

**Reads are per entity, writes are per aggregate.** A child entity gets its own query repository
(`DeviceModelQueryRepository`, implementing an `IDeviceModelAllQueryRepository` that joins its
per-feature query interfaces) under `<Aggregate>/<ChildEntity>/Repositories/Queries/`, because the read
side is raw SQL over a table and owes nothing to the aggregate boundary. It never gets a write
repository: the only write path is the root's behaviour through the root's repository, and that is
what keeps the invariant (`Manufacturer.AddModel` refusing a duplicate name) in one place.

**`Components/` holds non-routable components.** A `.razor` with an `@page` directive goes in `Pages/`;
one without goes in `Components/`. Same `.razor` + `.razor.cs` split, same `public sealed partial class`
code-behind, same `[Inject]` properties — the difference is `[Parameter]`s instead of route parameters.
`_Imports.razor` carries each `Components` namespace so markup can use the tag name bare.

**One installation class per aggregate.** The child's DI registrations and endpoint mappings sit in the
aggregate's `<Aggregate>InstallationExtensions`; the composition root gets no nested folder. Registration
is one place per aggregate and the child is part of it.

## Layout

`N` is `Sergin.MeterMinder.DeviceManagement`. Every move is a `git mv`.

| Layer | Before | After | Namespace |
|---|---|---|---|
| `.Domain` | `Manufacturers/DeviceModel.cs` | `Manufacturers/DeviceModels/DeviceModel.cs` (still one file holding `DeviceModel`, `DeviceModelInternalId`, `DeviceModelName`) | `N.Domain.Manufacturers.DeviceModels` |
| `.Application.Contracts` | `Manufacturers/Commands/{AddDeviceModel,GetDeviceModel,GetDeviceModelList}/` | `Manufacturers/DeviceModels/Commands/{Add,GetOne,GetList}/` | `N.Application.Manufacturers.DeviceModels.Commands.{Add,GetOne,GetList}` |
| `.Application` | same three folders; `IManufacturerAllQueryRepository` joined four interfaces | same three moves; new `Manufacturers/DeviceModels/IDeviceModelAllQueryRepository.cs` joins the two model interfaces; `IManufacturerAllQueryRepository` keeps the two manufacturer ones | `N.Application.Manufacturers.DeviceModels[.Commands.*]` |
| `.Infrastructure` | model reads inside `Manufacturers/Repositories/Queries/ManufacturerQueryRepository.cs` | new `Manufacturers/DeviceModels/Repositories/Queries/DeviceModelQueryRepository.cs` with those two reads, verbatim | `N.Infrastructure.Manufacturers.DeviceModels.Repositories.Queries` |
| `.Infrastructure.Data` | `Manufacturers/DeviceModelEntityTypeConfiguration.cs`, `Manufacturers/Converters/DeviceModel{InternalId,Name}Converter.cs` | `Manufacturers/DeviceModels/DeviceModelEntityTypeConfiguration.cs`, `Manufacturers/DeviceModels/Converters/…` | `N.Infrastructure.Data.Manufacturers.DeviceModels[.Converters]` |
| `.Presentation.WebApi` | `Manufacturers/Endpoints/{AddDeviceModel,GetDeviceModel,GetDeviceModelList}/` | `Manufacturers/DeviceModels/Endpoints/{Add,GetOne,GetList}/` | `N.Presentation.WebApi.Manufacturers.DeviceModels.Endpoints.{Add,GetOne,GetList}` |
| `.Presentation.Blazor` | `Manufacturers/Models/NewDeviceModelFormModel.cs`, `Manufacturers/Pages/{AddDeviceModelPage,DeviceModelDetailPage}` | `Manufacturers/DeviceModels/Models/…`, `Manufacturers/DeviceModels/Pages/…`, new `Manufacturers/DeviceModels/Components/DeviceModelTable` | `N.Presentation.Blazor.Manufacturers.DeviceModels.{Models,Pages,Components}` |
| composition root | `Manufacturers/ManufacturerInstallationExtensions.cs` | unchanged file; the three model query registrations point at `DeviceModelQueryRepository` | — |
| tests | `Manufacturers/DeviceModelTests.cs` | `Manufacturers/DeviceModels/DeviceModelTests.cs` | `Sergin.MeterMinder.IntegrationTests.All.Manufacturers.DeviceModels` |

One naming wrinkle is accepted rather than avoided: in the Blazor RCL, `Manufacturers/Models/` (form
models — `NewManufacturerFormModel`) now sits beside `Manufacturers/DeviceModels/` (the entity). `Models/`
means "form models" in every RCL folder, and `DeviceModels/` is the entity's name; renaming the former to
dodge the latter would break the `Models/` convention across both modules for one aggregate's benefit.

## `DeviceModelTable`

`ManufacturerDetailPage` today renders the manufacturer card, then a "Models" heading with a "New model"
button and a `MudTable<GetDeviceModelListItem>` on `ServerData`. The heading, button and table move
verbatim into `Manufacturers/DeviceModels/Components/DeviceModelTable.razor`; `LoadModelsAsync` (with
its `state.Page + 1` comment) and `OpenModel` move verbatim into the code-behind, keyed by a
`[Parameter, EditorRequired] Guid ManufacturerId` instead of the page's route `Id`. The page keeps
its `@if (manufacturer is not null)` guard and renders `<DeviceModelTable ManufacturerId="Id" />`
inside it, and its code-behind loses the two methods and the `NavigationManager` injection.

Behaviour is unchanged, including the one thing neither version does: the table does not reload when
the parameter changes, because `MudTable`'s `ServerData` fires on first render and the page never
called `ReloadServerData` either.

## EF snapshot

`DeviceManagementDbContextModelSnapshot.cs` names entities by CLR full name
(`"Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModel"`). Moving the type to
`…Manufacturers.DeviceModels` changes that string. The relational model — tables, columns, keys,
indexes — does not change, so `dotnet ef migrations has-pending-model-changes` reports none and the
Development-time `MigrateAsync` is unaffected. The snapshot is regenerated anyway so the stale name does
not surface as noise in the next real migration: `dotnet ef migrations add RelocateDeviceModel`, confirm
`Up`/`Down` are empty, delete the migration and its `.Designer.cs`, keep the rewritten snapshot. A
non-empty `Up` there would mean the move changed the model, which it must not; that is a stop, not a
migration to keep. Older migrations' `.Designer.cs` files keep the old name — they are history, and EF
only diffs against the latest snapshot.

## Documentation

- Root `CLAUDE.md`: the `.Domain` bullet's "lives in its aggregate's folder (`Domain/Manufacturers/DeviceModel.cs`,
  not a folder of its own)" becomes the nested rule; `.Application` feature folders read
  `<Aggregate>[/<ChildEntity>]/Commands/<Feature>/`; `.Infrastructure` notes the per-entity read repository and
  the never-a-write-repository rule; `.Presentation.Blazor` gains `Components/` and the `Models/` vs
  `DeviceModels/` note.
- `src/Modules/DeviceManagement/CLAUDE.md`: every "no `DeviceModels` folder or installation class anywhere"
  becomes "nested `Manufacturers/DeviceModels/` in every layer, no installation class of its own", plus
  `IDeviceModelAllQueryRepository`, `DeviceModelQueryRepository` and `DeviceModelTable`.
- `/add-feature`: input grammar `<Module> <Aggregate>[/<ChildEntity>] <Feature> <command|query>`; every path
  takes the optional `/<ChildEntity>` segment; nested feature folders use `Add`/`GetOne`/`GetList`; a child's
  query repository is its own class under the nested folder, registered in the aggregate's installation
  class; the Blazor section gains `Components/`.
- `/add-module`: `Components/` in the RCL layout.
- `2026-09-16-device-models-design.md` is not edited. Its "no `DeviceModels` folder" decision was about a
  top-level sibling and this spec supersedes the folder placement while keeping every other decision there.

## Testing

Nothing here changes behaviour, so the existing suite is the proof:

- `dotnet build Sergin.MeterMinder.slnx` clean — plus a grep that every `namespace` line under a
  `DeviceModels/` folder ends in that folder's path, since the analyzer only suggests it.
- `git status` shows renames, not delete-and-add pairs.
- `has-pending-model-changes` reports none, and the throwaway migration's `Up`/`Down` are empty.
- `dotnet test` green, in particular `Manufacturers/DeviceModels/DeviceModelTests` (the three slices end to
  end), `Validation/*` (validator discovery survives the namespace move) and `Shell/ModulePageRenderingTests`
  (`/dm/manufacturers/{guid}` still renders interactively with the component inside).
- A headless-browser pass on `/dm/manufacturers/{id}` confirming the models table loads rows and "New model"
  navigates — the one thing prerendering cannot see, because `ServerData` fires after first render.
