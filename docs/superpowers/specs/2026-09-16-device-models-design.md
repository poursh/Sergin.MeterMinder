# Device models under manufacturers

## Motivation

A meter is a physical instance of a *model* — a manufacturer's product line with a fixed set of
capabilities, registers and protocols. Today `Device` carries a `ManufacturerId` directly and knows
nothing about which model it is, so nothing in the system can ever say "all the XYZ-200s" or attach
model-level configuration to the devices that share it. The manufacturer link is the wrong level of
indirection: a device belongs to a model, and the model belongs to a manufacturer.

The domain already half-says this. `Sergin.MeterMinder.DeviceManagement.Domain/DeviceModels/DeviceModel.cs`
declares a `DeviceModel` with a `Name`, and `Device.cs` carries a commented-out `ModelId` and a
commented-out `Create(DeviceModelInternalId)` overload. Neither is wired into anything — no
repository, no slice, no EF configuration, no page — and the module `CLAUDE.md` warns against
building on the stub. This design finishes it, as an entity inside the `Manufacturer` aggregate,
and moves `Device` onto it.

## Scope

In:

- `DeviceModel` as a child entity of the `Manufacturer` aggregate: `ManufacturerId` and `Name`,
  with `Name` unique **within** a manufacturer, the invariant enforced by `Manufacturer.AddModel`.
- The `dm.device_model` table, its composite unique index, and the FK to `dm.manufacturer`.
- Three feature slices on the `Manufacturers` aggregate — `AddDeviceModel`, `GetDeviceModel`,
  `GetDeviceModelList` — following the module's existing trio at every layer: Application
  (+ Contracts), Infrastructure, WebApi endpoints, Blazor pages.
- `Device.ManufacturerId` **replaced** by `Device.DeviceModelId` (required). The `dm.device`
  column, FK, command, validator rules, both read models, the raw SQL, the Blazor pages, the WebApi
  DTO and the gRPC `DeviceData` message all follow.
- One migration that creates the new table, deletes every existing `dm.device` row, and swaps the
  column.
- Manufacturer detail page gains a Models table and a "New model" button; create-device page picks
  the model through cascading Manufacturer → Model selects.
- Integration tests for the new slices, the aggregate's uniqueness invariant, the index as the
  fallback under a race, and every updated device test.

Out, recorded so nobody reaches for them by accident:

- **Model-level attributes beyond `Name`** (protocol, register map, firmware, description). The
  entity is deliberately minimal; each attribute is its own product decision and its own slice.
- **Renaming or removing a model, or moving a device between models.** No `Update`/`Delete`
  commands exist for any aggregate in this module today; this design adds none.
- **A separate `permission.dm.device-models.*` permission.** Models are part of the manufacturer
  aggregate and are reachable only from a manufacturer's pages, so they are gated on
  `permission.dm.manufacturers.read`. A dedicated permission would need an `appsettings.json` grant
  plus a `Sergin.UserAccess` seed-migration PR in the other repo, for no access-control gain today.
- **Manufacturer on the device read models.** Chosen out on purpose (see Decisions): the device
  read models carry the model's id and name only.
- **Backfilling existing devices.** Migrations auto-apply only in Development and there is no
  production data. The migration deletes existing devices rather than inventing placeholder models.
- **Translating Postgres 23503/23505 into `ErrorOr`.** Still the separate cross-cutting slice
  recorded in `2026-09-15-repository-existence-and-uniqueness-design.md`.
- **A generic "must reference an existing child entity" validation rule in SharedKernel.**
  `MustExistIn` is generic over aggregate roots; the one call site that needs the entity version
  uses a local `MustAsync`. Generalising it is a SharedKernel PR for a second call site that does
  not exist yet.
- **Server-side sort/filter/search.** The new list is `MudTable` + paging only, like the other three.

## Decisions

1. **`DeviceModel` is an entity inside the `Manufacturer` aggregate, not its own root.** A model
   has no life outside the manufacturer that makes it, and the one invariant it carries — a name
   is unique within its manufacturer — is exactly the kind of rule an aggregate exists to hold.
   `Manufacturer.AddModel(name)` is the only way a model comes into being, and there is no
   `IDeviceModelRepository`.

2. **Mapped as a regular EF entity type, not `OwnsMany`.** `Role.Permissions` and `User.Roles`
   use `OwnsMany`, and it would be the natural first reach. It cannot work here: `Device` holds a
   foreign key to a model, and EF Core refuses an owned type on the principal side of a
   non-ownership relationship (`CoreStrings.PrincipalOwnedType`: "the owned entity type … cannot be
   on the principal side of a non-ownership relationship"). So `DeviceModel` is a plain entity type
   in `dm.device_model`, related to `Manufacturer` through `HasMany(...).WithOne()`, and the
   aggregate boundary is held by the **repository layer**: no `DbSet<DeviceModel>` is exposed, no
   repository takes a `DeviceModel`, and the only writes go through `Manufacturer.Models`.

3. **`Device` drops `ManufacturerId` entirely.** The manufacturer is derivable through the model;
   storing it twice invites disagreement between the two columns.

4. **Device read models are "model only".** `DeviceQueryResponse` and `GetDeviceListItem` become
   `(Id, DeviceId, DeviceModelId, DeviceModelName)`. One join instead of two, and the manufacturer
   is one click away on the model's page. Consequence, accepted: the device detail page renders the
   model name as text rather than a link, because the model detail route is nested under the
   manufacturer and the device response no longer carries a manufacturer id. Adding `ManufacturerId`
   back to `DeviceQueryResponse` is the one-line reversal if the link is ever wanted.

5. **The uniqueness invariant lives in the aggregate and returns `Error.Validation`.**
   `AddModel` refuses a duplicate name with `Error.Validation(nameof(DeviceModel.Name),
   "'Name' is already in use.")` — the same code and message `MustBeUniqueIn` would have produced —
   so it renders through the path already built for validation errors (`Description` shown, one
   snackbar in Blazor, one `ValidationProblem` entry on the API). The composite unique index stays
   as the guarantee under a race. There is no `IUniqueKeyRepository<...>` and no `DeviceModelKey`:
   that machinery is for alternate keys of aggregate roots checked before the handler runs; here
   the aggregate itself answers.

6. **Adding a model is a mutation of an existing aggregate, so the handler owns not-found.**
   `AddDeviceModelCommandHandler` loads the manufacturer and returns `Error.NotFound()` when it is
   missing — the `DeactivateUserCommandHandler` shape — and the validator checks shape only. No
   `MustExistIn` on `ManufacturerId`: the handler has to load the aggregate anyway, and a second
   query for the same answer buys nothing.

7. **Destructive migration.** `DELETE FROM dm.device` before the column swap. Development-only
   auto-apply, no production data, no placeholder rows left behind in the catalog.

8. **Nested routes and pages, no nav entry.** Models live at
   `/manufacturers/{manufacturerId}/models…` in the API and
   `/dm/manufacturers/{ManufacturerId:guid}/models…` in the UI. The manufacturer detail page is the
   entry point.

9. **`GetDeviceModel` is keyed by both ids.** `GetDeviceModelByIdQueryCommand(Guid ManufacturerId,
   Guid Id)` and `WHERE id = @Id AND manufacturer_id = @ManufacturerId`, so a model addressed under
   the wrong manufacturer is `NotFound` rather than served. The nested route then carries no
   ignored segment.

10. **`GetDeviceModelList` takes the manufacturer filter as a first-class argument**, not through
    `ListQuery.Filtering` — which no query repository in the codebase reads, and which would make
    the filter optional where it must not be.

11. **Cascade inside the aggregate, restrict across it.** `Manufacturer → Models` is
    `DeleteBehavior.Cascade` (aggregate semantics); `Device → DeviceModel` is
    `DeleteBehavior.Restrict` — a cross-aggregate reference must never cascade. Today's
    `Device → Manufacturer` FK silently took EF's default (cascade for a required relationship);
    since that FK is being rewritten anyway, the new one is explicit.

12. **Cascading selects on the create-device page.** Manufacturer is page state only; the form
    model submits `DeviceModelId`. The single-select alternative ("Manufacturer — Model" over the
    whole catalog) needs an unfiltered read shape with a manufacturer join and grows with the catalog.

13. **Trade-off accepted: adding a model loads every model of that manufacturer.**
    `GetWithModelsAsync` includes the collection so `AddModel` can check the name. That is the
    cost of holding the invariant in the aggregate, and it is fine for a catalog of tens of models
    per manufacturer. If a manufacturer ever has thousands, the check moves to a repository query
    and this decision is revisited.

## Design

### Domain — `Sergin.MeterMinder.DeviceManagement.Domain`

The `DeviceModels/` folder is deleted; an entity lives in its aggregate's folder.

`Manufacturers/DeviceModel.cs`:

```csharp
public class DeviceModel : Entity<DeviceModelInternalId>
{
    private DeviceModel() { }

    // The owner's id. EF needs it as the foreign key, and the entity knowing which manufacturer it
    // belongs to costs nothing.
    public ManufacturerId ManufacturerId { get; private set; }
    public DeviceModelName Name { get; private set; }

    // internal: Manufacturer.AddModel is the only caller. A model does not come into being on its own.
    internal static DeviceModel Create(ManufacturerId manufacturerId, DeviceModelName name)
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

// MaxLength: read by AddDeviceModelCommandValidator and NewDeviceModelFormModel's [StringLength].
public sealed record DeviceModelName(string Value)
{
    public const int MaxLength = 200;
}
```

`Manufacturers/Manufacturer.cs` gains the collection and the one behaviour:

```csharp
private readonly List<DeviceModel> models = [];

public IReadOnlyCollection<DeviceModel> Models => models;

/// <summary>
/// The only way a model comes into being. Refuses a name this manufacturer already uses — the
/// invariant the aggregate exists to hold; ix_device_model_manufacturer_id_name is the guarantee
/// under a race. Requires the manufacturer to have been loaded with its models
/// (IManufacturerRepository.GetWithModelsAsync); on a manufacturer loaded through GetAsync the
/// collection is empty and the check cannot see existing names.
/// </summary>
public ErrorOr<DeviceModel> AddModel(DeviceModelName name)
{
    if (models.Any(model => model.Name == name))
    {
        return Error.Validation(nameof(DeviceModel.Name), "'Name' is already in use.");
    }

    DeviceModel model = DeviceModel.Create(Id, name);
    models.Add(model);

    return model;
}
```

`DeviceModelName` is a record, so `==` is value equality and the comparison is case-sensitive —
the same rule the Postgres index applies.

`Manufacturers/IManufacturerRepository.cs` gains two named lookups:

```csharp
public interface IManufacturerRepository : IRepository<Manufacturer, ManufacturerId>
{
    /// <summary>The aggregate with its Models loaded — what AddModel needs. GetAsync loads no navigation.</summary>
    Task<Manufacturer?> GetWithModelsAsync(ManufacturerId id, CancellationToken cancellationToken = default);

    /// <summary>Whether any manufacturer has this model — a SELECT EXISTS on dm.device_model, nothing loaded.</summary>
    Task<bool> ModelExistsAsync(DeviceModelInternalId id, CancellationToken cancellationToken = default);
}
```

`Devices/Device.cs`: `ManufacturerId` property and its `using` removed; the commented-out
`ModelId`/`Model`/`Create(DeviceModelInternalId)` lines deleted;
`public DeviceModelInternalId DeviceModelId { get; private set; }` added;
`Create(DeviceId deviceId, DeviceModelInternalId deviceModelId)`. The property is named
`DeviceModelId` (not the stub's `ModelId`) so the read-model fields read `DeviceModelId` /
`DeviceModelName`, the same pattern as today's `ManufacturerId` / `ManufacturerName`.

### Infrastructure.Data

- `Manufacturers/Converters/DeviceModelInternalIdConverter.cs`, `DeviceModelNameConverter.cs` —
  the non-nullable converter template from the root `CLAUDE.md`.
- `Manufacturers/ManufacturerEntityTypeConfiguration.cs` gains the relationship, configured from
  the owner's side, and the backing-field navigation (the `Role` idiom):

  ```csharp
  // Not OwnsMany, though Role.Permissions and User.Roles are: Device holds a foreign key to a
  // model, and EF refuses an owned type on the principal side of a non-ownership relationship
  // (CoreStrings.PrincipalOwnedType). DeviceModel is a regular entity type instead, and the
  // aggregate boundary is held by the repository layer — no DbSet, no repository of its own,
  // written only through Manufacturer.Models.
  builder.HasMany(m => m.Models)
      .WithOne()
      .HasForeignKey(model => model.ManufacturerId)
      .IsRequired()
      .OnDelete(DeleteBehavior.Cascade);

  builder.Navigation(m => m.Models)
      .HasField("models")
      .UsePropertyAccessMode(PropertyAccessMode.Field);
  ```

- `Manufacturers/DeviceModelEntityTypeConfiguration.cs` — the entity's own shape:

  ```csharp
  builder.HasKey(model => model.Id);
  builder.Property(model => model.Id).HasConversion<DeviceModelInternalIdConverter>().ValueGeneratedNever();
  builder.Property(model => model.ManufacturerId).HasConversion<ManufacturerIdConverter>();
  builder.Property(model => model.Name).HasConversion<DeviceModelNameConverter>().IsRequired();

  // Manufacturer.AddModel refuses a duplicate name; this is the guarantee under a race.
  builder.HasIndex(model => new { model.ManufacturerId, model.Name }).IsUnique();
  ```

- `Devices/DeviceEntityTypeConfiguration.cs`: the `ManufacturerId` property and
  `HasOne<Manufacturer>()` block replaced by

  ```csharp
  builder.Property(d => d.DeviceModelId).HasConversion<DeviceModelInternalIdConverter>();

  // Restrict, not the default cascade: a reference across an aggregate boundary must never delete
  // the referrer.
  builder.HasOne<DeviceModel>()
      .WithMany()
      .HasForeignKey(d => d.DeviceModelId)
      .IsRequired()
      .OnDelete(DeleteBehavior.Restrict);
  ```

- Migration `AddDeviceModels` (`dotnet ef migrations add AddDeviceModels --project …Infrastructure.Data
  --startup-project src/Hosts/Sergin.MeterMinder.Hosts.All`), then hand-edited:
  1. Convert to a file-scoped namespace (IDE0161 otherwise fails the build).
  2. Insert `migrationBuilder.Sql("DELETE FROM dm.device;");` as the first statement of `Up`,
     with a comment: existing devices carry a manufacturer but no model; `device_model_id` is
     NOT NULL; migrations auto-apply only in Development and there is no production data, so the
     rows are dropped rather than backfilled with placeholder models.
  3. Verify the scaffolded order is: create `dm.device_model` + FK (`ON DELETE CASCADE`) + unique
     index `ix_device_model_manufacturer_id_name`; drop `fk_device_manufacturer_manufacturer_id`,
     `ix_device_manufacturer_id`, column `manufacturer_id`; add `device_model_id` NOT NULL, its
     index and FK (`ON DELETE RESTRICT`). EF scaffolds the `AddColumn` with `defaultValue:
     Guid.Empty` for a NOT NULL column on a table it thinks has rows — leave it; the table is empty
     by then and the default is harmless.

  `.Designer.cs` and the snapshot stay as generated.

### Application.Contracts — `Manufacturers/Commands/…`

Three new feature folders on the existing aggregate:

| File | Content |
|---|---|
| `AddDeviceModel/AddDeviceModelCommand.cs` | `public sealed record AddDeviceModelCommand(ManufacturerId ManufacturerId, DeviceModelName Name) : ICommand<AddDeviceModelCommandResponse>;` |
| `AddDeviceModel/AddDeviceModelCommandResponse.cs` | `public sealed record AddDeviceModelCommandResponse(Guid Id);` |
| `GetDeviceModel/GetDeviceModelByIdQueryCommand.cs` | `[RequiredPermissions("permission.dm.manufacturers.read")] public sealed record GetDeviceModelByIdQueryCommand(Guid ManufacturerId, Guid Id) : IQuery<DeviceModelQueryResponse>;` |
| `GetDeviceModel/DeviceModelQueryResponse.cs` | `public sealed record DeviceModelQueryResponse(Guid Id, Guid ManufacturerId, string ManufacturerName, string Name);` |
| `GetDeviceModelList/GetDeviceModelListItem.cs` | `public sealed record GetDeviceModelListItem(Guid Id, string Name);` |
| `GetDeviceModelList/GetDeviceModelListQueryCommand.cs` | see below |

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

### Application — `Manufacturers/…`

- `IManufacturerAllQueryRepository` also extends the two new query interfaces
  (`IGetDeviceModelQueryRepository`, `IGetDeviceModelListQueryRepository`).
- `Commands/AddDeviceModel/AddDeviceModelCommandHandler(IDeviceManagementUnitOfWork unitOfWork,
  IManufacturerRepository repository)`:

  ```csharp
  Manufacturer? manufacturer = await repository.GetWithModelsAsync(request.ManufacturerId, cancellationToken);

  if (manufacturer is null)
  {
      return Error.NotFound();
  }

  ErrorOr<DeviceModel> added = manufacturer.AddModel(request.Name);

  if (added.IsError)
  {
      return added.Errors;
  }

  await unitOfWork.SaveChangesAsync(cancellationToken);

  return new AddDeviceModelCommandResponse(added.Value.Id.Value);
  ```

  `GetWithModelsAsync`, not `GetAsync` — see the `AddModel` remarks. EF tracks the new entity
  through the navigation, so nothing is inserted explicitly.

- `Commands/AddDeviceModel/AddDeviceModelCommandValidator` — shape only, no constructor
  dependencies:

  ```csharp
  RuleFor(x => x.ManufacturerId.Value)
      .NotEmpty()
      .OverridePropertyName(nameof(AddDeviceModelCommand.ManufacturerId));

  RuleFor(x => x.Name.Value)
      .NotEmpty()
      .MaximumLength(DeviceModelName.MaxLength)
      .OverridePropertyName(nameof(AddDeviceModelCommand.Name));
  ```

  A comment names the two rules that are deliberately absent: manufacturer existence (the handler
  loads the aggregate and answers `NotFound`) and name uniqueness (`Manufacturer.AddModel`).

- `Commands/GetDeviceModel/IGetDeviceModelQueryRepository.GetDeviceModelById(ManufacturerId,
  DeviceModelInternalId, ct)` → `DeviceModelQueryResponse?`; handler maps `null` to bare
  `Error.NotFound()`.
- `Commands/GetDeviceModelList/IGetDeviceModelListQueryRepository.GetListAsync(ManufacturerId
  manufacturerId, ListQuery query, ct)`; handler passes `request.ManufacturerId, request`.

`Devices/Commands/Create/CreateDeviceCommandValidator(IDeviceRepository devices,
IManufacturerRepository manufacturers)` — the constructor is unchanged; the two `ManufacturerId`
rules become

```csharp
RuleFor(x => x.DeviceModelId.Value)
    .NotEmpty()
    .OverridePropertyName(nameof(CreateDeviceCommand.DeviceModelId));

// A local MustAsync, not MustExistIn: that extension is generic over IRepository<TAggregateRoot, TId>
// and a model is an entity inside the Manufacturer aggregate, not a root with a repository.
RuleFor(x => x.DeviceModelId)
    .MustAsync(manufacturers.ModelExistsAsync)
    .WithMessage("'{PropertyName}' must refer to an existing DeviceModel.")
    .When(x => x.DeviceModelId.Value != Guid.Empty);
```

Message on an unknown model: `'Device Model Id' must refer to an existing DeviceModel.`

### Infrastructure

- `Manufacturers/Repositories/ManufacturerRepository.cs` — no longer a one-liner:

  ```csharp
  internal sealed class ManufacturerRepository(IDeviceManagementDbContext dbContext)
      : EfRepository<Manufacturer, ManufacturerId>(dbContext), IManufacturerRepository
  {
      public Task<Manufacturer?> GetWithModelsAsync(ManufacturerId id, CancellationToken cancellationToken = default) =>
          Set.Include(m => m.Models).SingleOrDefaultAsync(m => m.Id == id, cancellationToken);

      // The one place DeviceModel is queried outside its aggregate: a yes/no for CreateDeviceCommandValidator.
      public Task<bool> ModelExistsAsync(DeviceModelInternalId id, CancellationToken cancellationToken = default) =>
          dbContext.Set<DeviceModel>().AnyAsync(model => model.Id == id, cancellationToken);
  }
  ```

- `Manufacturers/Repositories/Queries/ManufacturerQueryRepository.cs` gains the two reads — raw
  SQL via `IDbConnectionFactory`, one connection per call:

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

`Manufacturers/ManufacturerInstallationExtensions.cs` registers the two new query interfaces
against `ManufacturerQueryRepository` and maps the three new endpoints. No new installation class:
the slices belong to the aggregate that already has one.

### Presentation.WebApi — `Manufacturers/Endpoints/…`

| Endpoint | Route | Shape |
|---|---|---|
| `AddDeviceModel/AddDeviceModelEndpoint` | `POST /manufacturers/{manufacturerId:guid}/models` | `[FromBody] NewDeviceModelModel(string Name)` (plain `record`), `.Produces<AddDeviceModelCommandResponse>()` |
| `GetDeviceModel/GetDeviceModelEndpoint` | `GET /manufacturers/{manufacturerId:guid}/models/{modelId:guid}` | no `.Produces` — the GetOne family omits it |
| `GetDeviceModelList/GetDeviceModelListEndpoint` | `GET /manufacturers/{manufacturerId:guid}/models` | `[AsParameters] ListQueryRequestModel`, `.Produces<ListQueryResponse<GetDeviceModelListItem>>()` |

Endpoint classes are `internal class`, unsealed; they inject `ISender` directly and call
`.ToApiResult()`. Route strings carry no `/dm` — the host adds it.

`Devices/Endpoints/Create/NewDeviceModel` (the existing device DTO — the name clash with the new
entity is unfortunate but pre-existing; it is not renamed here) becomes
`NewDeviceModel(string DeviceId, Guid DeviceModelId)`; the endpoint builds
`new DeviceModelInternalId(request.DeviceModelId)`.

### Presentation.Blazor

New, under the existing `Manufacturers/` folder:

- `Models/NewDeviceModelFormModel` — `[Required] [StringLength(Domain.Manufacturers.DeviceModelName.MaxLength, MinimumLength = 1)] string Name`.
- `Pages/AddDeviceModelPage` — `@page "/dm/manufacturers/{ManufacturerId:guid}/models/new"`.
  Title "New device model"; one `MudTextField` for the name; Cancel returns to
  `/dm/manufacturers/{ManufacturerId}`. Submit dispatches
  `AddDeviceModelCommand(new ManufacturerId(ManufacturerId), new DeviceModelName(model.Name))`,
  `ErrorPresenter.Notify(result.Errors)` on failure (a duplicate name arrives as one validation
  error, an unknown manufacturer as not-found), navigates to
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
  as plain text (Decision 4).

`_Imports.razor` gains the three `…Application.Manufacturers.Commands.{AddDeviceModel,GetDeviceModel,GetDeviceModelList}`
namespaces. No `DeviceManagementNavigation` change. All C# in `.razor.cs`; no `@code` blocks.

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

New `tests/…/Manufacturers/DeviceModelTests.cs`:

- `AddModel_UnderAManufacturer_ThenGetOneAndListFindIt` — the round trip; `GetOne` returns the
  manufacturer's name; the manufacturer's list contains the id.
- `AddModel_ToAnUnknownManufacturer_IsNotFound` — `ErrorType.NotFound` from the handler, not a
  validation error; pins Decision 6.
- `AddModel_WithANameAlreadyUsedByThatManufacturer_IsRefused` — `Error.Code == "Name"`,
  `'Name' is already in use.`, `ErrorType.Validation`; nothing written.
- `AddModel_WithANameUsedByAnotherManufacturer_IsAllowed` — the same name under two manufacturers
  both succeed; this is what makes the rule per-manufacturer.
- `UniqueIndex_IsTheGuaranteeUnderTheRace` — two scopes each `GetWithModelsAsync` the same
  manufacturer, each `AddModel` the same name (both see no duplicate), save both:
  the second `SaveChangesAsync` throws `DbUpdateException` with `PostgresException.SqlState == 23505`.
  The race, made deterministic — and proof that the aggregate's check is not the guarantee.
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
  `…UnknownDeviceModel…` with `Error.Code == nameof(CreateDeviceCommand.DeviceModelId)` and the
  `DeviceModel` existence message; the multi-error test asserts the model error alongside the
  device-id error; helpers add a model.
- `Validation/CommandValidationTests` — the empty-command test asserts `DeviceModelId` instead of
  `ManufacturerId`.
- `Shell/ModulePageRenderingTests` — `/dm/manufacturers/{guid}/models/new` and
  `/dm/manufacturers/{guid}/models/{guid}` render server-side and interactively (the detail page
  renders its problem panel for random ids, which is still a render).

Unchanged on purpose: `Devices/DeviceListQueryTests` constructs no device command (it exercises the
manufacturer list query the create-device picker depends on) — untouched.
`Validation/ValidationProblemRenderingTests` builds a synthetic `Error.Validation("ManufacturerId", …)`
— it tests rendering, not the device command, and the string is only a label.

## Documentation

- `src/Modules/DeviceManagement/CLAUDE.md` — the `Manufacturers` section grows a `DeviceModel`
  subsection (child entity, `AddModel` as the only constructor, `GetWithModelsAsync` requirement,
  why not `OwnsMany`, the repository-held boundary, the slices table, routes, the shape-only
  validator and the two rules deliberately absent); the "unfinished, dangling piece" paragraph
  deleted; the `Devices` section rewritten for `DeviceModelId`, the "model only" read models, the
  model join, the gRPC fields, and the validator's local `MustAsync`; the stale "Permission: none"
  on both `GetList` rows corrected to `permission.dm.manufacturers.read` / `permission.dm.devices.read`
  (the attributes are already there); the `Repositories` section updated — `ManufacturerRepository`
  is no longer a one-line declaration.
- Root `.claude/CLAUDE.md` — the mentions of `MustExistIn(manufacturers)` on `ManufacturerId`, the
  `dm.device.manufacturer_id → dm.manufacturer.id` FK, and "ManufacturerRepository is a one-line
  declaration" updated; a sentence under "Per-module project layering / `.Domain`" recording that a
  child entity derives from `Entity<TId>`, lives in its aggregate's folder, is created only through
  the root, and — when something outside the aggregate holds an FK to it — is mapped as a regular
  entity type, not `OwnsMany`.
- `graphify update .` + `python .claude/skills/graphify/scripts/graphify_repair.py` after the code
  lands.

## Rollout

Worktree `feat/device-models` off `main`; `git submodule update --init --recursive` inside it
before the first build. No SharedKernel or UserAccess change — this repo only. Order: domain +
EF + migration → Application slices with their tests → Infrastructure → WebApi → gRPC → Blazor →
docs. `dotnet build Sergin.MeterMinder.slnx` must be warning-free under the analyzers; the full
integration suite runs against Testcontainers (Docker Desktop up first).
