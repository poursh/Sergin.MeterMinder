# DeviceManagement full CRUD — design

Date: 2026-10-01
Status: approved in conversation, awaiting written-spec review
Scope: the `dm` module only (`Device`, `Manufacturer`, and `DeviceModel` as `Manufacturer`'s child). No UserAccess
change (see Decision 6).

## Goal

Every DeviceManagement aggregate supports create, read, update and delete from both the Blazor UI and the
(unhosted) WebApi. Today:

| Aggregate | Create | Read (one/list) | Update | Delete |
|---|---|---|---|---|
| `Device` | yes | yes | **missing** | yes (soft, versioned) |
| `Manufacturer` | yes | yes | **missing** | yes (soft, versioned) |
| `DeviceModel` (child) | `AddModel` | yes | **missing** | **missing** |

The work adds `UpdateDevice`, `UpdateManufacturer`, `RenameDeviceModel` and `RemoveDeviceModel`, each as a full
vertical slice following the `/add-feature` shape, plus the permissions to guard them.

## Non-goals

- Users update/delete. UserAccess is a submodule and `User` has no soft delete or row version yet; it gets its
  own spec.
- Restoring or purging soft-deleted rows.
- Generalizing the self-excluding uniqueness rule into SharedKernel (see Decision 2).
- Making manufacturer names unique. They are not unique today and stay that way.

## Decisions

1. **Device update changes both fields.** `UpdateDeviceCommand` carries `DeviceId` and `DeviceModelId`. Correcting
   a mistyped device id is a real need, and the model can change when a meter is re-catalogued.

2. **Self-excluding uniqueness is module-local.** `MustBeUniqueIn(devices)` calls `IsTakenAsync(key)`, which
   answers `true` for the device's own unchanged id and would refuse every save that keeps it. `IDeviceRepository`
   gains `IsTakenByOtherAsync(DeviceId key, DeviceIntenralId exceptId)`, and `UpdateDeviceCommandValidator` uses a
   local `MustAsync` over it with `MustBeUniqueIn`'s message text (`'{PropertyName}' is already in use.`) — the same
   precedent as `ModelExistsAsync`. No SharedKernel change. When `User` update needs the same rule, that is the
   moment to generalize; a second parameter on `IUniqueKeyRepository<TKey>` would defeat the inference
   `MustBeUniqueIn` relies on today, so the generalization needs its own design.

3. **Model changes go through the root.** `DeviceModel` has no repository. Renaming and removing a model are
   behaviours on `Manufacturer`, loaded with `GetWithModelsAsync`, exactly like `AddModel`.

4. **Removing a model in use is refused by a validator rule.** A soft delete fires no foreign key, so
   `dm.device.device_model_id`'s `RESTRICT` no longer protects a model. `RemoveDeviceModelCommandValidator`
   refuses while a live device references the model, mirroring `DeleteManufacturerCommandValidator`. Advisory, like
   every repository rule: a device created between the query and the save lands on a deleted model.

5. **Every new write is version-guarded.** All four commands carry `[RequiresExpectedVersion]`: a missing version
   is 428 (`VersionErrors.Required`), a stale one is 412 (`VersionErrors.Stale`) and nothing is saved. A model
   change moves `Manufacturer`'s `row_version`, as `AddModel` does.

6. **New `.update` permissions; no seed change.** `permission.dm.devices.update` guards `UpdateDevice`;
   `permission.dm.manufacturers.update` guards `UpdateManufacturer`, `RenameDeviceModel` and `RemoveDeviceModel`
   (model changes are changes to the manufacturer aggregate). In `Keycloak` mode the seeded `administrator` role
   holds `permission.sys.platform-all`, and `IUserContext.HasPermissions` passes every check for it
   (`IsSystemAdmin`), so admins can update and delete without any new seed row. `viewer` stays read-only. (Amended
   2026-10-01: the conversation assumed `administrator` lacked the `.delete` permissions and planned a UserAccess
   seed migration; it does not need one.)

7. **`DeviceQueryResponse` gets its manufacturer back.** The device-models spec (Decision 4) made device read
   models "model only" and named adding `ManufacturerId` as the one-line reversal. `EditDevicePage` needs it to
   preselect the manufacturer picker, so `DeviceQueryResponse` becomes
   `(Guid Id, string DeviceId, Guid DeviceModelId, string DeviceModelName, Guid ManufacturerId, string ManufacturerName)`.
   `GetDeviceListItem` stays model-only.

8. **Edit is a separate page, not inline.** Each aggregate gets an `/edit` route that copies the create page's
   `MudForm` + `ISerginFormValidator` shape, as the root CLAUDE.md already prescribes for update pages.

9. **Child verbs are `Rename` and `Remove`.** They match the aggregate behaviours (`RenameModel`, `RemoveModel`)
   and keep the child's vocabulary distinct from `Delete` on roots, the way `Add` is distinct from `Create`.

## Domain

`Device` (`Domain/Devices/Device.cs`):

```csharp
public void Update(DeviceId deviceId, DeviceModelInternalId deviceModelId)
{
    DeviceId = deviceId;
    DeviceModelId = deviceModelId;
}
```

`Manufacturer` (`Domain/Manufacturers/Manufacturer.cs`):

- `public void Update(ManufacturerName name, ManufacturerAddress? address)` — sets both; a `null` address clears it.
- `public ErrorOr<DeviceModel> RenameModel(DeviceModelInternalId id, DeviceModelName name)`:
  - model not in `models` → `Error.NotFound()`;
  - another model already has `name` → `Error.Validation(nameof(DeviceModel.Name), "'Name' is already in use.")`,
    the exact error `AddModel` returns (extract one private helper so the two cannot drift);
  - renaming to the model's own current name succeeds;
  - otherwise calls `DeviceModel`'s new `internal void Rename(DeviceModelName name)` and returns the model.
- `public ErrorOr<Deleted> RemoveModel(DeviceModelInternalId id)`: model not in `models` → `Error.NotFound()`;
  otherwise removes it from the collection and returns `Result.Deleted` (the handler maps that to
  `RemoveDeviceModelCommandResponse`). `SoftDeleteInterceptor` turns the dropped
  child into a soft delete (already covered: "a dropped child is soft-deleted alone").

Both model behaviours need the manufacturer loaded with its models, and say so in their XML doc as `AddModel` does.

Repositories (`Domain`, implemented in `Infrastructure` over `EfRepository`'s protected `AnyAsync`):

- `IDeviceRepository.IsTakenByOtherAsync(DeviceId key, DeviceIntenralId exceptId, CancellationToken)` —
  `AnyAsync(d => d.DeviceId == key && d.Id != exceptId, ct)`. The soft-delete query filter excludes deleted
  devices, matching the partial unique index.
- `IDeviceRepository.AnyUsingModelAsync(DeviceModelInternalId modelId, CancellationToken)` —
  sibling of `AnyUsingManufacturerAsync`.

No migration: audit, soft-delete and row-version columns already exist on all three tables, and no index changes.

## Application slices

Request/response records go in `.Application.Contracts`; handlers and validators in `.Application`, in the same
feature folder. All handlers are `internal sealed class`; all records `sealed record`.

| Slice | Folder | Request | Response | Permission |
|---|---|---|---|---|
| Update device | `Devices/Commands/Update` | `UpdateDeviceCommand(Guid Id, DeviceId DeviceId, DeviceModelInternalId DeviceModelId)` | `UpdateDeviceCommandResponse(Guid Id)` | `permission.dm.devices.update` |
| Update manufacturer | `Manufacturers/Commands/Update` | `UpdateManufacturerCommand(Guid Id, ManufacturerName Name, ManufacturerAddress? Address)` | `UpdateManufacturerCommandResponse(Guid Id)` | `permission.dm.manufacturers.update` |
| Rename model | `Manufacturers/DeviceModels/Commands/Rename` | `RenameDeviceModelCommand(ManufacturerId ManufacturerId, Guid Id, DeviceModelName Name)` | `RenameDeviceModelCommandResponse(Guid Id)` | `permission.dm.manufacturers.update` |
| Remove model | `Manufacturers/DeviceModels/Commands/Remove` | `RemoveDeviceModelCommand(ManufacturerId ManufacturerId, Guid Id)` | `RemoveDeviceModelCommandResponse(Guid Id)` | `permission.dm.manufacturers.update` |

All four also carry `[RequiresExpectedVersion]`. Argument types follow the siblings: the root's own id is a
`Guid` (as on `DeleteDeviceCommand(Guid Id)`), field values are domain value objects (as on `CreateDeviceCommand`),
and the parent of a child is `ManufacturerId` (as on `AddDeviceModelCommand`). The model id is a `Guid`, wrapped
into `DeviceModelInternalId` by the handler. Every response is `<Feature>CommandResponse(Guid Id)`, including
Remove, matching `DeleteDeviceCommandResponse`.

Handlers: load the root (`GetAsync`, or `GetWithModelsAsync` for the model slices); missing root →
`Error.NotFound()`; call the behaviour; propagate its error; `SaveChangesAsync`; return the response.

Validators (every write command has one):

- `UpdateDeviceCommandValidator(IDeviceRepository devices, IManufacturerRepository manufacturers)`: the same shape
  rules as `CreateDeviceCommandValidator` (`.Value` rules with `OverridePropertyName`, `DeviceId.MaxLength`), plus
  `MustAsync(IsTakenByOtherAsync)` on `DeviceId` and the local `MustAsync(manufacturers.ModelExistsAsync)` on
  `DeviceModelId`, each guarded with `.When(<shape rule passed>)`.
- `UpdateManufacturerCommandValidator`: the shape rules of `CreateManufacturerCommandValidator`, plus `Id` not empty.
- `RenameDeviceModelCommandValidator`: shape rules of `AddDeviceModelCommandValidator`, plus both ids not empty.
  Duplicate names are the aggregate's (Decision 3).
- `RemoveDeviceModelCommandValidator(IDeviceRepository devices)`: both ids not empty, plus
  `MustAsync(!AnyUsingModelAsync)` with message
  `"The model is still used by a device; delete those devices first."`.

`GetDeviceById` read side: `DeviceQueryResponse` gains `ManufacturerId` and `ManufacturerName` (Decision 7); the
GetOne SQL in `DeviceQueryRepository` joins `dm.manufacturer` and keeps its `deleted_at_utc IS NULL` on
`dm.device` only (joined tables are not filtered, per convention).

## Presentation — Blazor

Three new pages, each `.razor` (markup only) + `.razor.cs` (`public sealed partial class`, `[Inject]` properties),
plus form models under `Models/` with no DataAnnotations:

| Page | Route | Form model | Entry point |
|---|---|---|---|
| `Devices/Pages/EditDevicePage` | `/dm/devices/{Id:guid}/edit` | `EditDeviceFormModel` | `Edit` button beside `Delete` on `DeviceDetailPage` |
| `Manufacturers/Pages/EditManufacturerPage` | `/dm/manufacturers/{Id:guid}/edit` | `EditManufacturerFormModel` | `Edit` button on `ManufacturerDetailPage` |
| `Manufacturers/DeviceModels/Pages/RenameDeviceModelPage` | `/dm/manufacturers/{ManufacturerId:guid}/models/{Id:guid}/edit` | `RenameDeviceModelFormModel` | `Rename` button on `DeviceModelDetailPage` |

Shape, identical on all three (copied from `CreateUserPage` with these differences):

- `OnInitializedAsync` loads the record with `Dispatcher.SendVersionedAsync(new Get…ByIdQueryCommand(…))`, fills
  the form model, keeps `version`. A failed load renders `SerginProblemPanel` and no form.
- `ToCommand()` includes the route ids; `validation = FormValidator.RulesFor(ToCommand)`.
- Submit: `await form.ValidateAsync(); if (!form.IsValid) return;`, then
  `Dispatcher.SendVersionedAsync(ToCommand(), version)`. Success → navigate to the detail page. Error →
  `ErrorPresenter.Notify(result.Errors)`; if any error is `VersionErrors.Stale`, reload the record and version so
  the next submit is against current data.
- Breadcrumbs: section step, record step linking to the detail page (tail text from the loaded record, placeholder
  the page's `<PageTitle>` word), then `Edit` / `Rename`.
- `EditDevicePage` reuses `CreateDevicePage`'s manufacturer → model cascade, preselected from
  `DeviceQueryResponse.ManufacturerId` / `DeviceModelId`.
- `Cancel` returns to the detail page.

Remove model: a `Remove` button on `DeviceModelDetailPage` (`Color.Error`, disabled while running), the same shape
as `DeviceDetailPage`'s `Delete`: `SendVersionedAsync(new RemoveDeviceModelCommand(…), version)`, success navigates
to `/dm/manufacturers/{ManufacturerId}`, error → `Notify`. `DeviceModelTable` stays read-only (rows open the model).

`DeviceDetailPage` renders the model name as a link to its nested page now that the response carries the
manufacturer id. `_Imports.razor` gains the four new feature namespaces. No `DeviceManagementNavigation` change.

## Presentation — WebApi

`internal class` endpoints implementing `IEndpoint`, mapped in each aggregate's `InstallationExtensions`; routes
carry no schema segment; `[FromBody]` DTOs are plain `record`s.

| Endpoint | Body | Sends |
|---|---|---|
| `PUT /devices/{deviceId:guid}` | `UpdateDeviceModel(string DeviceId, Guid DeviceModelId)` | `UpdateDeviceCommand` |
| `PUT /manufacturers/{manufacturerId:guid}` | `UpdateManufacturerModel(string Name, string? Address)` | `UpdateManufacturerCommand` |
| `PUT /manufacturers/{manufacturerId:guid}/models/{modelId:guid}` | `RenameDeviceModelModel(string Name)` | `RenameDeviceModelCommand` |
| `DELETE /manufacturers/{manufacturerId:guid}/models/{modelId:guid}` | — | `RemoveDeviceModelCommand` |

The groups already carry `ExpectedVersionEndpointFilter`: a strong `If-Match` becomes the expected version and
`Current` becomes the `ETag`. Results go through `ToApiResult()`, no `.Produces<>` (the Delete family's shape).

## Presentation — gRPC

`devices.proto` `DeviceData` gains `manufacturer_id` and `manufacturer_name`; `DeviceGrpcService` and
`GetDeviceByIdGrpcInvoker` map them. No new RPCs: gRPC carries only `GetDeviceById` today, and that stays the case.

## Permissions

- Host `appsettings.json`, `Sergin:DevUser:Permissions`: add `permission.dm.devices.update` and
  `permission.dm.manufacturers.update`.
- Keycloak mode: no change. `administrator` holds `permission.sys.platform-all`; `viewer` gets no write
  permission.

## Testing

Integration only, on the shared `SerginWebApiFactory<Program>` fixture; Blazor-shaped sends resolve
`ISerginDispatcher` from a scope; guarded sends use `VersionedDispatch.SendAt…VersionAsync`.

New `tests/.../Updates/DeviceManagementUpdateTests.cs`:

- Update device changes both fields; the read returns them and a new version; `modified_at_utc`/`modified_by` are
  stamped.
- Update device keeping its own `DeviceId` succeeds; another live device's id is a validation error on `DeviceId`;
  a soft-deleted device's id is accepted; an unknown model is a validation error on `DeviceModelId`.
- Update manufacturer changes name and address; a `null` address clears it.
- Rename model succeeds; a sibling's name is a validation error on `Name`; its own current name succeeds.
- Remove model soft-deletes the model only, keeps the manufacturer live, bumps its version; a model used by a live
  device is refused; a model used only by a deleted device is removed.
- Unknown root or model → not-found, for every slice.
- Each slice is forbidden for a seeded caller without its `.update` permission.

Extended:

- `DeviceManagementConcurrencyTests`: each new command without a version → 428; with a stale version → 412 and
  nothing persisted.
- `ModulePageRenderingTests` and `BreadcrumbRenderingTests`: the three edit pages prerender, render interactively,
  and show their trails.
- `DeviceGrpcRoundTripTests`: the two new `DeviceData` fields round-trip.
- Validator discovery needs no separate test: every slice test that expects a validation error proves its
  validator was found.
- `DeviceReadModelTests.DeviceDetailPage_RendersDeviceModelName_NotItsId` asserts the page contains neither the
  model id nor the manufacturer id. The model link puts both in an `href`, so that test is rewritten to assert the
  link instead.

## Delivery

1. Worktree branch `feature/dm-full-crud`, one PR, commits in this order: domain behaviours and repository lookups;
   the four slices with validators and their tests; read-model and proto change; WebApi endpoints; Blazor pages and
   page tests; docs (`src/Modules/DeviceManagement/CLAUDE.md`, and the root CLAUDE.md's
   "Versioned today / guarded" list and `Sergin:DevUser` permission list).
