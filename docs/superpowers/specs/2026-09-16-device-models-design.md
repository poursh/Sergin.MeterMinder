# Device models under manufacturers

## Motivation

A meter is a physical instance of a *model* — a manufacturer's product line with a fixed set of
capabilities, registers and protocols. Today `Device` carries a `ManufacturerId` directly and knows
nothing about which model it is, so nothing in the system can ever say "all the XYZ-200s" or attach
model-level configuration to the devices that share it. The manufacturer link is the wrong level of
indirection: a device belongs to a model, and the model belongs to a manufacturer.

The domain already half-says this. `Sergin.MeterMinder.DeviceManagement.Domain/DeviceModels/DeviceModel.cs`
declares a `DeviceModel` aggregate with a `Name`, and `Device.cs` carries a commented-out `ModelId`
and a commented-out `Create(DeviceModelInternalId)` overload. Neither is wired into anything — no
repository, no slice, no EF configuration, no page — and the module `CLAUDE.md` warns against
building on the stub. This design finishes it and moves `Device` onto it.

## Scope

In:

- `DeviceModel` as a real aggregate root: `ManufacturerId` (required) and `Name`, with
  `Name` unique **within** a manufacturer.
- The `dm.device_model` table, its composite unique index, and the FK to `dm.manufacturer`.
- Three feature slices — `Create`, `GetOne`, `GetList` — following the module's existing
  `Manufacturers` trio at every layer: Application (+ Contracts), Infrastructure, WebApi endpoints,
  Blazor pages.
- `Device.ManufacturerId` **replaced** by `Device.DeviceModelId` (required). The `dm.device`
  column, FK, command, validator rules, both read models, the raw SQL, the Blazor pages, the WebApi
  DTO and the gRPC `DeviceData` message all follow.
- One migration that creates the new table, deletes every existing `dm.device` row, and swaps the
  column.
- Manufacturer detail page gains a Models table and a "New model" button; create-device page picks
  the model through cascading Manufacturer → Model selects.
- Integration tests for the new slices, the uniqueness rule, the index as the fallback, and every
  updated device test.

Out, recorded so nobody reaches for them by accident:

- **Model-level attributes beyond `Name`** (protocol, register map, firmware, description). The
  aggregate is deliberately minimal; each attribute is its own product decision and its own slice.
- **Editing or deleting a model, or moving a device between models.** No `Update`/`Delete`
  commands exist for any aggregate in this module today; this design adds none.
- **A separate `permission.dm.device-models.*` permission.** Models are part of the manufacturer
  catalog and are reachable only from a manufacturer's pages, so they are gated on
  `permission.dm.manufacturers.read`. A dedicated permission would need an `appsettings.json` grant
  plus a `Sergin.UserAccess` seed-migration PR in the other repo, for no access-control gain today.
- **Manufacturer on the device read models.** Chosen out on purpose (see Decisions): the device
  read models carry the model's id and name only.
- **Backfilling existing devices.** Migrations auto-apply only in Development and there is no
  production data. The migration deletes existing devices rather than inventing placeholder models.
- **Translating Postgres 23503/23505 into `ErrorOr`.** Still the separate cross-cutting slice
  recorded in `2026-09-15-repository-existence-and-uniqueness-design.md`.
- **Server-side sort/filter/search.** The new list page is `MudTable` + paging only, like the
  other three.

## Decisions

1. **`DeviceModel` is its own aggregate root, not a child of `Manufacturer`.** It carries a
   required `ManufacturerId` FK and no navigation property — exactly the `Device → Manufacturer`
   shape the module already uses. A child entity would have meant loading a whole manufacturer to
   add a model, breaking the module's no-navigation style, and losing `MustExistIn` for the
   device's model reference (which needs an `IRepository<DeviceModel, …>`).

2. **`Device` drops `ManufacturerId` entirely.** The manufacturer is derivable through the model;
   storing it twice invites disagreement between the two columns.

3. **Device read models are "model only".** `DeviceQueryResponse` and `GetDeviceListItem` become
   `(Id, DeviceId, DeviceModelId, DeviceModelName)`. One join instead of two, and the manufacturer
   is one click away on the model's page. Consequence, accepted: the device detail page renders the
   model name as text rather than a link, because the model detail route is nested under the
   manufacturer and the device response no longer carries a manufacturer id. Adding `ManufacturerId`
   back to `DeviceQueryResponse` is the one-line reversal if the link is ever wanted.

4. **Name is unique per manufacturer, declared through the existing alternate-key machinery.** A
   `DeviceModelKey(ManufacturerId, DeviceModelName)` value object is the key type;
   `IDeviceModelRepository` implements `IUniqueKeyRepository<DeviceModelKey>`; the validator uses
   `MustBeUniqueIn`; the composite unique index is the guarantee. No bespoke `IsNameTakenAsync`.

5. **Destructive migration.** `DELETE FROM dm.device` before the column swap. Development-only
   auto-apply, no production data, no placeholder rows left behind in the catalog.

6. **Nested routes and pages, no nav entry.** Models live at
   `/manufacturers/{manufacturerId}/models…` in the API and
   `/dm/manufacturers/{ManufacturerId:guid}/models…` in the UI. The manufacturer detail page is the
   entry point.

7. **`GetOne` is keyed by both ids.** `GetDeviceModelByIdQueryCommand(Guid ManufacturerId, Guid Id)`
   and `WHERE id = @Id AND manufacturer_id = @ManufacturerId`, so a model addressed under the wrong
   manufacturer is `NotFound` rather than served. The nested route then carries no ignored segment.

8. **`GetList` takes the manufacturer filter as a first-class argument**, not through
   `ListQuery.Filtering` — which no query repository in the codebase reads, and which would make the
   filter optional where it must not be.

9. **Cascading selects on the create-device page.** Manufacturer is page state only; the form
   model submits `DeviceModelId`. The single-select alternative ("Manufacturer — Model" over the
   whole catalog) needs an unfiltered read shape with a manufacturer join and grows with the catalog.

## Design

### Domain — `Sergin.MeterMinder.DeviceManagement.Domain`

`DeviceModels/DeviceModel.cs` (the stub, finished):

```csharp
public class DeviceModel : AggregateRoot<DeviceModelInternalId>
{
    private DeviceModel() { }

    public ManufacturerId ManufacturerId { get; private set; }
    public DeviceModelName Name { get; private set; }

    public static DeviceModel Create(ManufacturerId manufacturerId, DeviceModelName name)
    {
        return new DeviceModel
        {
            Id = new DeviceModelInternalId(Guid.CreateVersion7()),
            ManufacturerId = manufacturerId,
            Name = name
        };
    }
}

public sealed record DeviceModelInternalId(Guid Value);

// MaxLength: read by CreateDeviceModelCommandValidator and NewDeviceModelFormModel's [StringLength].
public sealed record DeviceModelName(string Value)
{
    public const int MaxLength = 200;
}

// The alternate key IDeviceModelRepository declares: a name is unique within one manufacturer,
// not globally. Composite on purpose — two manufacturers may both ship a "Model 100".
public sealed record DeviceModelKey(ManufacturerId ManufacturerId, DeviceModelName Name);
```

`DeviceModels/IDeviceModelRepository.cs`:

```csharp
public interface IDeviceModelRepository
    : IRepository<DeviceModel, DeviceModelInternalId>, IUniqueKeyRepository<DeviceModelKey>;
```

`Devices/Device.cs`: `ManufacturerId` property and its `using` removed; the commented-out
`ModelId`/`Model`/`Create(DeviceModelInternalId)` lines deleted;
`public DeviceModelInternalId DeviceModelId { get; private set; }` added;
`Create(DeviceId deviceId, DeviceModelInternalId deviceModelId)`. The property is named
`DeviceModelId` (not the stub's `ModelId`) so the read-model fields read `DeviceModelId` /
`DeviceModelName`, the same pattern as today's `ManufacturerId` / `ManufacturerName`.

### Infrastructure.Data

- `DeviceModels/Converters/DeviceModelInternalIdConverter.cs`, `DeviceModelNameConverter.cs` —
  the non-nullable converter template from the root `CLAUDE.md`.
- `DeviceModels/DeviceModelEntityTypeConfiguration.cs`:

  ```csharp
  builder.HasKey(m => m.Id);
  builder.Property(m => m.Id).HasConversion<DeviceModelInternalIdConverter>().ValueGeneratedNever();
  builder.Property(m => m.ManufacturerId).HasConversion<ManufacturerIdConverter>();
  builder.Property(m => m.Name).HasConversion<DeviceModelNameConverter>().IsRequired();

  builder.HasOne<Manufacturer>().WithMany().HasForeignKey(m => m.ManufacturerId).IsRequired();

  // IDeviceModelRepository declares DeviceModelKey (manufacturer + name) an alternate key; the
  // validator's check is advisory, this is the guarantee.
  builder.HasIndex(m => new { m.ManufacturerId, m.Name }).IsUnique();
  ```

- `Devices/DeviceEntityTypeConfiguration.cs`: the `ManufacturerId` property and
  `HasOne<Manufacturer>()` block replaced by

  ```csharp
  builder.Property(d => d.DeviceModelId).HasConversion<DeviceModelInternalIdConverter>();
  builder.HasOne<DeviceModel>().WithMany().HasForeignKey(d => d.DeviceModelId).IsRequired();
  ```

- Migration `AddDeviceModels` (`dotnet ef migrations add AddDeviceModels --project …Infrastructure.Data
  --startup-project src/Hosts/Sergin.MeterMinder.Hosts.All`), then hand-edited:
  1. Convert to a file-scoped namespace (IDE0161 otherwise fails the build).
  2. Insert `migrationBuilder.Sql("DELETE FROM dm.device;");` as the first statement of `Up`,
     with a comment: existing devices carry a manufacturer but no model; `device_model_id` is
     NOT NULL; migrations auto-apply only in Development and there is no production data, so the
     rows are dropped rather than backfilled with placeholder models.
  3. Verify the scaffolded order is: create `dm.device_model` + FK + unique index
     `ix_device_model_manufacturer_id_name`; drop `fk_device_manufacturer_manufacturer_id`,
     `ix_device_manufacturer_id`, column `manufacturer_id`; add `device_model_id` NOT NULL, its
     index and FK. EF scaffolds the `AddColumn` with `defaultValue: Guid.Empty` for a NOT NULL
     column on a table it thinks has rows — leave it; the table is empty by then and the default
     is harmless.

  `.Designer.cs` and the snapshot stay as generated.

### Application.Contracts — `DeviceModels/Commands/…`

| File | Content |
|---|---|
| `Create/CreateDeviceModelCommand.cs` | `public sealed record CreateDeviceModelCommand(ManufacturerId ManufacturerId, DeviceModelName Name) : ICommand<CreateDeviceModelCommandResponse>;` |
| `Create/CreateDeviceModelCommandResponse.cs` | `public sealed record CreateDeviceModelCommandResponse(Guid Id);` |
| `GetOne/GetDeviceModelByIdQueryCommand.cs` | `[RequiredPermissions("permission.dm.manufacturers.read")] public sealed record GetDeviceModelByIdQueryCommand(Guid ManufacturerId, Guid Id) : IQuery<DeviceModelQueryResponse>;` |
| `GetOne/DeviceModelQueryResponse.cs` | `public sealed record DeviceModelQueryResponse(Guid Id, Guid ManufacturerId, string ManufacturerName, string Name);` |
| `GetList/GetDeviceModelListItem.cs` | `public sealed record GetDeviceModelListItem(Guid Id, string Name);` |
| `GetList/GetDeviceModelListQueryCommand.cs` | see below |

```csharp
[RequiredPermissions("permission.dm.manufacturers.read")]
public sealed record GetDeviceModelListQueryCommand : ListQuery<GetDeviceModelListItem>
{
    public GetDeviceModelListQueryCommand(
        ManufacturerId manufacturerId,
        Paggination paggination,
        Term? term = default,
        Filtering? filtering = default,
        Sorting? sorting = default)
        : base(paggination, term, filtering, sorting)
    {
        ManufacturerId = manufacturerId;
    }

    // The filter every read of this list must carry. Not routed through Filtering: no query
    // repository reads it, and it would make a mandatory scope optional.
    public ManufacturerId ManufacturerId { get; }
}
```

`Devices/Commands/…` changes:

- `CreateDeviceCommand(DeviceId DeviceId, DeviceModelInternalId DeviceModelId)`.
- `DeviceQueryResponse(Guid Id, string DeviceId, Guid DeviceModelId, string DeviceModelName)`.
- `GetDeviceListItem(Guid Id, string DeviceId, Guid DeviceModelId, string DeviceModelName)`.

### Application — `DeviceModels/…`

- `IDeviceModelAllQueryRepository : IGetDeviceModelQueryRepository, IGetDeviceModelListQueryRepository`
  (correctly spelled; do not copy the `Devices` typo).
- `Commands/Create/CreateDeviceModelCommandHandler` — `DeviceModel.Create(request.ManufacturerId,
  request.Name)`, `repository.Insert`, `unitOfWork.SaveChangesAsync`, return
  `new CreateDeviceModelCommandResponse(model.Id.Value)`. Checks nothing itself.
- `Commands/Create/CreateDeviceModelCommandValidator(IManufacturerRepository manufacturers,
  IDeviceModelRepository deviceModels)`:

  ```csharp
  RuleFor(x => x.ManufacturerId.Value)
      .NotEmpty()
      .OverridePropertyName(nameof(CreateDeviceModelCommand.ManufacturerId));

  RuleFor(x => x.Name.Value)
      .NotEmpty()
      .MaximumLength(DeviceModelName.MaxLength)
      .OverridePropertyName(nameof(CreateDeviceModelCommand.Name));

  RuleFor(x => x.ManufacturerId)
      .MustExistIn(manufacturers)
      .When(x => x.ManufacturerId.Value != Guid.Empty);

  // The composite key is built in the rule, so the property name has to be restored by hand —
  // the error belongs to Name, which is what the user typed.
  RuleFor(x => new DeviceModelKey(x.ManufacturerId, x.Name))
      .MustBeUniqueIn(deviceModels)
      .OverridePropertyName(nameof(CreateDeviceModelCommand.Name))
      .When(x => x.ManufacturerId.Value != Guid.Empty && !string.IsNullOrWhiteSpace(x.Name.Value));
  ```

  Message on a duplicate: `'Name' is already in use.`; `Error.Code` is `Name`.

- `Commands/GetOne/IGetDeviceModelQueryRepository.GetDeviceModelById(ManufacturerId, DeviceModelInternalId, ct)`
  → `DeviceModelQueryResponse?`; handler maps `null` to bare `Error.NotFound()`.
- `Commands/GetList/IGetDeviceModelListQueryRepository.GetListAsync(ManufacturerId manufacturerId,
  ListQuery query, ct)`; handler passes `request.ManufacturerId, request`.

`Devices/Commands/Create/CreateDeviceCommandValidator(IDeviceRepository devices,
IDeviceModelRepository deviceModels)` — the two `ManufacturerId` rules become

```csharp
RuleFor(x => x.DeviceModelId.Value)
    .NotEmpty()
    .OverridePropertyName(nameof(CreateDeviceCommand.DeviceModelId));

RuleFor(x => x.DeviceModelId)
    .MustExistIn(deviceModels)
    .When(x => x.DeviceModelId.Value != Guid.Empty);
```

Message on an unknown model: `'Device Model Id' must refer to an existing DeviceModel.`

### Infrastructure

- `DeviceModels/Repositories/DeviceModelRepository.cs`:

  ```csharp
  internal sealed class DeviceModelRepository(IDeviceManagementDbContext dbContext)
      : EfRepository<DeviceModel, DeviceModelInternalId>(dbContext), IDeviceModelRepository
  {
      public Task<bool> IsTakenAsync(DeviceModelKey key, CancellationToken cancellationToken = default) =>
          AnyAsync(m => m.ManufacturerId == key.ManufacturerId && m.Name == key.Name, cancellationToken);
  }
  ```

- `DeviceModels/Repositories/Queries/DeviceModelQueryRepository.cs` — raw SQL via
  `IDbConnectionFactory`, one connection per call:

  ```sql
  -- GetDeviceModelById
  SELECT dm_.id, dm_.manufacturer_id AS manufacturerId, m.name AS manufacturerName, dm_.name
  FROM dm.device_model dm_
  JOIN dm.manufacturer m ON m.id = dm_.manufacturer_id
  WHERE dm_.id = @Id AND dm_.manufacturer_id = @ManufacturerId;

  -- GetListAsync, one QueryMultipleAsync
  SELECT count(*) FROM dm.device_model WHERE manufacturer_id = @ManufacturerId;

  SELECT id, name
  FROM dm.device_model
  WHERE manufacturer_id = @ManufacturerId
  ORDER BY id
  LIMIT @PageSize OFFSET @Offset;
  ```

  (`dm` is the schema; the alias is `dm_` so the two never read alike.)

- `Devices/Repositories/Queries/DeviceQueryRepository.cs` — both statements lose the manufacturer
  join and gain the model join:

  ```sql
  SELECT d.id, d.device_id AS deviceId, d.device_model_id AS deviceModelId, dm_.name AS deviceModelName
  FROM dm.device d
  JOIN dm.device_model dm_ ON dm_.id = d.device_model_id
  ```

### Composition root — `Sergin.MeterMinder.DeviceManagement`

`DeviceModels/DeviceModelInstallationExtensions.cs` with `AddDeviceModelDependencies()` (the write
repository plus the three query interfaces, each against `DeviceModelQueryRepository`) and
`MapDeviceModelEndpoints()`. `DeviceManagementModule.AddServices` and `MapEndpoints` call them next
to the manufacturer pair.

### Presentation.WebApi — `DeviceModels/Endpoints/…`

| Endpoint | Route | Shape |
|---|---|---|
| `Create/CreateDeviceModelEndpoint` | `POST /manufacturers/{manufacturerId:guid}/models` | `[FromBody] NewDeviceModelModel(string Name)` (plain `record`), `.Produces<CreateDeviceModelCommandResponse>()` |
| `GetOne/GetDeviceModelEndpoint` | `GET /manufacturers/{manufacturerId:guid}/models/{modelId:guid}` | no `.Produces` — the GetOne family omits it |
| `GetList/GetDeviceModelListEndpoint` | `GET /manufacturers/{manufacturerId:guid}/models` | `[AsParameters] ListQueryRequestModel`, `.Produces<ListQueryResponse<GetDeviceModelListItem>>()` |

Endpoint classes are `internal class`, unsealed; they inject `ISender` directly and call
`.ToApiResult()`. Route strings carry no `/dm` — the host adds it.

`Devices/Endpoints/Create/NewDeviceModel` (the existing device DTO — the name clash with the new
aggregate is unfortunate but pre-existing; it is not renamed here) becomes
`NewDeviceModel(string DeviceId, Guid DeviceModelId)`; the endpoint builds
`new DeviceModelInternalId(request.DeviceModelId)`.

### Presentation.Blazor

New `DeviceModels/` folder:

- `Models/NewDeviceModelFormModel` — `[Required] [StringLength(Domain.DeviceModels.DeviceModelName.MaxLength, MinimumLength = 1)] string Name`.
- `Pages/CreateDeviceModelPage` — `@page "/dm/manufacturers/{ManufacturerId:guid}/models/new"`.
  Title "New device model"; one `MudTextField` for the name; Cancel returns to
  `/dm/manufacturers/{ManufacturerId}`. Submit dispatches
  `CreateDeviceModelCommand(new ManufacturerId(ManufacturerId), new DeviceModelName(model.Name))`,
  `ErrorPresenter.Notify(result.Errors)` on failure, navigates to
  `/dm/manufacturers/{ManufacturerId}/models/{result.Value.Id}` on success.
- `Pages/DeviceModelDetailPage` — `@page "/dm/manufacturers/{ManufacturerId:guid}/models/{Id:guid}"`.
  Back button to the manufacturer; `SerginProblemPanel`; card with `Name` as `h5`,
  `Manufacturer: <MudLink Href="/dm/manufacturers/{ManufacturerId}">{ManufacturerName}</MudLink>`,
  and the id. Dispatches `GetDeviceModelByIdQueryCommand(ManufacturerId, Id)` in
  `OnParametersSetAsync`, same success/problem handling as `ManufacturerDetailPage`.

Changed pages:

- `Manufacturers/Pages/ManufacturerDetailPage` — below the existing card, rendered only when
  `manufacturer is not null`: a `MudStack` header ("Models", "New model" button to
  `/dm/manufacturers/{Id}/models/new`) and a `MudTable<GetDeviceModelListItem>` with `ServerData`
  dispatching `GetDeviceModelListQueryCommand(new ManufacturerId(Id), Paggination.Create(state.PageSize, state.Page + 1))`
  — `PageIndex` is 1-based, `TableState.Page` 0-based, comment says so — `Notify(result.FirstError)`
  on failure, row click to the model's detail route. One column, Name.
- `Devices/Pages/CreateDevicePage` — the manufacturer `MudSelect` stays but binds to a page field
  `selectedManufacturerId` (no `For`, not on the form model) through `Value`/`ValueChanged`; the
  handler dispatches `GetDeviceModelListQueryCommand(new ManufacturerId(value), Paggination.Create(200, 1))`
  into `deviceModels` and resets `model.DeviceModelId`. A second `MudSelect<Guid>` labelled "Model"
  binds `model.DeviceModelId` with `For`, `Disabled` until a manufacturer is chosen. The same
  200-row caveat comment as the manufacturer picker applies. Submit builds
  `new CreateDeviceCommand(new DeviceId(model.DeviceId), new DeviceModelInternalId(model.DeviceModelId))`.
- `Devices/Models/NewDeviceFormModel` — `ManufacturerId` replaced by `[Required] Guid DeviceModelId`.
- `Devices/Pages/DeviceListPage` — header and cell "Manufacturer" → "Model", bound to
  `DeviceModelName`.
- `Devices/Pages/DeviceDetailPage` — `Manufacturer:` line becomes `Model: @device.DeviceModelName`
  as plain text (Decision 3).

`_Imports.razor` gains the three `…Application.DeviceModels.Commands.*` namespaces. No
`DeviceManagementNavigation` change. All C# in `.razor.cs`; no `@code` blocks.

### Presentation.Grpc

`Contracts/Protos/devices.proto`, `DeviceData`:

```proto
message DeviceData {
  string id = 1;
  string device_id = 2;
  string device_model_id = 3;
  string device_model_name = 4;
}
```

Fields 3 and 4 are re-used rather than reserved: the service is live-but-unhosted and no client has
ever seen the old shape. `Server/Devices/DeviceGrpcService` and `Client/Devices/GetDeviceByIdGrpcInvoker`
map the two new fields in place of the manufacturer pair.

## Testing

All integration tests, `[Collection(nameof(IntegrationTestCollection))]`, dispatching through
`ISerginDispatcher` resolved from a scope.

New `tests/…/DeviceModels/DeviceModelTests.cs`:

- `Create_UnderAManufacturer_ThenGetOneAndListFindIt` — the round trip; `GetOne` returns the
  manufacturer's name; the manufacturer's list contains the id.
- `Create_WithAnUnknownManufacturer_IsRefused` — `Error.Code == "ManufacturerId"`, description
  `'Manufacturer Id' must refer to an existing Manufacturer.`
- `Create_WithANameAlreadyUsedByThatManufacturer_IsRefused` — `Error.Code == "Name"`,
  `'Name' is already in use.`
- `Create_WithANameUsedByAnotherManufacturer_IsAllowed` — the same name under two manufacturers
  both succeed; this is what makes the key composite.
- `UniqueIndex_IsTheGuaranteeWhenTheValidatorIsBypassed` — `IDeviceModelRepository.Insert` twice
  directly + `SaveChangesAsync` throws `DbUpdateException` (mirrors the device test).
- `GetList_ReturnsOnlyThatManufacturersModels` — two manufacturers, models under each, list for one
  contains none of the other's.
- `GetOne_UnderTheWrongManufacturer_IsNotFound` — real model id, other manufacturer's id,
  `ErrorType.NotFound`.

Updated:

- `Devices/DeviceReadModelTests` — helpers become manufacturer → model → device; assertions on
  `DeviceModelId`/`DeviceModelName`; the detail-page HTML test asserts the model name is present
  and neither Guid is rendered as text.
- `Devices/DeviceGrpcRoundTripTests` — the record-equality assertion follows the new shape.
- `Validation/RepositoryRuleTests` — `CreateDevice_WithAnUnknownManufacturer_IsRefused` becomes
  `…UnknownDeviceModel…` with `Error.Code == nameof(CreateDeviceCommand.DeviceModelId)`; the
  multi-error test asserts the model error alongside the device-id error; helpers create a model.
- `Validation/CommandValidationTests` — the empty-command test asserts `DeviceModelId` instead of
  `ManufacturerId`.
- `Shell/ModulePageRenderingTests` — `/dm/manufacturers/{guid}/models/new` and
  `/dm/manufacturers/{guid}/models/{guid}` render server-side and interactively (the detail page
  renders its problem panel for random ids, which is still a render).
Unchanged on purpose: `Devices/DeviceListQueryTests` constructs no device command (it exercises the
manufacturer list query the create-device picker depends on) — untouched.
`Validation/ValidationProblemRenderingTests` builds a synthetic
`Error.Validation("ManufacturerId", …)` — it tests rendering, not the device command, and the
string is only a label.

## Documentation

- `src/Modules/DeviceManagement/CLAUDE.md` — new `DeviceModels` aggregate section (shape, key,
  slices table, routes, validator, the composite-key `OverridePropertyName` idiom); the "unfinished,
  dangling piece" paragraph deleted; the `Devices` section rewritten for `DeviceModelId`, the
  "model only" read models, the model join, the gRPC fields, and the validator's new dependency;
  the stale "Permission: none" on both `GetList` rows corrected to `permission.dm.manufacturers.read`
  / `permission.dm.devices.read` (the attributes are already there); the `Manufacturers` section
  notes the detail page's Models table.
- Root `.claude/CLAUDE.md` — the mentions of `CreateDeviceCommandValidator(IDeviceRepository devices,
  IManufacturerRepository manufacturers)`, the `dm.device.manufacturer_id → dm.manufacturer.id` FK,
  and `MustExistIn(manufacturers)` on `ManufacturerId` updated to the model equivalents.
- `graphify update .` + `python .claude/skills/graphify/scripts/graphify_repair.py` after the code
  lands.

## Rollout

Worktree `feat/device-models` off `main`; `git submodule update --init --recursive` inside it
before the first build. No SharedKernel or UserAccess change — this repo only. Order: domain +
EF + migration → Application slices with their tests → Infrastructure → WebApi → gRPC → Blazor →
docs. `dotnet build Sergin.MeterMinder.slnx` must be warning-free under the analyzers; the full
integration suite runs against Testcontainers (Docker Desktop up first).
