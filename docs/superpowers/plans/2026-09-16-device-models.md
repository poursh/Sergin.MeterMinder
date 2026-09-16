# Device Models Under Manufacturers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `DeviceModel` a real entity inside the `Manufacturer` aggregate, and re-point `Device` from `ManufacturerId` to `DeviceModelId`.

**Architecture:** `DeviceModel : Entity<DeviceModelInternalId>` lives in `Domain/Manufacturers/` and is created only through `Manufacturer.AddModel(name)`, which also enforces name-uniqueness within the manufacturer. It is mapped as a regular EF entity type (not `OwnsMany` — `Device` holds an FK to it, and EF refuses an owned type as the principal of a non-ownership relationship), with the aggregate boundary held by the repository layer: no `IDeviceModelRepository`, no `DbSet`. Three slices on the `Manufacturers` aggregate (`AddDeviceModel`, `GetDeviceModel`, `GetDeviceModelList`) plus the `Device` swap ripple through Contracts, Application, Infrastructure, WebApi, gRPC, Blazor, one destructive migration, and the integration tests.

**Tech Stack:** .NET 10, EF Core (Npgsql, snake_case), Dapper raw SQL, MediatR, FluentValidation, ErrorOr, MudBlazor, gRPC, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-09-16-device-models-design.md`

## Global Constraints

- `Directory.Build.props`: `TreatWarningsAsErrors=true`, `AnalysisMode=All`, SonarAnalyzer, `EnforceCodeStyleInBuild`. Any warning fails the build. File-scoped namespaces are mandatory (IDE0161).
- Central Package Management: no new packages are needed by this plan; never add a `Version` attribute to a `PackageReference`.
- Every module page route starts with `/dm/`; every WebApi route string omits the schema (`/manufacturers/...`, the host adds `/dm`).
- `.razor` files are markup-only; all C# goes in the `.razor.cs` partial class. Zero `@code` blocks.
- Inject `ISerginDispatcher` in Blazor, never `ISender`. WebApi endpoints inject `ISender` directly.
- IDs: `Guid.CreateVersion7()`, never `Guid.NewGuid()`.
- Domain value objects: trailing `sealed record`s in the owning entity's file. `Create` uses object-initializer syntax against the private parameterless constructor.
- The existing misspellings `DeviceIntenralId` and `IDeviceAllQueryRepositoriy` are real type names — match them.
- Aliases in raw SQL: `dm` is the schema, so the `device_model` table alias is `dm_`.
- Permission on the two model queries: `permission.dm.manufacturers.read` (already granted in `appsettings.json`; no UserAccess change).
- Message texts, verbatim: `'Name' is already in use.` (from `Manufacturer.AddModel`), `'Device Model Id' must refer to an existing DeviceModel.` (from `CreateDeviceCommandValidator`).
- All work happens in a git worktree `feat/device-models` (Task 1 creates it). Run `git submodule update --init --recursive` there before the first build. Every `dotnet` command below is run from the worktree root.
- Commit messages: plain English, imperative, no `Co-Authored-By` trailer (repo rule).
- Full solution build: `dotnet build Sergin.MeterMinder.slnx`. Full tests: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj` (needs Docker Desktop running).

## File Structure

`M` = `src/Modules/DeviceManagement`.

| Project | Create | Modify | Delete |
|---|---|---|---|
| `M/…Domain` | `Manufacturers/DeviceModel.cs` | `Manufacturers/Manufacturer.cs`, `Manufacturers/IManufacturerRepository.cs`, `Devices/Device.cs` | `DeviceModels/DeviceModel.cs` |
| `M/…Application.Contracts` | `Manufacturers/Commands/AddDeviceModel/{AddDeviceModelCommand,AddDeviceModelCommandResponse}.cs`, `Manufacturers/Commands/GetDeviceModel/{GetDeviceModelByIdQueryCommand,DeviceModelQueryResponse}.cs`, `Manufacturers/Commands/GetDeviceModelList/{GetDeviceModelListQueryCommand,GetDeviceModelListItem}.cs` | `Devices/Commands/Create/CreateDeviceCommand.cs`, `Devices/Commands/GetOne/DeviceQueryResponse.cs`, `Devices/Commands/GetList/GetDeviceListItem.cs` | |
| `M/…Application` | `Manufacturers/Commands/AddDeviceModel/{AddDeviceModelCommandHandler,AddDeviceModelCommandValidator}.cs`, `Manufacturers/Commands/GetDeviceModel/{GetDeviceModelByIdQueryCommandHandler,IGetDeviceModelQueryRepository}.cs`, `Manufacturers/Commands/GetDeviceModelList/{GetDeviceModelListQueryCommandHandler,IGetDeviceModelListQueryRepository}.cs` | `Manufacturers/IManufacturerAllQueryRepository.cs`, `Devices/Commands/Create/{CreateDeviceCommandHandler,CreateDeviceCommandValidator}.cs` | |
| `M/…Infrastructure.Data` | `Manufacturers/Converters/{DeviceModelInternalIdConverter,DeviceModelNameConverter}.cs`, `Manufacturers/DeviceModelEntityTypeConfiguration.cs`, `Migrations/<ts>_AddDeviceModels.cs` (+ Designer, snapshot) | `Manufacturers/ManufacturerEntityTypeConfiguration.cs`, `Devices/DeviceEntityTypeConfiguration.cs` | |
| `M/…Infrastructure` | | `Manufacturers/Repositories/ManufacturerRepository.cs`, `Manufacturers/Repositories/Queries/ManufacturerQueryRepository.cs`, `Devices/Repositories/Queries/DeviceQueryRepository.cs` | |
| `M/Sergin.MeterMinder.DeviceManagement` | | `Manufacturers/ManufacturerInstallationExtensions.cs` | |
| `M/…Presentation.WebApi` | `Manufacturers/Endpoints/AddDeviceModel/{AddDeviceModelEndpoint,NewDeviceModelModel}.cs`, `Manufacturers/Endpoints/GetDeviceModel/GetDeviceModelEndpoint.cs`, `Manufacturers/Endpoints/GetDeviceModelList/GetDeviceModelListEndpoint.cs` | `Devices/Endpoints/Create/{CreateDeviceEndpoint,NewDeviceModel}.cs` | |
| `M/…Presentation.Grpc.*` | | `Contracts/Protos/devices.proto`, `Server/Devices/DeviceGrpcService.cs`, `Client/Devices/GetDeviceByIdGrpcInvoker.cs` | |
| `M/…Presentation.Blazor` | `Manufacturers/Models/NewDeviceModelFormModel.cs`, `Manufacturers/Pages/AddDeviceModelPage.razor(.cs)`, `Manufacturers/Pages/DeviceModelDetailPage.razor(.cs)` | `_Imports.razor`, `Manufacturers/Pages/ManufacturerDetailPage.razor(.cs)`, `Devices/Models/NewDeviceFormModel.cs`, `Devices/Pages/CreateDevicePage.razor(.cs)`, `Devices/Pages/DeviceListPage.razor`, `Devices/Pages/DeviceDetailPage.razor` | |
| `tests/…IntegrationTests.All` | `Manufacturers/DeviceModelTests.cs` | `Devices/DeviceReadModelTests.cs`, `Devices/DeviceGrpcRoundTripTests.cs`, `Validation/RepositoryRuleTests.cs`, `Validation/CommandValidationTests.cs`, `Shell/ModulePageRenderingTests.cs` | |
| docs | | `src/Modules/DeviceManagement/CLAUDE.md`, `.claude/CLAUDE.md` | |

**Compile order matters.** Tasks 1–7 each end with a project-level `dotnet build` of the project(s) touched; the full solution does not compile until Task 8. The migration (Task 9) needs the whole solution to compile because `dotnet ef` builds the host. All tests are integration tests over the compiled stack, so the red/green cycle for this feature is: Task 8 makes the updated tests compile, Task 9's migration makes them runnable, Task 10 adds the new tests and runs everything.

---

### Task 1: Domain — `DeviceModel` entity, `Manufacturer.AddModel`, repository lookups, `Device` swap

**Files:**
- Create: `M/Sergin.MeterMinder.DeviceManagement.Domain/Manufacturers/DeviceModel.cs`
- Modify: `M/Sergin.MeterMinder.DeviceManagement.Domain/Manufacturers/Manufacturer.cs`
- Modify: `M/Sergin.MeterMinder.DeviceManagement.Domain/Manufacturers/IManufacturerRepository.cs`
- Modify: `M/Sergin.MeterMinder.DeviceManagement.Domain/Devices/Device.cs`
- Delete: `M/Sergin.MeterMinder.DeviceManagement.Domain/DeviceModels/DeviceModel.cs` (and the now-empty `DeviceModels/` folder)

**Interfaces:**
- Produces: `DeviceModel` (`Id: DeviceModelInternalId`, `ManufacturerId`, `Name: DeviceModelName`), `DeviceModelInternalId(Guid Value)`, `DeviceModelName(string Value)` with `MaxLength = 200`, `Manufacturer.Models: IReadOnlyCollection<DeviceModel>`, `Manufacturer.AddModel(DeviceModelName) : ErrorOr<DeviceModel>`, `IManufacturerRepository.GetWithModelsAsync(ManufacturerId, CancellationToken) : Task<Manufacturer?>`, `IManufacturerRepository.ModelExistsAsync(DeviceModelInternalId, CancellationToken) : Task<bool>`, `Device.DeviceModelId : DeviceModelInternalId`, `Device.Create(DeviceId, DeviceModelInternalId)`.

- [ ] **Step 1: Create the worktree and initialise submodules**

```bash
git worktree add ../Sergin.MeterMinder-device-models -b feat/device-models main
cd ../Sergin.MeterMinder-device-models
git submodule update --init --recursive
```

Expected: both `src/SharedKernel` and `src/Modules/UserAccess` populated. Every later command runs from this directory.

- [ ] **Step 2: Delete the stub**

```bash
git rm src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Domain/DeviceModels/DeviceModel.cs
```

- [ ] **Step 3: Write `Manufacturers/DeviceModel.cs`**

```csharp
using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

/// <summary>
/// A manufacturer's product line, an entity inside the Manufacturer aggregate. It has no life of its own:
/// <see cref="Manufacturer.AddModel"/> is the only way one comes into being, and there is no repository for it.
/// </summary>
public class DeviceModel : Entity<DeviceModelInternalId>
{
    private DeviceModel() { }

    // The owner's id. EF needs it as the foreign key, and the entity knowing which manufacturer it belongs
    // to costs nothing.
    public ManufacturerId ManufacturerId { get; private set; }

    public DeviceModelName Name { get; private set; }

    // internal: Manufacturer.AddModel is the only caller.
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

// MaxLength: the one number AddDeviceModelCommandValidator and NewDeviceModelFormModel's [StringLength] both
// read, so the limit is never repeated as a literal.
public sealed record DeviceModelName(string Value)
{
    public const int MaxLength = 200;
}
```

- [ ] **Step 4: Rewrite `Manufacturers/Manufacturer.cs`**

```csharp
using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

public class Manufacturer : AggregateRoot<ManufacturerId>
{
    private readonly List<DeviceModel> models = [];

    private Manufacturer() { }

    public ManufacturerName Name { get; private set; }
    public ManufacturerAddress? Address { get; private set; }

    public IReadOnlyCollection<DeviceModel> Models => models;

    public static Manufacturer Create(ManufacturerName name, ManufacturerAddress? address = null)
    {
        return new Manufacturer
        {
            Id = new ManufacturerId(Guid.CreateVersion7()),
            Name = name,
            Address = address
        };
    }

    /// <summary>
    /// The only way a model comes into being. Refuses a name this manufacturer already uses — the invariant
    /// the aggregate exists to hold; <c>ix_device_model_manufacturer_id_name</c> is the guarantee under a race.
    /// Requires the manufacturer to have been loaded with its models
    /// (<c>IManufacturerRepository.GetWithModelsAsync</c>); on a manufacturer loaded through <c>GetAsync</c>
    /// the collection is empty and the check cannot see existing names.
    /// </summary>
    public ErrorOr<DeviceModel> AddModel(DeviceModelName name)
    {
        if (models.Any(model => model.Name == name))
        {
            // Error.Validation, so it renders through the path already built for validator errors:
            // Description shown, one snackbar in Blazor, one ValidationProblem entry on the API. The code and
            // text are what MustBeUniqueIn would have produced for a Name property.
            return Error.Validation(nameof(DeviceModel.Name), "'Name' is already in use.");
        }

        DeviceModel model = DeviceModel.Create(Id, name);
        models.Add(model);

        return model;
    }
}

public sealed record ManufacturerId(Guid Value);

// MaxLength: read by CreateManufacturerCommandValidator. The columns are unbounded text today, so these
// are the only limits; widen here and the validator follows.
public sealed record ManufacturerName(string Value)
{
    public const int MaxLength = 200;
}

public sealed record ManufacturerAddress(string Value)
{
    public const int MaxLength = 500;
}
```

- [ ] **Step 5: Rewrite `Manufacturers/IManufacturerRepository.cs`**

```csharp
using Sergin.SharedKernel.Domain.Repositories;

namespace Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

public interface IManufacturerRepository : IRepository<Manufacturer, ManufacturerId>
{
    /// <summary>
    /// The aggregate with its <see cref="Manufacturer.Models"/> loaded — what <see cref="Manufacturer.AddModel"/>
    /// needs. <c>GetAsync</c> is a <c>FindAsync</c>, which loads no navigation.
    /// </summary>
    Task<Manufacturer?> GetWithModelsAsync(ManufacturerId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether any manufacturer has this model — a <c>SELECT EXISTS</c> on <c>dm.device_model</c>, nothing
    /// loaded. The one place a model is looked up from outside its aggregate: <c>CreateDeviceCommandValidator</c>.
    /// </summary>
    Task<bool> ModelExistsAsync(DeviceModelInternalId id, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 6: Rewrite `Devices/Device.cs`**

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.DeviceManagement.Domain.Devices;

public class Device : AggregateRoot<DeviceIntenralId>
{
    private Device() { }

    public DeviceId DeviceId { get; private set; }

    // A reference across the aggregate boundary to an entity inside Manufacturer. The manufacturer itself is
    // reachable only through the model — Device deliberately does not store it twice.
    public DeviceModelInternalId DeviceModelId { get; private set; }

    public static Device Create(DeviceId deviceId, DeviceModelInternalId deviceModelId)
    {
        return new Device
        {
            Id = new DeviceIntenralId(Guid.CreateVersion7()),
            DeviceId = deviceId,
            DeviceModelId = deviceModelId
        };
    }
}

public sealed record DeviceIntenralId(Guid Value);

// MaxLength: the one number CreateDeviceCommandValidator and NewDeviceFormModel's [StringLength] both
// read, so the limit is never repeated as a literal.
public sealed record DeviceId(string Value)
{
    public const int MaxLength = 100;
}
```

- [ ] **Step 7: Build the Domain project**

Run: `dotnet build src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Domain/Sergin.MeterMinder.DeviceManagement.Domain.csproj`

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. (Dependants do not compile yet — that is Tasks 2–7.)

- [ ] **Step 8: Commit**

```bash
git add -A src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Domain
git commit -m "Make DeviceModel an entity of the Manufacturer aggregate and point Device at it"
```

---

### Task 2: Application.Contracts + Application — the three slices and the `Device` swap

**Files:**
- Create (Contracts, root `M/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Manufacturers/Commands/`): `AddDeviceModel/AddDeviceModelCommand.cs`, `AddDeviceModel/AddDeviceModelCommandResponse.cs`, `GetDeviceModel/GetDeviceModelByIdQueryCommand.cs`, `GetDeviceModel/DeviceModelQueryResponse.cs`, `GetDeviceModelList/GetDeviceModelListQueryCommand.cs`, `GetDeviceModelList/GetDeviceModelListItem.cs`
- Modify (Contracts): `Devices/Commands/Create/CreateDeviceCommand.cs`, `Devices/Commands/GetOne/DeviceQueryResponse.cs`, `Devices/Commands/GetList/GetDeviceListItem.cs`
- Create (Application, root `M/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/Commands/`): `AddDeviceModel/AddDeviceModelCommandHandler.cs`, `AddDeviceModel/AddDeviceModelCommandValidator.cs`, `GetDeviceModel/GetDeviceModelByIdQueryCommandHandler.cs`, `GetDeviceModel/IGetDeviceModelQueryRepository.cs`, `GetDeviceModelList/GetDeviceModelListQueryCommandHandler.cs`, `GetDeviceModelList/IGetDeviceModelListQueryRepository.cs`
- Modify (Application): `Manufacturers/IManufacturerAllQueryRepository.cs`, `Devices/Commands/Create/CreateDeviceCommandHandler.cs`, `Devices/Commands/Create/CreateDeviceCommandValidator.cs`

**Interfaces:**
- Consumes: everything Task 1 produces.
- Produces: `AddDeviceModelCommand(ManufacturerId ManufacturerId, DeviceModelName Name)` → `AddDeviceModelCommandResponse(Guid Id)`; `GetDeviceModelByIdQueryCommand(Guid ManufacturerId, Guid Id)` → `DeviceModelQueryResponse(Guid Id, Guid ManufacturerId, string ManufacturerName, string Name)`; `GetDeviceModelListQueryCommand(ManufacturerId manufacturerId, Paggination paggination, Term? term = default, Filtering? filtering = default, Sorting? sorting = default)` with property `ManufacturerId ManufacturerId`, items `GetDeviceModelListItem(Guid Id, string Name)`; `IGetDeviceModelQueryRepository.GetDeviceModelById(ManufacturerId, DeviceModelInternalId, CancellationToken) : Task<DeviceModelQueryResponse?>`; `IGetDeviceModelListQueryRepository.GetListAsync(ManufacturerId, ListQuery, CancellationToken) : Task<ListQueryResponse<GetDeviceModelListItem>>`; `CreateDeviceCommand(DeviceId DeviceId, DeviceModelInternalId DeviceModelId)`; `DeviceQueryResponse(Guid Id, string DeviceId, Guid DeviceModelId, string DeviceModelName)`; `GetDeviceListItem(Guid Id, string DeviceId, Guid DeviceModelId, string DeviceModelName)`.

- [ ] **Step 1: Contracts — `AddDeviceModel/AddDeviceModelCommand.cs`**

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;

public sealed record AddDeviceModelCommand(ManufacturerId ManufacturerId, DeviceModelName Name) : ICommand<AddDeviceModelCommandResponse>;
```

- [ ] **Step 2: Contracts — `AddDeviceModel/AddDeviceModelCommandResponse.cs`**

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;

public sealed record AddDeviceModelCommandResponse(Guid Id);
```

- [ ] **Step 3: Contracts — `GetDeviceModel/GetDeviceModelByIdQueryCommand.cs`**

```csharp
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModel;

// Keyed by both ids: a model addressed under the wrong manufacturer is NotFound, not served, so the nested
// route /manufacturers/{manufacturerId}/models/{modelId} carries no ignored segment.
[RequiredPermissions("permission.dm.manufacturers.read")]
public sealed record GetDeviceModelByIdQueryCommand(Guid ManufacturerId, Guid Id) : IQuery<DeviceModelQueryResponse>;
```

- [ ] **Step 4: Contracts — `GetDeviceModel/DeviceModelQueryResponse.cs`**

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModel;

public sealed record DeviceModelQueryResponse(Guid Id, Guid ManufacturerId, string ManufacturerName, string Name);
```

- [ ] **Step 5: Contracts — `GetDeviceModelList/GetDeviceModelListItem.cs`**

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModelList;

public sealed record GetDeviceModelListItem(Guid Id, string Name);
```

- [ ] **Step 6: Contracts — `GetDeviceModelList/GetDeviceModelListQueryCommand.cs`**

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModelList;

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

    // The filter every read of this list must carry. Not routed through Filtering: no query repository reads
    // it, and it would make a mandatory scope optional.
    public ManufacturerId ManufacturerId { get; }
}
```

- [ ] **Step 7: Contracts — rewrite the three `Devices` records**

`Devices/Commands/Create/CreateDeviceCommand.cs`:

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;

public sealed record CreateDeviceCommand(DeviceId DeviceId, DeviceModelInternalId DeviceModelId) : ICommand<CreateDeviceCommandResponse>;
```

`Devices/Commands/GetOne/DeviceQueryResponse.cs`:

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;

public sealed record DeviceQueryResponse(Guid Id, string DeviceId, Guid DeviceModelId, string DeviceModelName);
```

`Devices/Commands/GetList/GetDeviceListItem.cs`:

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;

public sealed record GetDeviceListItem(Guid Id, string DeviceId, Guid DeviceModelId, string DeviceModelName);
```

- [ ] **Step 8: Build Contracts**

Run: `dotnet build src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj`

Expected: `Build succeeded.`

- [ ] **Step 9: Application — `GetDeviceModel/IGetDeviceModelQueryRepository.cs`**

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModel;

public interface IGetDeviceModelQueryRepository
{
    Task<DeviceModelQueryResponse?> GetDeviceModelById(
        ManufacturerId manufacturerId, DeviceModelInternalId id, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 10: Application — `GetDeviceModel/GetDeviceModelByIdQueryCommandHandler.cs`**

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModel;

internal sealed class GetDeviceModelByIdQueryCommandHandler(IGetDeviceModelQueryRepository repository)
    : IQueryHandler<GetDeviceModelByIdQueryCommand, DeviceModelQueryResponse>
{
    public async Task<ErrorOr<DeviceModelQueryResponse>> Handle(GetDeviceModelByIdQueryCommand request, CancellationToken cancellationToken)
    {
        DeviceModelQueryResponse? res = await repository.GetDeviceModelById(
            new ManufacturerId(request.ManufacturerId), new DeviceModelInternalId(request.Id), cancellationToken);

        if (res is null)
        {
            return Error.NotFound();
        }

        return res;
    }
}
```

- [ ] **Step 11: Application — `GetDeviceModelList/IGetDeviceModelListQueryRepository.cs`**

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModelList;

public interface IGetDeviceModelListQueryRepository
{
    // The manufacturer is a first-class argument, not something read out of query.Filtering — see
    // GetDeviceModelListQueryCommand. The base ListQuery is enough for the rest: only Paggination is read.
    Task<ListQueryResponse<GetDeviceModelListItem>> GetListAsync(
        ManufacturerId manufacturerId, ListQuery query, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 12: Application — `GetDeviceModelList/GetDeviceModelListQueryCommandHandler.cs`**

```csharp
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModelList;

internal sealed class GetDeviceModelListQueryCommandHandler(IGetDeviceModelListQueryRepository queryRepository)
    : IListQueryHandler<GetDeviceModelListQueryCommand, GetDeviceModelListItem>
{
    public async Task<ErrorOr<ListQueryResponse<GetDeviceModelListItem>>> Handle(
        GetDeviceModelListQueryCommand request, CancellationToken cancellationToken)
    {
        ListQueryResponse<GetDeviceModelListItem> res =
            await queryRepository.GetListAsync(request.ManufacturerId, request, cancellationToken);

        return res;
    }
}
```

- [ ] **Step 13: Application — `AddDeviceModel/AddDeviceModelCommandHandler.cs`**

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;

// A mutation of an existing aggregate, so the handler owns not-found (the DeactivateUser shape) and the
// aggregate owns the uniqueness rule. The validator checks shape only.
internal sealed class AddDeviceModelCommandHandler(
    IDeviceManagementUnitOfWork unitOfWork,
    IManufacturerRepository repository) : ICommandHandler<AddDeviceModelCommand, AddDeviceModelCommandResponse>
{
    public async Task<ErrorOr<AddDeviceModelCommandResponse>> Handle(
        AddDeviceModelCommand request, CancellationToken cancellationToken)
    {
        // GetWithModelsAsync, not GetAsync: AddModel checks the name against the loaded collection.
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

        // EF tracks the new entity through the Models navigation of the tracked manufacturer; nothing is
        // inserted explicitly.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AddDeviceModelCommandResponse(added.Value.Id.Value);
    }
}
```

- [ ] **Step 14: Application — `AddDeviceModel/AddDeviceModelCommandValidator.cs`**

```csharp
using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;

// Shape only. Two rules are deliberately absent: manufacturer existence (the handler loads the aggregate and
// answers NotFound — a MustExistIn here would query for an answer the handler is about to get anyway) and name
// uniqueness (Manufacturer.AddModel holds that invariant; the composite unique index is the guarantee).
internal sealed class AddDeviceModelCommandValidator : AbstractValidator<AddDeviceModelCommand>
{
    public AddDeviceModelCommandValidator()
    {
        RuleFor(x => x.ManufacturerId.Value)
            .NotEmpty()
            .OverridePropertyName(nameof(AddDeviceModelCommand.ManufacturerId));

        RuleFor(x => x.Name.Value)
            .NotEmpty()
            .MaximumLength(DeviceModelName.MaxLength)
            .OverridePropertyName(nameof(AddDeviceModelCommand.Name));
    }
}
```

- [ ] **Step 15: Application — rewrite `Manufacturers/IManufacturerAllQueryRepository.cs`**

```csharp
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModel;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModelList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers;
public interface IManufacturerAllQueryRepository :
    IGetManufacturerListQueryRepository,
    IGetManufacturerQueryRepository,
    IGetDeviceModelQueryRepository,
    IGetDeviceModelListQueryRepository;
```

- [ ] **Step 16: Application — update `Devices/Commands/Create/CreateDeviceCommandHandler.cs`**

Change the one line `var newDevice = Device.Create(request.DeviceId, request.ManufacturerId);` to:

```csharp
        var newDevice = Device.Create(request.DeviceId, request.DeviceModelId);
```

- [ ] **Step 17: Application — rewrite `Devices/Commands/Create/CreateDeviceCommandValidator.cs`**

```csharp
using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Validations;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;

// Shape rules target the value object's Value and OverridePropertyName restores the command property name:
// that name is what ValidationPipelineBehavior puts in Error.Code and what the API groups a
// ValidationProblem by.
//
// The repository rules target the wrapper itself, so their Error.Code is already the property name. Whether
// the model exists and whether the device id is free are answered here, as ErrorOr validation errors; the
// foreign key and the unique index on dm.device.device_id stay as the guarantee under a race. Each is
// guarded by a When so no query runs for a value the shape rule has already refused.
internal sealed class CreateDeviceCommandValidator : AbstractValidator<CreateDeviceCommand>
{
    public CreateDeviceCommandValidator(IDeviceRepository devices, IManufacturerRepository manufacturers)
    {
        RuleFor(x => x.DeviceId.Value)
            .NotEmpty()
            .MaximumLength(DeviceId.MaxLength)
            .OverridePropertyName(nameof(CreateDeviceCommand.DeviceId));

        RuleFor(x => x.DeviceModelId.Value)
            .NotEmpty()
            .OverridePropertyName(nameof(CreateDeviceCommand.DeviceModelId));

        RuleFor(x => x.DeviceId)
            .MustBeUniqueIn(devices)
            .When(x => !string.IsNullOrWhiteSpace(x.DeviceId.Value));

        // A local MustAsync, not MustExistIn: that extension is generic over IRepository<TAggregateRoot, TId>,
        // and a model is an entity inside the Manufacturer aggregate, not a root with a repository. The
        // message mirrors MustExistIn's so the two read alike.
        RuleFor(x => x.DeviceModelId)
            .MustAsync(manufacturers.ModelExistsAsync)
            .WithMessage("'{PropertyName}' must refer to an existing DeviceModel.")
            .When(x => x.DeviceModelId.Value != Guid.Empty);
    }
}
```

- [ ] **Step 18: Build Application**

Run: `dotnet build src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Sergin.MeterMinder.DeviceManagement.Application.csproj`

Expected: `Build succeeded.` If Sonar flags the `MustAsync(manufacturers.ModelExistsAsync)` method group (it does not for `MustExistIn`'s identical shape), wrap as `MustAsync((id, ct) => manufacturers.ModelExistsAsync(id, ct))`.

- [ ] **Step 19: Commit**

```bash
git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application
git commit -m "Add the AddDeviceModel, GetDeviceModel and GetDeviceModelList slices; devices reference a model"
```

---

### Task 3: Infrastructure.Data — converters and entity configurations

**Files:**
- Create: `M/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Manufacturers/Converters/DeviceModelInternalIdConverter.cs`
- Create: `M/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Manufacturers/Converters/DeviceModelNameConverter.cs`
- Create: `M/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Manufacturers/DeviceModelEntityTypeConfiguration.cs`
- Modify: `M/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Manufacturers/ManufacturerEntityTypeConfiguration.cs`
- Modify: `M/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Devices/DeviceEntityTypeConfiguration.cs`

**Interfaces:**
- Consumes: Task 1's `DeviceModel`, `DeviceModelInternalId`, `DeviceModelName`, `Manufacturer.Models`, `Device.DeviceModelId`.
- Produces: table `dm.device_model(id uuid PK, manufacturer_id uuid FK→dm.manufacturer ON DELETE CASCADE, name text)` with unique index `ix_device_model_manufacturer_id_name`; `dm.device.device_model_id uuid FK→dm.device_model ON DELETE RESTRICT`. `DeviceModelInternalIdConverter` is reused by `DeviceEntityTypeConfiguration`.

- [ ] **Step 1: `Manufacturers/Converters/DeviceModelInternalIdConverter.cs`**

```csharp
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers.Converters;

internal sealed class DeviceModelInternalIdConverter : ValueConverter<DeviceModelInternalId, Guid>
{
    private static readonly ConverterMappingHints defaultHints = new();

    public DeviceModelInternalIdConverter() : this(null)
    {
    }

    public DeviceModelInternalIdConverter(ConverterMappingHints? mappingHints)
        : base(
                convertToProviderExpression: x => x.Value,
                convertFromProviderExpression: x => new DeviceModelInternalId(x),
                mappingHints: defaultHints.With(mappingHints))
    {
    }
}
```

- [ ] **Step 2: `Manufacturers/Converters/DeviceModelNameConverter.cs`**

```csharp
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers.Converters;

internal sealed class DeviceModelNameConverter : ValueConverter<DeviceModelName, string>
{
    private static readonly ConverterMappingHints defaultHints = new();

    public DeviceModelNameConverter() : this(null)
    {
    }

    public DeviceModelNameConverter(ConverterMappingHints? mappingHints)
        : base(
                convertToProviderExpression: x => x.Value,
                convertFromProviderExpression: x => new DeviceModelName(x),
                mappingHints: defaultHints.With(mappingHints))
    {
    }
}
```

- [ ] **Step 3: `Manufacturers/DeviceModelEntityTypeConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers.Converters;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers;

/// <summary>
/// The entity's own shape. Its relationship to the owner is configured from the owner's side, in
/// <see cref="ManufacturerEntityTypeConfiguration"/>, which also explains why this is a regular entity type and
/// not an owned one.
/// </summary>
internal sealed class DeviceModelEntityTypeConfiguration : IEntityTypeConfiguration<DeviceModel>
{
    public void Configure(EntityTypeBuilder<DeviceModel> builder)
    {
        builder.HasKey(model => model.Id);

        builder.Property(model => model.Id)
            .HasConversion<DeviceModelInternalIdConverter>()
            .ValueGeneratedNever();

        builder.Property(model => model.ManufacturerId)
            .HasConversion<ManufacturerIdConverter>();

        builder.Property(model => model.Name)
            .HasConversion<DeviceModelNameConverter>()
            .IsRequired();

        // Manufacturer.AddModel refuses a duplicate name; this is the guarantee under a race.
        builder.HasIndex(model => new { model.ManufacturerId, model.Name }).IsUnique();
    }
}
```

- [ ] **Step 4: Rewrite `Manufacturers/ManufacturerEntityTypeConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers.Converters;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers;

internal sealed class ManufacturerEntityTypeConfiguration : IEntityTypeConfiguration<Manufacturer>
{
    public void Configure(EntityTypeBuilder<Manufacturer> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasConversion<ManufacturerIdConverter>()
            .ValueGeneratedNever();

        builder.Property(m => m.Name)
            .HasConversion<ManufacturerNameConverter>()
            .IsRequired();

        builder.Property(m => m.Address)
            .HasConversion<ManufacturerAddressConverter>()
            .IsRequired(false);

        // Not OwnsMany, though Role.Permissions and User.Roles are: Device holds a foreign key to a model, and
        // EF refuses an owned type on the principal side of a non-ownership relationship
        // (CoreStrings.PrincipalOwnedType). DeviceModel is a regular entity type instead, and the aggregate
        // boundary is held by the repository layer — no DbSet, no repository of its own, written only through
        // Manufacturer.Models. Cascade: a model has no life outside its manufacturer.
        builder.HasMany(m => m.Models)
            .WithOne()
            .HasForeignKey(model => model.ManufacturerId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(m => m.Models)
            .HasField("models")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
```

- [ ] **Step 5: Rewrite `Devices/DeviceEntityTypeConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Devices.Converters;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers.Converters;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Devices;

internal sealed class DeviceEntityTypeConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasConversion<DeviceInternalIdConverter>()
            .ValueGeneratedNever();

        builder.Property(d => d.DeviceId)
            .HasConversion<DeviceIdConverter>();

        // IDeviceRepository declares DeviceId an alternate key; the validator's check is advisory, this is the guarantee.
        builder.HasIndex(d => d.DeviceId).IsUnique();

        builder.Property(d => d.DeviceModelId)
            .HasConversion<DeviceModelInternalIdConverter>();

        // Restrict, not the default cascade: a reference across an aggregate boundary must never delete the
        // referrer.
        builder.HasOne<DeviceModel>()
            .WithMany()
            .HasForeignKey(d => d.DeviceModelId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 6: Build Infrastructure.Data**

Run: `dotnet build src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.csproj`

Expected: `Build succeeded.` (The model snapshot is stale until Task 9; that is not a compile error.)

- [ ] **Step 7: Commit**

```bash
git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data
git commit -m "Map DeviceModel as a regular entity type under Manufacturer and re-point the device FK"
```

---

### Task 4: Infrastructure — repository lookups and the raw-SQL reads

**Files:**
- Modify: `M/Sergin.MeterMinder.DeviceManagement.Infrastructure/Manufacturers/Repositories/ManufacturerRepository.cs`
- Modify: `M/Sergin.MeterMinder.DeviceManagement.Infrastructure/Manufacturers/Repositories/Queries/ManufacturerQueryRepository.cs`
- Modify: `M/Sergin.MeterMinder.DeviceManagement.Infrastructure/Devices/Repositories/Queries/DeviceQueryRepository.cs`

**Interfaces:**
- Consumes: `IManufacturerRepository.GetWithModelsAsync/ModelExistsAsync` (Task 1), `IGetDeviceModelQueryRepository`, `IGetDeviceModelListQueryRepository`, `IManufacturerAllQueryRepository` (Task 2), `DeviceQueryResponse`/`GetDeviceListItem` new shapes (Task 2).
- Produces: the implementations; the SQL column aliases `deviceModelId`, `deviceModelName`, `manufacturerId`, `manufacturerName` that Dapper binds by name.

- [ ] **Step 1: Rewrite `ManufacturerRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Repositories;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Manufacturers.Repositories;

internal class ManufacturerRepository(IDeviceManagementDbContext dbContext)
    : EfRepository<Manufacturer, ManufacturerId>(dbContext), IManufacturerRepository
{
    public Task<Manufacturer?> GetWithModelsAsync(ManufacturerId id, CancellationToken cancellationToken = default)
    {
        return Set.Include(m => m.Models).SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    // Through the aggregate's own Set rather than dbContext.Set<DeviceModel>(): capturing the primary-constructor
    // parameter that is also passed to the base is CS9107, an error under TreatWarningsAsErrors. EF translates
    // the SelectMany into an EXISTS over dm.device_model; nothing is loaded.
    public Task<bool> ModelExistsAsync(DeviceModelInternalId id, CancellationToken cancellationToken = default)
    {
        return Set.SelectMany(m => m.Models).AnyAsync(model => model.Id == id, cancellationToken);
    }
}
```

- [ ] **Step 2: Rewrite `ManufacturerQueryRepository.cs`**

```csharp
using System.Data.Common;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModel;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModelList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Infrastracture.Data;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Manufacturers.Repositories.Queries;

internal sealed class ManufacturerQueryRepository(
    IDbConnectionFactory connectionFactory) : IManufacturerAllQueryRepository
{
    public async Task<ManufacturerQueryResponse?> GetManufacturerById(
        ManufacturerId id, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        string queries =
           """
            SELECT id, name, address
            FROM dm.manufacturer
            WHERE id = @Id;
            """;

        return await connection.QuerySingleOrDefaultAsync<ManufacturerQueryResponse>(
            queries, new { Id = id.Value });
    }

    public async Task<ListQueryResponse<GetManufacturerListItem>> GetListAsync(
        ListQuery query, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        string queries =
            """
            SELECT count(*) FROM dm.manufacturer;

            SELECT id, name, address
            FROM dm.manufacturer
            ORDER BY id
            LIMIT @PageSize OFFSET @Offset;
            """;

        GridReader res = await connection.QueryMultipleAsync(
            queries, new { PageSize = query.Paggination.Size.Value, Offset = query.Paggination.Skip });

        int count = await res.ReadSingleAsync<int>();
        IReadOnlyCollection<GetManufacturerListItem> list = [.. await res.ReadAsync<GetManufacturerListItem>()];

        return new ListQueryResponse<GetManufacturerListItem>(list, count);
    }

    // dm is the schema, so the device_model alias is dm_ — the two must never read alike.
    public async Task<DeviceModelQueryResponse?> GetDeviceModelById(
        ManufacturerId manufacturerId, DeviceModelInternalId id, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        // Keyed by both ids: a model addressed under the wrong manufacturer is null, hence NotFound.
        string queries =
           """
            SELECT dm_.id, dm_.manufacturer_id AS manufacturerId, m.name AS manufacturerName, dm_.name
            FROM dm.device_model dm_
            JOIN dm.manufacturer m ON m.id = dm_.manufacturer_id
            WHERE dm_.id = @Id AND dm_.manufacturer_id = @ManufacturerId;
            """;

        return await connection.QuerySingleOrDefaultAsync<DeviceModelQueryResponse>(
            queries, new { Id = id.Value, ManufacturerId = manufacturerId.Value });
    }

    public async Task<ListQueryResponse<GetDeviceModelListItem>> GetListAsync(
        ManufacturerId manufacturerId, ListQuery query, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        string queries =
            """
            SELECT count(*) FROM dm.device_model WHERE manufacturer_id = @ManufacturerId;

            SELECT id, name
            FROM dm.device_model
            WHERE manufacturer_id = @ManufacturerId
            ORDER BY id
            LIMIT @PageSize OFFSET @Offset;
            """;

        GridReader res = await connection.QueryMultipleAsync(
            queries,
            new
            {
                ManufacturerId = manufacturerId.Value,
                PageSize = query.Paggination.Size.Value,
                Offset = query.Paggination.Skip
            });

        int count = await res.ReadSingleAsync<int>();
        IReadOnlyCollection<GetDeviceModelListItem> list = [.. await res.ReadAsync<GetDeviceModelListItem>()];

        return new ListQueryResponse<GetDeviceModelListItem>(list, count);
    }
}
```

- [ ] **Step 3: Update the two SQL statements in `DeviceQueryRepository.cs`**

Replace the `GetDeviceById` query string with:

```csharp
        string queries =
           """
            SELECT d.id, d.device_id AS deviceId, d.device_model_id AS deviceModelId, dm_.name AS deviceModelName
            FROM dm.device d
            JOIN dm.device_model dm_ ON dm_.id = d.device_model_id
            WHERE d.id = @Id;
            """;
```

Replace the `GetListAsync` query string with:

```csharp
        string queries =
            """
            SELECT count(*) FROM dm.device;

            SELECT d.id, d.device_id AS deviceId, d.device_model_id AS deviceModelId, dm_.name AS deviceModelName
            FROM dm.device d
            JOIN dm.device_model dm_ ON dm_.id = d.device_model_id
            ORDER BY d.id
            LIMIT @PageSize OFFSET @Offset;
            """;
```

Nothing else in the file changes.

- [ ] **Step 4: Build Infrastructure**

Run: `dotnet build src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure/Sergin.MeterMinder.DeviceManagement.Infrastructure.csproj`

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure
git commit -m "Implement the manufacturer model lookups and the device-model reads"
```

---

### Task 5: Composition root + WebApi endpoints

**Files:**
- Modify: `M/Sergin.MeterMinder.DeviceManagement/Manufacturers/ManufacturerInstallationExtensions.cs`
- Create: `M/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Manufacturers/Endpoints/AddDeviceModel/NewDeviceModelModel.cs`
- Create: `M/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Manufacturers/Endpoints/AddDeviceModel/AddDeviceModelEndpoint.cs`
- Create: `M/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Manufacturers/Endpoints/GetDeviceModel/GetDeviceModelEndpoint.cs`
- Create: `M/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Manufacturers/Endpoints/GetDeviceModelList/GetDeviceModelListEndpoint.cs`
- Modify: `M/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Devices/Endpoints/Create/NewDeviceModel.cs`
- Modify: `M/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Devices/Endpoints/Create/CreateDeviceEndpoint.cs`

**Interfaces:**
- Consumes: Task 2's records; `ListQueryRequestModel.ToPaggination()`, `.Term`, `.Filtering`, `.Sorting` (SharedKernel WebApi, already used by `GetManufacturerListEndpoint`).
- Produces: routes `POST /manufacturers/{manufacturerId:guid}/models`, `GET /manufacturers/{manufacturerId:guid}/models/{modelId:guid}`, `GET /manufacturers/{manufacturerId:guid}/models`; DTO `NewDeviceModel(string DeviceId, Guid DeviceModelId)`.

- [ ] **Step 1: `AddDeviceModel/NewDeviceModelModel.cs`**

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.AddDeviceModel;

public record NewDeviceModelModel(string Name);
```

- [ ] **Step 2: `AddDeviceModel/AddDeviceModelEndpoint.cs`**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.AddDeviceModel;

internal class AddDeviceModelEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder
            .MapPost("/manufacturers/{manufacturerId:guid}/models", async (
                [FromRoute] Guid manufacturerId, [FromBody] NewDeviceModelModel deviceModel, ISender sender) =>
            {
                ErrorOr<AddDeviceModelCommandResponse> res = await sender.Send(
                    new AddDeviceModelCommand(new ManufacturerId(manufacturerId), new DeviceModelName(deviceModel.Name)));

                return res.ToApiResult();
            })
            .Produces<AddDeviceModelCommandResponse>();
    }
}
```

- [ ] **Step 3: `GetDeviceModel/GetDeviceModelEndpoint.cs`**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModel;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.GetDeviceModel;

internal class GetDeviceModelEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/manufacturers/{manufacturerId:guid}/models/{modelId:guid}", async (
            [FromRoute] Guid manufacturerId, [FromRoute] Guid modelId, ISender sender) =>
        {
            ErrorOr<DeviceModelQueryResponse> res = await sender.Send(
                new GetDeviceModelByIdQueryCommand(manufacturerId, modelId));

            return res.ToApiResult();
        });
    }
}
```

- [ ] **Step 4: `GetDeviceModelList/GetDeviceModelListEndpoint.cs`**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModelList;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.GetDeviceModelList;

internal class GetDeviceModelListEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder
            .MapGet("/manufacturers/{manufacturerId:guid}/models", async (
                [FromRoute] Guid manufacturerId, [AsParameters] ListQueryRequestModel request, ISender sender) =>
            {
                ErrorOr<ListQueryResponse<GetDeviceModelListItem>> res = await sender.Send(
                    new GetDeviceModelListQueryCommand(
                        new ManufacturerId(manufacturerId),
                        request.ToPaggination(), request.Term, request.Filtering, request.Sorting));

                return res.ToApiResult();
            })
            .Produces<ListQueryResponse<GetDeviceModelListItem>>();
    }
}
```

- [ ] **Step 5: Rewrite `Devices/Endpoints/Create/NewDeviceModel.cs`**

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.Create;

// The name predates the DeviceModel entity and means "model of a new device", not "device model". Left as is.
public record NewDeviceModel(string DeviceId, Guid DeviceModelId);
```

- [ ] **Step 6: Rewrite `Devices/Endpoints/Create/CreateDeviceEndpoint.cs`**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.Create;

internal class CreateDeviceEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder
            .MapPost("/devices", async ([FromBody] NewDeviceModel device, ISender sender) =>
            {
                ErrorOr<CreateDeviceCommandResponse> res = await sender.Send(
                    new CreateDeviceCommand(
                        new DeviceId(device.DeviceId),
                        new DeviceModelInternalId(device.DeviceModelId)));

                return res.ToApiResult();
            })
            .Produces<CreateDeviceCommandResponse>();
    }
}
```

- [ ] **Step 7: Rewrite `Manufacturers/ManufacturerInstallationExtensions.cs` (composition root)**

```csharp
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModel;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModelList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Manufacturers.Repositories;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Manufacturers.Repositories.Queries;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.AddDeviceModel;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.Create;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.GetDeviceModel;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.GetDeviceModelList;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.GetList;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.GetOne;

namespace Sergin.MeterMinder.DeviceManagement.Manufacturers;

internal static class ManufacturerInstallationExtensions
{
    internal static IServiceCollection AddManufacturerDependencies(this IServiceCollection services)
    {
        services.AddTransient<IManufacturerRepository, ManufacturerRepository>();
        services.AddTransient<IManufacturerAllQueryRepository, ManufacturerQueryRepository>();
        services.AddTransient<IGetManufacturerQueryRepository, ManufacturerQueryRepository>();
        services.AddTransient<IGetManufacturerListQueryRepository, ManufacturerQueryRepository>();
        // Device models are entities of this aggregate, so their reads hang off the same query repository.
        services.AddTransient<IGetDeviceModelQueryRepository, ManufacturerQueryRepository>();
        services.AddTransient<IGetDeviceModelListQueryRepository, ManufacturerQueryRepository>();

        return services;
    }

    internal static IEndpointRouteBuilder MapManufacturerEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        new CreateManufacturerEndpoint().MapEndpoint(routeBuilder);
        new GetManufacturerEndpoint().MapEndpoint(routeBuilder);
        new GetManufacturerListEndpoint().MapEndpoint(routeBuilder);
        new AddDeviceModelEndpoint().MapEndpoint(routeBuilder);
        new GetDeviceModelEndpoint().MapEndpoint(routeBuilder);
        new GetDeviceModelListEndpoint().MapEndpoint(routeBuilder);

        return routeBuilder;
    }
}
```

- [ ] **Step 8: Build WebApi and the composition root**

Run: `dotnet build src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.csproj`

Expected: `Build succeeded.` The composition root also references the Blazor RCL, which does not compile until Task 7 — build the composition root at the end of Task 7 instead.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi
git commit -m "Expose device models as nested manufacturer endpoints and register the new reads"
```

---

### Task 6: gRPC — `DeviceData` follows the read model

**Files:**
- Modify: `M/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts/Protos/devices.proto`
- Modify: `M/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server/Devices/DeviceGrpcService.cs`
- Modify: `M/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client/Devices/GetDeviceByIdGrpcInvoker.cs`

**Interfaces:**
- Consumes: `DeviceQueryResponse(Guid Id, string DeviceId, Guid DeviceModelId, string DeviceModelName)` (Task 2).
- Produces: generated `DeviceData.DeviceModelId` / `DeviceData.DeviceModelName` properties (fields 3 and 4).

- [ ] **Step 1: Replace the `DeviceData` message in `devices.proto`**

Replace the trailing `message DeviceData { … }` block (keep the comment above it) with:

```proto
message DeviceData {
  string id = 1;
  string device_id = 2;
  // Fields 3 and 4 re-used, not reserved: the service is live-but-unhosted and no client has seen the
  // manufacturer_id / manufacturer_name shape they replace.
  string device_model_id = 3;
  string device_model_name = 4;
}
```

- [ ] **Step 2: Update the `Success` mapping in `DeviceGrpcService.cs`**

Replace the `Success = new DeviceData { … }` initializer with:

```csharp
                Success = new DeviceData
                {
                    Id = response.Id.ToString(),
                    DeviceId = response.DeviceId,
                    DeviceModelId = response.DeviceModelId.ToString(),
                    DeviceModelName = response.DeviceModelName,
                },
```

- [ ] **Step 3: Update the success branch in `GetDeviceByIdGrpcInvoker.cs`**

Replace the `new DeviceQueryResponse(...)` expression with:

```csharp
            : new DeviceQueryResponse(
                Guid.Parse(reply.Success.Id),
                reply.Success.DeviceId,
                Guid.Parse(reply.Success.DeviceModelId),
                reply.Success.DeviceModelName);
```

- [ ] **Step 4: Build the two gRPC projects**

Run:
```bash
dotnet build src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server.csproj
dotnet build src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client.csproj
```

Expected: both `Build succeeded.` (the Contracts project regenerates from the proto as a dependency).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client
git commit -m "Carry the device model instead of the manufacturer over gRPC"
```

---

### Task 7: Blazor — model pages, manufacturer detail table, device pages

**Files (root `M/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/`):**
- Modify: `_Imports.razor`
- Create: `Manufacturers/Models/NewDeviceModelFormModel.cs`
- Create: `Manufacturers/Pages/AddDeviceModelPage.razor`, `Manufacturers/Pages/AddDeviceModelPage.razor.cs`
- Create: `Manufacturers/Pages/DeviceModelDetailPage.razor`, `Manufacturers/Pages/DeviceModelDetailPage.razor.cs`
- Modify: `Manufacturers/Pages/ManufacturerDetailPage.razor`, `Manufacturers/Pages/ManufacturerDetailPage.razor.cs`
- Modify: `Devices/Models/NewDeviceFormModel.cs`
- Modify: `Devices/Pages/CreateDevicePage.razor`, `Devices/Pages/CreateDevicePage.razor.cs`
- Modify: `Devices/Pages/DeviceListPage.razor`
- Modify: `Devices/Pages/DeviceDetailPage.razor`

**Interfaces:**
- Consumes: Task 2's records; `ISerginDispatcher`, `IUiErrorPresenter`, `SerginProblem`, `SerginProblemPanel`, `Paggination.Create(size, pageIndex)` (1-based).
- Produces: routes `/dm/manufacturers/{ManufacturerId:guid}/models/new`, `/dm/manufacturers/{ManufacturerId:guid}/models/{Id:guid}`.

- [ ] **Step 1: Append three namespaces to `_Imports.razor`**

Add after the last existing `@using`:

```razor
@using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel
@using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModel
@using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModelList
```

- [ ] **Step 2: `Manufacturers/Models/NewDeviceModelFormModel.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Models;

public sealed class NewDeviceModelFormModel
{
    [Required]
    [StringLength(DeviceModelName.MaxLength, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}
```

- [ ] **Step 3: `Manufacturers/Pages/AddDeviceModelPage.razor`**

```razor
@page "/dm/manufacturers/{ManufacturerId:guid}/models/new"

<PageTitle>New device model</PageTitle>

<MudText Typo="Typo.h4" Class="mb-4">New device model</MudText>

<EditForm Model="model" OnValidSubmit="SubmitAsync">
    <DataAnnotationsValidator />

    <MudCard>
        <MudCardContent>
            <MudTextField @bind-Value="model.Name" Label="Name" For="@(() => model.Name)" />
        </MudCardContent>
        <MudCardActions>
            <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled"
                       Color="Color.Primary" Disabled="submitting">Create</MudButton>
            <MudButton Href="@($"/dm/manufacturers/{ManufacturerId}")">Cancel</MudButton>
        </MudCardActions>
    </MudCard>
</EditForm>
```

- [ ] **Step 4: `Manufacturers/Pages/AddDeviceModelPage.razor.cs`**

```csharp
using Microsoft.AspNetCore.Components;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Models;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

public sealed partial class AddDeviceModelPage
{
    private readonly NewDeviceModelFormModel model = new();

    private bool submitting;

    [Parameter]
    public Guid ManufacturerId { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private async Task SubmitAsync()
    {
        submitting = true;

        ErrorOr<AddDeviceModelCommandResponse> result = await Dispatcher.SendAsync(
            new AddDeviceModelCommand(new ManufacturerId(ManufacturerId), new DeviceModelName(model.Name)));

        submitting = false;

        if (result.IsError)
        {
            // Every error, not the first: a duplicate name arrives as one validation error from the aggregate,
            // an unknown manufacturer as not-found from the handler.
            ErrorPresenter.Notify(result.Errors);

            return;
        }

        Navigation.NavigateTo($"/dm/manufacturers/{ManufacturerId}/models/{result.Value.Id}");
    }
}
```

- [ ] **Step 5: `Manufacturers/Pages/DeviceModelDetailPage.razor`**

```razor
@page "/dm/manufacturers/{ManufacturerId:guid}/models/{Id:guid}"

<PageTitle>Device model</PageTitle>

<MudButton StartIcon="@Icons.Material.Filled.ArrowBack" Href="@($"/dm/manufacturers/{ManufacturerId}")" Class="mb-4">Back to manufacturer</MudButton>

<SerginProblemPanel Problem="problem" />

@if (deviceModel is not null)
{
    <MudCard>
        <MudCardContent>
            <MudText Typo="Typo.h5">@deviceModel.Name</MudText>
            <MudText Typo="Typo.body2">
                Manufacturer:
                <MudLink Href="@($"/dm/manufacturers/{deviceModel.ManufacturerId}")">@deviceModel.ManufacturerName</MudLink>
            </MudText>
            <MudText Typo="Typo.body2">@deviceModel.Id</MudText>
        </MudCardContent>
    </MudCard>
}
```

- [ ] **Step 6: `Manufacturers/Pages/DeviceModelDetailPage.razor.cs`**

```csharp
using Microsoft.AspNetCore.Components;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModel;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

public sealed partial class DeviceModelDetailPage
{
    private DeviceModelQueryResponse? deviceModel;
    private SerginProblem? problem;

    [Parameter]
    public Guid ManufacturerId { get; set; }

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    protected override async Task OnParametersSetAsync()
    {
        ErrorOr<DeviceModelQueryResponse> result =
            await Dispatcher.SendAsync(new GetDeviceModelByIdQueryCommand(ManufacturerId, Id));

        if (result.IsError)
        {
            deviceModel = null;
            problem = ErrorPresenter.Present(result.FirstError);

            return;
        }

        problem = null;
        deviceModel = result.Value;
    }
}
```

- [ ] **Step 7: Rewrite `Manufacturers/Pages/ManufacturerDetailPage.razor`**

```razor
@page "/dm/manufacturers/{Id:guid}"

<PageTitle>Manufacturer</PageTitle>

<MudButton StartIcon="@Icons.Material.Filled.ArrowBack" Href="/dm/manufacturers" Class="mb-4">Back to manufacturers</MudButton>

<SerginProblemPanel Problem="problem" />

@if (manufacturer is not null)
{
    <MudCard>
        <MudCardContent>
            <MudText Typo="Typo.h5">@manufacturer.Name</MudText>
            @if (manufacturer.Address is not null)
            {
                <MudText Typo="Typo.body2">Address: @manufacturer.Address</MudText>
            }
            <MudText Typo="Typo.body2">@manufacturer.Id</MudText>
        </MudCardContent>
    </MudCard>

    <MudStack Row="true" Justify="Justify.SpaceBetween" AlignItems="AlignItems.Center" Class="mt-6 mb-2">
        <MudText Typo="Typo.h5">Models</MudText>
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   StartIcon="@Icons.Material.Filled.Add" Href="@($"/dm/manufacturers/{Id}/models/new")">New model</MudButton>
    </MudStack>

    <MudTable T="GetDeviceModelListItem" ServerData="LoadModelsAsync" Hover="true" Striped="true"
              OnRowClick="@(args => OpenModel(args.Item))" RowsPerPage="10">
        <HeaderContent>
            <MudTh>Name</MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTd DataLabel="Name">@context.Name</MudTd>
        </RowTemplate>
        <PagerContent>
            <MudTablePager />
        </PagerContent>
    </MudTable>
}
```

- [ ] **Step 8: Rewrite `Manufacturers/Pages/ManufacturerDetailPage.razor.cs`**

```csharp
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModelList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

public sealed partial class ManufacturerDetailPage
{
    private ManufacturerQueryResponse? manufacturer;
    private SerginProblem? problem;

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    protected override async Task OnParametersSetAsync()
    {
        ErrorOr<ManufacturerQueryResponse> result = await Dispatcher.SendAsync(new GetManufacturerByIdQueryCommand(Id));

        if (result.IsError)
        {
            manufacturer = null;
            problem = ErrorPresenter.Present(result.FirstError);

            return;
        }

        problem = null;
        manufacturer = result.Value;
    }

    private async Task<TableData<GetDeviceModelListItem>> LoadModelsAsync(TableState state, CancellationToken cancellationToken)
    {
        // MudBlazor's TableState.Page is 0-based; Sergin's PageIndex is 1-based.
        ErrorOr<ListQueryResponse<GetDeviceModelListItem>> result =
            await Dispatcher.SendAsync(
                new GetDeviceModelListQueryCommand(new ManufacturerId(Id), Paggination.Create(state.PageSize, state.Page + 1)),
                cancellationToken);

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return new TableData<GetDeviceModelListItem> { Items = [], TotalItems = 0 };
        }

        return new TableData<GetDeviceModelListItem> { Items = result.Value.Data, TotalItems = result.Value.Total };
    }

    private void OpenModel(GetDeviceModelListItem? item)
    {
        if (item is not null)
        {
            Navigation.NavigateTo($"/dm/manufacturers/{Id}/models/{item.Id}");
        }
    }
}
```

- [ ] **Step 9: Rewrite `Devices/Models/NewDeviceFormModel.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Models;

public sealed class NewDeviceFormModel
{
    [Required]
    // Qualified: this model's own DeviceId property shadows the domain type inside the attribute.
    [StringLength(Domain.Devices.DeviceId.MaxLength, MinimumLength = 1)]
    public string DeviceId { get; set; } = string.Empty;

    // The manufacturer is page state on CreateDevicePage, not part of what is submitted. [Required] on a Guid
    // never fails (Guid.Empty is not null) — the pipeline validator's NotEmpty is the real check, as it was for
    // ManufacturerId before.
    [Required]
    public Guid DeviceModelId { get; set; }
}
```

- [ ] **Step 10: Rewrite `Devices/Pages/CreateDevicePage.razor`**

```razor
@page "/dm/devices/new"

<PageTitle>New device</PageTitle>

<MudText Typo="Typo.h4" Class="mb-4">New device</MudText>

<EditForm Model="model" OnValidSubmit="SubmitAsync">
    <DataAnnotationsValidator />

    <MudCard>
        <MudCardContent>
            <MudTextField @bind-Value="model.DeviceId" Label="Device ID" For="@(() => model.DeviceId)" />

            <MudSelect T="Guid" Value="selectedManufacturerId" ValueChanged="OnManufacturerChangedAsync" Label="Manufacturer">
                @foreach (GetManufacturerListItem manufacturer in manufacturers)
                {
                    <MudSelectItem T="Guid" Value="@manufacturer.Id">@manufacturer.Name</MudSelectItem>
                }
            </MudSelect>

            <MudSelect T="Guid" @bind-Value="model.DeviceModelId" Label="Model"
                       For="@(() => model.DeviceModelId)" Disabled="@(selectedManufacturerId == Guid.Empty)">
                @foreach (GetDeviceModelListItem deviceModel in deviceModels)
                {
                    <MudSelectItem T="Guid" Value="@deviceModel.Id">@deviceModel.Name</MudSelectItem>
                }
            </MudSelect>
        </MudCardContent>
        <MudCardActions>
            <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled"
                       Color="Color.Primary" Disabled="submitting">Create</MudButton>
            <MudButton Href="/dm/devices">Cancel</MudButton>
        </MudCardActions>
    </MudCard>
</EditForm>
```

- [ ] **Step 11: Rewrite `Devices/Pages/CreateDevicePage.razor.cs`**

```csharp
using Microsoft.AspNetCore.Components;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModelList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Models;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Pages;

public sealed partial class CreateDevicePage
{
    private readonly NewDeviceFormModel model = new();

    private IReadOnlyCollection<GetManufacturerListItem> manufacturers = [];
    private IReadOnlyCollection<GetDeviceModelListItem> deviceModels = [];

    // Page state only: the manufacturer narrows the model picker and is not part of the command.
    private Guid selectedManufacturerId;
    private bool submitting;

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        // One page of 200 fills the picker. There is no server-side search to fall back on
        // (the list repositories ignore Term/Filtering/Sorting), so beyond 200 manufacturers
        // the tail is silently unreachable here and this needs an autocomplete instead.
        ErrorOr<ListQueryResponse<GetManufacturerListItem>> result =
            await Dispatcher.SendAsync(
                new GetManufacturerListQueryCommand(Paggination.Create(200, 1)));

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return;
        }

        manufacturers = result.Value.Data;
    }

    private async Task OnManufacturerChangedAsync(Guid manufacturerId)
    {
        selectedManufacturerId = manufacturerId;
        model.DeviceModelId = Guid.Empty;
        deviceModels = [];

        if (manufacturerId == Guid.Empty)
        {
            return;
        }

        // Same 200-row caveat as the manufacturer picker above.
        ErrorOr<ListQueryResponse<GetDeviceModelListItem>> result =
            await Dispatcher.SendAsync(
                new GetDeviceModelListQueryCommand(new ManufacturerId(manufacturerId), Paggination.Create(200, 1)));

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return;
        }

        deviceModels = result.Value.Data;
    }

    private async Task SubmitAsync()
    {
        submitting = true;

        ErrorOr<CreateDeviceCommandResponse> result = await Dispatcher.SendAsync(
            new CreateDeviceCommand(new DeviceId(model.DeviceId), new DeviceModelInternalId(model.DeviceModelId)));

        submitting = false;

        if (result.IsError)
        {
            // Every error, not the first: validation yields one per broken rule.
            ErrorPresenter.Notify(result.Errors);

            return;
        }

        Navigation.NavigateTo($"/dm/devices/{result.Value.Id}");
    }
}
```

- [ ] **Step 12: `Devices/Pages/DeviceListPage.razor` — Manufacturer column becomes Model**

Replace `<MudTh>Manufacturer</MudTh>` with `<MudTh>Model</MudTh>` and `<MudTd DataLabel="Manufacturer">@context.ManufacturerName</MudTd>` with `<MudTd DataLabel="Model">@context.DeviceModelName</MudTd>`. Nothing else changes.

- [ ] **Step 13: `Devices/Pages/DeviceDetailPage.razor` — model as text**

Replace the `Manufacturer:` `MudText` block (the three lines from `<MudText Typo="Typo.body2">` through `</MudText>` that contain the `MudLink`) with:

```razor
            @* Text, not a link: the model's page is nested under its manufacturer, and this read model
               deliberately carries no manufacturer id (spec, Decision 4). *@
            <MudText Typo="Typo.body2">Model: @device.DeviceModelName</MudText>
```

- [ ] **Step 14: Build the Blazor RCL and the composition root**

Run:
```bash
dotnet build src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.csproj
dotnet build src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement/Sergin.MeterMinder.DeviceManagement.csproj
```

Expected: both `Build succeeded.` If `ValueChanged="OnManufacturerChangedAsync"` fails to bind, write it as `ValueChanged="@(EventCallback.Factory.Create<Guid>(this, OnManufacturerChangedAsync))"` — but the bare method-group form is what MudBlazor documents and should compile.

- [ ] **Step 15: Commit**

```bash
git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor
git commit -m "Add device-model pages under the manufacturer and pick the model when creating a device"
```

---

### Task 8: Update the existing tests to the new shapes — full solution compiles

**Files (root `tests/Sergin.MeterMinder.IntegrationTests.All/`):**
- Modify: `Devices/DeviceReadModelTests.cs`
- Modify: `Devices/DeviceGrpcRoundTripTests.cs` (one line)
- Modify: `Validation/RepositoryRuleTests.cs`
- Modify: `Validation/CommandValidationTests.cs` (one test)
- Modify: `Shell/ModulePageRenderingTests.cs` (two `InlineData` rows)

**Interfaces:**
- Consumes: `AddDeviceModelCommand`/`AddDeviceModelCommandResponse`, `CreateDeviceCommand(DeviceId, DeviceModelInternalId)`, `DeviceQueryResponse`/`GetDeviceListItem` new shapes.
- Produces: nothing new; this task exists so `dotnet build Sergin.MeterMinder.slnx` passes.

These tests will not *run* green until Task 9 adds the migration (the host applies migrations at startup and EF refuses to `Migrate()` with pending model changes). This task is verified by compilation only.

- [ ] **Step 1: Rewrite `Devices/DeviceReadModelTests.cs`**

```csharp
using System.Net;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Devices;

/// <summary>
/// Both device read models carry the model's name next to its id — DeviceQueryRepository joins
/// dm.device_model in both raw-SQL queries — so the list and detail pages show a name, not a Guid. The
/// manufacturer is deliberately absent from these read models (spec, Decision 4). Written through the real
/// command handlers and read back through ISerginDispatcher from a scope, the way a Blazor page does, so the
/// reads genuinely round-trip through Postgres. The last test renders the detail page itself:
/// OnParametersSetAsync runs during server-side prerendering, so the name is in the HTML a plain GET returns.
/// The list page is not rendered the same way on purpose — MudTable's ServerData loads after first render,
/// which prerendering never reaches, so its rows are absent from that HTML and the read-model assertion is
/// the honest check for it.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceReadModelTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task GetDeviceList_CarriesDeviceModelName()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelName modelName = NewDeviceModelName();
        DeviceModelInternalId modelId = await AddDeviceModelAsync(dispatcher, manufacturerId, modelName);
        DeviceId deviceId = NewDeviceId();
        await CreateDeviceAsync(dispatcher, deviceId, modelId);

        ErrorOr<ListQueryResponse<GetDeviceListItem>> list =
            await dispatcher.SendAsync(new GetDeviceListQueryCommand(Paggination.Create(1000, 1)));

        Assert.False(list.IsError, list.IsError ? list.FirstError.Description : string.Empty);
        GetDeviceListItem item = Assert.Single(list.Value.Data, i => i.DeviceId == deviceId.Value);
        Assert.Equal(modelId.Value, item.DeviceModelId);
        Assert.Equal(modelName.Value, item.DeviceModelName);
    }

    [Fact]
    public async Task GetDeviceById_CarriesDeviceModelName()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelName modelName = NewDeviceModelName();
        DeviceModelInternalId modelId = await AddDeviceModelAsync(dispatcher, manufacturerId, modelName);
        Guid id = await CreateDeviceAsync(dispatcher, NewDeviceId(), modelId);

        ErrorOr<DeviceQueryResponse> device = await dispatcher.SendAsync(new GetDeviceByIdQueryCommand(id));

        Assert.False(device.IsError, device.IsError ? device.FirstError.Description : string.Empty);
        Assert.Equal(modelId.Value, device.Value.DeviceModelId);
        Assert.Equal(modelName.Value, device.Value.DeviceModelName);
    }

    [Fact]
    public async Task DeviceDetailPage_RendersDeviceModelName_NotItsId()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelName modelName = NewDeviceModelName();
        DeviceModelInternalId modelId = await AddDeviceModelAsync(dispatcher, manufacturerId, modelName);
        Guid id = await CreateDeviceAsync(dispatcher, NewDeviceId(), modelId);

        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri($"/dm/devices/{id}", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Model: {modelName.Value}", html, StringComparison.Ordinal);
        // Neither the model's nor the manufacturer's id is visible text on this page.
        Assert.DoesNotContain(modelId.Value.ToString(), html, StringComparison.Ordinal);
        Assert.DoesNotContain(manufacturerId.Value.ToString(), html, StringComparison.Ordinal);
    }

    private static DeviceModelName NewDeviceModelName() => new($"model-{Guid.CreateVersion7()}");

    private static DeviceId NewDeviceId() => new($"device-{Guid.CreateVersion7()}");

    private static async Task<ManufacturerId> CreateManufacturerAsync(ISerginDispatcher dispatcher)
    {
        ErrorOr<CreateManufacturerCommandResponse> created = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName($"manufacturer-{Guid.CreateVersion7()}"), Address: null));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return new ManufacturerId(created.Value.Id);
    }

    private static async Task<DeviceModelInternalId> AddDeviceModelAsync(
        ISerginDispatcher dispatcher, ManufacturerId manufacturerId, DeviceModelName name)
    {
        ErrorOr<AddDeviceModelCommandResponse> added = await dispatcher.SendAsync(
            new AddDeviceModelCommand(manufacturerId, name));

        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);

        return new DeviceModelInternalId(added.Value.Id);
    }

    private static async Task<Guid> CreateDeviceAsync(ISerginDispatcher dispatcher, DeviceId deviceId, DeviceModelInternalId modelId)
    {
        ErrorOr<CreateDeviceCommandResponse> created = await dispatcher.SendAsync(
            new CreateDeviceCommand(deviceId, modelId));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return created.Value.Id;
    }
}
```

- [ ] **Step 2: `Devices/DeviceGrpcRoundTripTests.cs` — the fake response's literal**

The record is positional `(Guid, string, Guid, string)` in both shapes, so line ~99 still compiles; make its meaning honest by changing

```csharp
        DeviceQueryResponse expected = new(deviceGuid, "DEV-42", Guid.CreateVersion7(), "Acme Meters");
```

to

```csharp
        DeviceQueryResponse expected = new(deviceGuid, "DEV-42", Guid.CreateVersion7(), "XYZ-200");
```

- [ ] **Step 3: `Validation/RepositoryRuleTests.cs` — the device tests reference a model**

Apply these edits:

1. Add `using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;` to the usings.
2. Replace the two constants with:

```csharp
    private const string DeviceIdTakenMessage = "'Device Id' is already in use.";
    private const string DeviceModelMissingMessage = "'Device Model Id' must refer to an existing DeviceModel.";
```

3. Rename `CreateDevice_UnknownManufacturer_IsRefusedNotThrown` to `CreateDevice_UnknownDeviceModel_IsRefusedNotThrown` and replace its body with:

```csharp
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        DeviceId deviceId = new($"device-{Guid.CreateVersion7()}");

        // Well-formed, so the shape rule passes and the existence rule is what runs. Without the rule this
        // send would throw DbUpdateException out of the handler's SaveChangesAsync (FK 23503).
        ErrorOr<CreateDeviceCommandResponse> created = await dispatcher.SendAsync(
            new CreateDeviceCommand(deviceId, new DeviceModelInternalId(Guid.CreateVersion7())));

        Assert.True(created.IsError, "A device-model id that matches no row must be refused.");
        Error error = Assert.Single(created.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(CreateDeviceCommand.DeviceModelId), error.Code);
        Assert.Equal(DeviceModelMissingMessage, error.Description);

        ErrorOr<ListQueryResponse<GetDeviceListItem>> list =
            await dispatcher.SendAsync(new GetDeviceListQueryCommand(Paggination.Create(1000, 1)));

        Assert.False(list.IsError, list.IsError ? list.FirstError.Description : string.Empty);
        Assert.DoesNotContain(list.Value.Data, item => item.DeviceId == deviceId.Value);
```

4. In `CreateDevice_DuplicateDeviceId_IsRefused`, replace `ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);` with `DeviceModelInternalId modelId = await CreateDeviceModelAsync(dispatcher);` and both `new CreateDeviceCommand(deviceId, manufacturerId)` with `new CreateDeviceCommand(deviceId, modelId)`.

5. Rename `CreateDevice_EmptyDeviceIdAndUnknownManufacturer_ReportsShapeAndExistenceNotUniqueness` to `CreateDevice_EmptyDeviceIdAndUnknownDeviceModel_ReportsShapeAndExistenceNotUniqueness`; in its doc comment change "the manufacturer rule" to "the device-model rule"; replace the command with `new CreateDeviceCommand(new DeviceId(string.Empty), new DeviceModelInternalId(Guid.CreateVersion7()))`, the assertion message with `"An empty device id and an unknown device model must both be refused."`, and the last `Assert.Contains` with:

```csharp
        Assert.Contains(created.Errors, error =>
            error.Code == nameof(CreateDeviceCommand.DeviceModelId) && error.Description == DeviceModelMissingMessage);
```

6. In `UniqueIndex_IsTheGuaranteeWhenTheValidatorIsBypassed`, replace `ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);` with `DeviceModelInternalId modelId = await CreateDeviceModelAsync(dispatcher);`, `new CreateDeviceCommand(deviceId, manufacturerId)` with `new CreateDeviceCommand(deviceId, modelId)`, and `devices.Insert(Device.Create(deviceId, manufacturerId));` with `devices.Insert(Device.Create(deviceId, modelId));`.

7. Add this helper after `CreateManufacturerAsync` (which stays — `ExistsAsync_AnswersWithoutTracking` still uses it):

```csharp
    private static async Task<DeviceModelInternalId> CreateDeviceModelAsync(ISerginDispatcher dispatcher)
    {
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);

        ErrorOr<AddDeviceModelCommandResponse> added = await dispatcher.SendAsync(
            new AddDeviceModelCommand(manufacturerId, new DeviceModelName($"model-{Guid.CreateVersion7()}")));

        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);

        return new DeviceModelInternalId(added.Value.Id);
    }
```

8. In the class doc comment, change "a reference to a missing aggregate" to "a reference to a missing aggregate or entity".

- [ ] **Step 4: `Validation/CommandValidationTests.cs` — the empty-command test**

Rename `CreateDevice_EmptyDeviceIdAndManufacturer_ReportsBothAtOnce` to `CreateDevice_EmptyDeviceIdAndDeviceModel_ReportsBothAtOnce`; replace `new ManufacturerId(Guid.Empty)` with `new DeviceModelInternalId(Guid.Empty)`, the message with `"An empty device id and an empty device model must both be refused."`, and `nameof(CreateDeviceCommand.ManufacturerId)` with `nameof(CreateDeviceCommand.DeviceModelId)`. The file's `using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;` stays (it now supplies `DeviceModelInternalId`).

- [ ] **Step 5: `Shell/ModulePageRenderingTests.cs` — the two new routes render**

Add two rows to the `Page_RendersServerSide_WithNavFromBothModules` theory, after `[InlineData("/dm/manufacturers/new")]`:

```csharp
    [InlineData("/dm/manufacturers/01920000-0000-7000-8000-000000000001/models/new")]
    [InlineData("/dm/manufacturers/01920000-0000-7000-8000-000000000001/models/01920000-0000-7000-8000-000000000002")]
```

(The detail page renders its problem panel for ids that match nothing — still a 200 with the nav.)

- [ ] **Step 6: Build the whole solution**

Run: `dotnet build Sergin.MeterMinder.slnx`

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. This is the first full-solution build since Task 1; fix any analyzer finding in the files this plan touched before moving on.

- [ ] **Step 7: Commit**

```bash
git add tests/Sergin.MeterMinder.IntegrationTests.All
git commit -m "Point the device tests at a device model"
```

---

### Task 9: Migration `AddDeviceModels` — then the existing suite runs green

**Files:**
- Create (scaffolded): `M/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Migrations/<timestamp>_AddDeviceModels.cs`, `<timestamp>_AddDeviceModels.Designer.cs`
- Modify (scaffolded): `M/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Migrations/DeviceManagementDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes: the model from Task 3.
- Produces: `dm.device_model`; `dm.device.device_model_id`; `dm.device.manufacturer_id` gone.

- [ ] **Step 1: Scaffold**

Run:
```bash
dotnet ef migrations add AddDeviceModels \
  --project src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data \
  --startup-project src/Hosts/Sergin.MeterMinder.Hosts.All
```

Expected: `Done. To undo this action, use 'ef migrations remove'`. If `dotnet ef` is missing: `dotnet tool install --global dotnet-ef` and retry. No connection string is needed for `migrations add`.

- [ ] **Step 2: Convert `<timestamp>_AddDeviceModels.cs` to a file-scoped namespace**

Change `namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Migrations\n{` to `namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Migrations;`, delete the matching closing brace at the end of the file, and dedent the class body by one level. `20260915140708_AddDeviceIdUniqueIndex.cs` is the reference shape. Leave the `.Designer.cs` and the snapshot exactly as generated.

- [ ] **Step 3: Insert the delete as the first statement of `Up`, and check the operation order**

The first lines of `Up` become:

```csharp
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing devices carry a manufacturer but no model, and device_model_id is NOT NULL. Migrations
        // auto-apply only in Development and there is no production data, so the rows are dropped rather than
        // backfilled with placeholder models (spec, Decision 7).
        migrationBuilder.Sql("DELETE FROM dm.device;");

        migrationBuilder.DropForeignKey(
            name: "fk_device_manufacturer_manufacturer_id",
            schema: "dm",
            table: "device");
```

Then read the rest of `Up` and confirm it contains, in EF's order: `DropIndex("ix_device_manufacturer_id")`, `DropColumn("manufacturer_id")`, `AddColumn<Guid>("device_model_id", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000000"))` — leave that default, the table is empty by then — `CreateTable("device_model")` with columns `id`, `manufacturer_id`, `name` (`text`, `nullable: false`), primary key `pk_device_model`, and a foreign key `fk_device_model_manufacturer_manufacturer_id` with `onDelete: ReferentialAction.Cascade`; `CreateIndex("ix_device_device_model_id")`; `CreateIndex("ix_device_model_manufacturer_id_name", columns: ["manufacturer_id", "name"], unique: true)`; `AddForeignKey("fk_device_device_model_device_model_id", … onDelete: ReferentialAction.Restrict)`. If the two `onDelete` values differ from Cascade/Restrict, Task 3's `OnDelete` calls were missed — fix the configuration and re-scaffold (`dotnet ef migrations remove` first), do not hand-edit the migration.

- [ ] **Step 4: Build**

Run: `dotnet build Sergin.MeterMinder.slnx`

Expected: `Build succeeded.`

- [ ] **Step 5: Start Docker Desktop if it is not running**

```bash
docker info >/dev/null 2>&1 || ( "/c/Program Files/Docker/Docker/Docker Desktop.exe" & )
until docker info >/dev/null 2>&1; do sleep 5; done
```

- [ ] **Step 6: Run the whole integration suite**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`

Expected: every test passes, including the three updated `DeviceReadModelTests`, the four updated device tests in `RepositoryRuleTests`, `CommandValidationTests`, `DeviceGrpcRoundTripTests`, and the two new `ModulePageRenderingTests` rows. A failure that names `pending changes` means the snapshot was not regenerated — re-run Step 1.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Migrations
git commit -m "Add the device_model table and swap the device's manufacturer for a model"
```

---

### Task 10: `DeviceModelTests` — the aggregate's rules, the reads, and the index under a race

**Files:**
- Create: `tests/Sergin.MeterMinder.IntegrationTests.All/Manufacturers/DeviceModelTests.cs`

**Interfaces:**
- Consumes: `AddDeviceModelCommand`, `GetDeviceModelByIdQueryCommand`, `GetDeviceModelListQueryCommand`, `IManufacturerRepository.GetWithModelsAsync`, `Manufacturer.AddModel`, `IDeviceManagementUnitOfWork`.

- [ ] **Step 1: Write the test class**

```csharp
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModel;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModelList;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Manufacturers;

/// <summary>
/// A device model is an entity inside the Manufacturer aggregate: it comes into being only through
/// Manufacturer.AddModel, which refuses a name that manufacturer already uses, and the composite unique index
/// on dm.device_model (manufacturer_id, name) is the guarantee under a race. The reads are keyed by the
/// manufacturer — GetOne by both ids, the list by the manufacturer's — so a model is never reachable under
/// another manufacturer. Dispatched through ISerginDispatcher from a scope, exactly as a Blazor page does.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceModelTests(SerginWebApiFactory<Program> factory)
{
    private const string NameTakenMessage = "'Name' is already in use.";

    [Fact]
    public async Task AddModel_UnderAManufacturer_ThenGetOneAndListFindIt()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerName manufacturerName = NewManufacturerName();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher, manufacturerName);
        DeviceModelName name = NewModelName();

        ErrorOr<AddDeviceModelCommandResponse> added = await dispatcher.SendAsync(
            new AddDeviceModelCommand(manufacturerId, name));

        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);

        ErrorOr<DeviceModelQueryResponse> one = await dispatcher.SendAsync(
            new GetDeviceModelByIdQueryCommand(manufacturerId.Value, added.Value.Id));

        Assert.False(one.IsError, one.IsError ? one.FirstError.Description : string.Empty);
        Assert.Equal(added.Value.Id, one.Value.Id);
        Assert.Equal(manufacturerId.Value, one.Value.ManufacturerId);
        Assert.Equal(manufacturerName.Value, one.Value.ManufacturerName);
        Assert.Equal(name.Value, one.Value.Name);

        ErrorOr<ListQueryResponse<GetDeviceModelListItem>> list = await dispatcher.SendAsync(
            new GetDeviceModelListQueryCommand(manufacturerId, Paggination.Create(1000, 1)));

        Assert.False(list.IsError, list.IsError ? list.FirstError.Description : string.Empty);
        GetDeviceModelListItem item = Assert.Single(list.Value.Data);
        Assert.Equal(added.Value.Id, item.Id);
        Assert.Equal(name.Value, item.Name);
    }

    /// <summary>
    /// Not-found is the handler's, not the validator's: adding a model is a mutation of an existing aggregate,
    /// so the handler loads it and answers NotFound when it is missing (spec, Decision 6).
    /// </summary>
    [Fact]
    public async Task AddModel_ToAnUnknownManufacturer_IsNotFound()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<AddDeviceModelCommandResponse> added = await dispatcher.SendAsync(
            new AddDeviceModelCommand(new ManufacturerId(Guid.CreateVersion7()), NewModelName()));

        Assert.True(added.IsError, "A manufacturer id that matches no row must be refused.");
        Assert.Equal(ErrorType.NotFound, added.FirstError.Type);
    }

    [Fact]
    public async Task AddModel_WithANameAlreadyUsedByThatManufacturer_IsRefused()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher, NewManufacturerName());
        DeviceModelName name = NewModelName();

        ErrorOr<AddDeviceModelCommandResponse> first = await dispatcher.SendAsync(
            new AddDeviceModelCommand(manufacturerId, name));

        Assert.False(first.IsError, first.IsError ? first.FirstError.Description : string.Empty);

        ErrorOr<AddDeviceModelCommandResponse> second = await dispatcher.SendAsync(
            new AddDeviceModelCommand(manufacturerId, name));

        Assert.True(second.IsError, "A name this manufacturer already uses must be refused.");
        Error error = Assert.Single(second.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(DeviceModel.Name), error.Code);
        Assert.Equal(NameTakenMessage, error.Description);

        ErrorOr<ListQueryResponse<GetDeviceModelListItem>> list = await dispatcher.SendAsync(
            new GetDeviceModelListQueryCommand(manufacturerId, Paggination.Create(1000, 1)));

        Assert.False(list.IsError, list.IsError ? list.FirstError.Description : string.Empty);
        Assert.Single(list.Value.Data);
    }

    /// <summary>The rule is per manufacturer, not global — two manufacturers may both ship a "Model 100".</summary>
    [Fact]
    public async Task AddModel_WithANameUsedByAnotherManufacturer_IsAllowed()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerId first = await CreateManufacturerAsync(dispatcher, NewManufacturerName());
        ManufacturerId second = await CreateManufacturerAsync(dispatcher, NewManufacturerName());
        DeviceModelName name = NewModelName();

        ErrorOr<AddDeviceModelCommandResponse> underFirst = await dispatcher.SendAsync(new AddDeviceModelCommand(first, name));
        ErrorOr<AddDeviceModelCommandResponse> underSecond = await dispatcher.SendAsync(new AddDeviceModelCommand(second, name));

        Assert.False(underFirst.IsError, underFirst.IsError ? underFirst.FirstError.Description : string.Empty);
        Assert.False(underSecond.IsError, underSecond.IsError ? underSecond.FirstError.Description : string.Empty);
        Assert.NotEqual(underFirst.Value.Id, underSecond.Value.Id);
    }

    /// <summary>
    /// The aggregate's check is not the guarantee. Two scopes each load the same manufacturer with its models,
    /// each add the same name — both see no duplicate — and each save: the second SaveChangesAsync hits the
    /// unique index. This is the race between two AddDeviceModel requests, made deterministic. It still
    /// surfaces as DbUpdateException over a 23505, not as ErrorOr: translating Postgres SqlStates is a
    /// separate, cross-cutting slice.
    /// </summary>
    [Fact]
    public async Task UniqueIndex_IsTheGuaranteeUnderTheRace()
    {
        using IServiceScope setup = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = setup.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher, NewManufacturerName());
        DeviceModelName name = NewModelName();

        using IServiceScope first = factory.Services.CreateScope();
        using IServiceScope second = factory.Services.CreateScope();

        Manufacturer? loadedFirst = await first.ServiceProvider
            .GetRequiredService<IManufacturerRepository>().GetWithModelsAsync(manufacturerId);
        Manufacturer? loadedSecond = await second.ServiceProvider
            .GetRequiredService<IManufacturerRepository>().GetWithModelsAsync(manufacturerId);

        Assert.NotNull(loadedFirst);
        Assert.NotNull(loadedSecond);

        Assert.False(loadedFirst.AddModel(name).IsError);
        Assert.False(loadedSecond.AddModel(name).IsError);

        await first.ServiceProvider.GetRequiredService<IDeviceManagementUnitOfWork>().SaveChangesAsync();

        DbUpdateException exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => second.ServiceProvider.GetRequiredService<IDeviceManagementUnitOfWork>().SaveChangesAsync());

        PostgresException postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
    }

    [Fact]
    public async Task GetList_ReturnsOnlyThatManufacturersModels()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerId mine = await CreateManufacturerAsync(dispatcher, NewManufacturerName());
        ManufacturerId theirs = await CreateManufacturerAsync(dispatcher, NewManufacturerName());

        Guid mineA = await AddModelAsync(dispatcher, mine, NewModelName());
        Guid mineB = await AddModelAsync(dispatcher, mine, NewModelName());
        Guid theirsOnly = await AddModelAsync(dispatcher, theirs, NewModelName());

        ErrorOr<ListQueryResponse<GetDeviceModelListItem>> list = await dispatcher.SendAsync(
            new GetDeviceModelListQueryCommand(mine, Paggination.Create(1000, 1)));

        Assert.False(list.IsError, list.IsError ? list.FirstError.Description : string.Empty);
        Assert.Equal(2, list.Value.Total);
        Assert.Contains(list.Value.Data, item => item.Id == mineA);
        Assert.Contains(list.Value.Data, item => item.Id == mineB);
        Assert.DoesNotContain(list.Value.Data, item => item.Id == theirsOnly);
    }

    [Fact]
    public async Task GetOne_UnderTheWrongManufacturer_IsNotFound()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerId owner = await CreateManufacturerAsync(dispatcher, NewManufacturerName());
        ManufacturerId other = await CreateManufacturerAsync(dispatcher, NewManufacturerName());
        Guid modelId = await AddModelAsync(dispatcher, owner, NewModelName());

        ErrorOr<DeviceModelQueryResponse> underOwner = await dispatcher.SendAsync(
            new GetDeviceModelByIdQueryCommand(owner.Value, modelId));
        ErrorOr<DeviceModelQueryResponse> underOther = await dispatcher.SendAsync(
            new GetDeviceModelByIdQueryCommand(other.Value, modelId));

        Assert.False(underOwner.IsError, underOwner.IsError ? underOwner.FirstError.Description : string.Empty);
        Assert.True(underOther.IsError, "A real model addressed under another manufacturer must not be served.");
        Assert.Equal(ErrorType.NotFound, underOther.FirstError.Type);
    }

    private static ManufacturerName NewManufacturerName() => new($"manufacturer-{Guid.CreateVersion7()}");

    private static DeviceModelName NewModelName() => new($"model-{Guid.CreateVersion7()}");

    private static async Task<ManufacturerId> CreateManufacturerAsync(ISerginDispatcher dispatcher, ManufacturerName name)
    {
        ErrorOr<CreateManufacturerCommandResponse> created = await dispatcher.SendAsync(
            new CreateManufacturerCommand(name, Address: null));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return new ManufacturerId(created.Value.Id);
    }

    private static async Task<Guid> AddModelAsync(ISerginDispatcher dispatcher, ManufacturerId manufacturerId, DeviceModelName name)
    {
        ErrorOr<AddDeviceModelCommandResponse> added = await dispatcher.SendAsync(
            new AddDeviceModelCommand(manufacturerId, name));

        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);

        return added.Value.Id;
    }
}
```

- [ ] **Step 2: Run just this class**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~DeviceModelTests"`

Expected: 7 passed. If `UniqueIndex_IsTheGuaranteeUnderTheRace` fails with the second save succeeding, the unique index is missing — check the migration from Task 9. If `AddModel_WithANameAlreadyUsedByThatManufacturer_IsRefused` gets a `DbUpdateException` instead of a validation error, the handler is calling `GetAsync` instead of `GetWithModelsAsync`.

- [ ] **Step 3: Run the whole suite once more**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`

Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add tests/Sergin.MeterMinder.IntegrationTests.All/Manufacturers/DeviceModelTests.cs
git commit -m "Prove the device-model invariant, its reads, and the index under a race"
```

---

### Task 11: Documentation and the knowledge graph

**Files:**
- Modify: `src/Modules/DeviceManagement/CLAUDE.md`
- Modify: `.claude/CLAUDE.md`
- Regenerate (gitignored, local only): `graphify-out/`

- [ ] **Step 1: Module `CLAUDE.md` — header and the `Devices` aggregate section**

In the intro paragraph change "this file only covers what's specific to the `Devices`/`DeviceModels` aggregates" to "this file only covers what's specific to the `Devices`/`Manufacturers` aggregates".

Replace the first paragraph of `## \`Devices\` aggregate` (the one ending "set via `Device.Create(DeviceId, ManufacturerId)`") with:

```markdown
`Sergin.MeterMinder.DeviceManagement.Domain/Devices/Device.cs` — `AggregateRoot<DeviceIntenralId>` (note the misspelling — it's the real type name, match it). `DeviceId` is the business-facing string key; `DeviceIntenralId` is the internal `Guid` PK. `Device` carries a mandatory `DeviceModelId` — a `DeviceModelInternalId`, a reference across the aggregate boundary to a `DeviceModel` entity inside the `Manufacturers` aggregate (below) — set via `Device.Create(DeviceId, DeviceModelInternalId)`. **A device does not store its manufacturer**; it is reachable only through the model (`dm.device.device_model_id → dm.device_model.id → dm.device_model.manufacturer_id`). The FK is `ON DELETE RESTRICT` — a reference across an aggregate boundary must never cascade.
```

Delete the whole paragraph beginning "**`DeviceModel` is an unfinished, dangling piece**".

Replace the paragraph beginning "**Both device read models carry the manufacturer's name next to its id.**" with:

```markdown
**Both device read models carry the model's name next to its id, and nothing about the manufacturer.** `GetDeviceListItem` and `DeviceQueryResponse` are `(Id, DeviceId, DeviceModelId, DeviceModelName)`; `DeviceQueryRepository` fills the name with an inner `JOIN dm.device_model dm_ ON dm_.id = d.device_model_id` in both raw-SQL reads (the alias is `dm_` because `dm` is the schema; safe because `device_model_id` is a required FK — the count statement stays on `dm.device` alone). `DeviceListPage` shows the name in its Model column and `DeviceDetailPage` renders `Model: <name>` as plain text — not a link, because the model's page is nested under its manufacturer and this read model deliberately carries no manufacturer id (`docs/superpowers/specs/2026-09-16-device-models-design.md`, Decision 4; adding `ManufacturerId` back to `DeviceQueryResponse` is the one-line reversal). The gRPC wire shape follows the record: `DeviceData.device_model_id = 3`, `device_model_name = 4` in `devices.proto`, mapped by `DeviceGrpcService` and read back by `GetDeviceByIdGrpcInvoker`. Adding a field to `DeviceQueryResponse` therefore means touching those three plus `DeviceGrpcRoundTripTests`, whose record-equality assertion is what proves the field survives the round trip. `tests/.../Devices/DeviceReadModelTests.cs` covers the two reads and the detail page's prerendered HTML; the list page is deliberately not asserted through HTML, because `MudTable`'s `ServerData` loads after first render, which prerendering never reaches.
```

In the `Devices` slices table, change the `GetList` row's Permission cell from `none` to `` `permission.dm.devices.read` `` (the attribute has been on `GetDeviceListQueryCommand` all along; the table was stale).

- [ ] **Step 2: Module `CLAUDE.md` — `Validators` section**

Replace the `CreateDeviceCommandValidator` bullet with:

```markdown
- `Devices/Commands/Create/CreateDeviceCommandValidator.cs` — this module's one validator with **constructor dependencies** (UserAccess's `CreateUserCommandValidator` is the other): `CreateDeviceCommandValidator(IDeviceRepository devices, IManufacturerRepository manufacturers)`. Four rules, in two pairs. The shape rules target `.Value` with `OverridePropertyName`: `DeviceId.Value` not empty and at most `DeviceId.MaxLength` = 100; `DeviceModelId.Value` not `Guid.Empty`. The database rules target the wrapper, so their `Error.Code` is already the property name: `RuleFor(x => x.DeviceId).MustBeUniqueIn(devices).When(x => !string.IsNullOrWhiteSpace(x.DeviceId.Value))` — message `'Device Id' is already in use.` — and `RuleFor(x => x.DeviceModelId).MustAsync(manufacturers.ModelExistsAsync).WithMessage("'{PropertyName}' must refer to an existing DeviceModel.").When(x => x.DeviceModelId.Value != Guid.Empty)` — message `'Device Model Id' must refer to an existing DeviceModel.`. **The model rule is a local `MustAsync`, not `MustExistIn`**: that extension is generic over `IRepository<TAggregateRoot, TId>`, and a model is an entity inside the `Manufacturer` aggregate with no repository of its own, so the yes/no comes from `IManufacturerRepository.ModelExistsAsync` instead. The `When` guards keep the queries off for a value the shape rule already refused. Both are advisory — the FK and `ix_device_device_id` (below) are the guarantee under a race, which still surfaces as `DbUpdateException` (root CLAUDE.md, "CQRS structural gotchas").
```

Add a new bullet after the `CreateManufacturerCommandValidator` bullet:

```markdown
- `Manufacturers/Commands/AddDeviceModel/AddDeviceModelCommandValidator.cs` — shape only, no dependencies: `ManufacturerId.Value` not `Guid.Empty`; `Name.Value` not empty, at most `DeviceModelName.MaxLength` = 200. **Two rules are deliberately absent.** Manufacturer existence: adding a model is a mutation of an existing aggregate, so `AddDeviceModelCommandHandler` loads it and returns `Error.NotFound()` when it is missing (the `DeactivateUser` shape) — a `MustExistIn` here would query for an answer the handler is about to get anyway. Name uniqueness: `Manufacturer.AddModel` holds that invariant and returns `Error.Validation("Name", "'Name' is already in use.")` itself — the same code and text `MustBeUniqueIn` would produce, so it renders through the same path. `tests/.../Manufacturers/DeviceModelTests.cs` pins both.
```

Change the sentence "Both write commands carry a FluentValidation validator" at the top of the section to "All three write commands carry a FluentValidation validator".

- [ ] **Step 3: Module `CLAUDE.md` — `Manufacturers` aggregate section**

Replace the first paragraph of `## \`Manufacturers\` aggregate` (ending "matching the rest of this module's style).") with:

```markdown
`Sergin.MeterMinder.DeviceManagement.Domain/Manufacturers/Manufacturer.cs` — `AggregateRoot<ManufacturerId>`, private ctor + `static Create(ManufacturerName, ManufacturerAddress?)` factory. `Name` is mandatory, `Address` is optional (nullable value object, nullable `ManufacturerAddressConverter`). The aggregate owns a collection of **`DeviceModel` entities** — see the next subsection. Nothing else references a manufacturer: `Device` used to carry a `ManufacturerId` FK and now carries a `DeviceModelId` instead.
```

Insert a new subsection immediately after that paragraph, before the "Implemented feature slices" line:

```markdown
### `DeviceModel` — an entity inside this aggregate

`Domain/Manufacturers/DeviceModel.cs` — `Entity<DeviceModelInternalId>` (not an aggregate root), with `ManufacturerId` (the owner's id; EF's FK) and `DeviceModelName Name` (`MaxLength = 200`). **It comes into being only through `Manufacturer.AddModel(DeviceModelName)`**, which returns `ErrorOr<DeviceModel>` and refuses a name this manufacturer already uses with `Error.Validation(nameof(DeviceModel.Name), "'Name' is already in use.")` — the invariant the aggregate exists to hold. `DeviceModel.Create` is `internal static` so nothing outside the Domain assembly can construct one. `Manufacturer.Models` is an `IReadOnlyCollection<DeviceModel>` over a `private readonly List<DeviceModel> models` backing field (the `Role.Permissions` idiom). Comparison is `DeviceModelName` record equality — case-sensitive, the same rule the Postgres index applies.

**Mapped as a regular EF entity type, not `OwnsMany`.** `Role.Permissions` and `User.Roles` use `OwnsMany`, and it would be the natural first reach. It cannot work here: `Device` holds a foreign key to a model, and EF Core refuses an owned type on the principal side of a non-ownership relationship (`CoreStrings.PrincipalOwnedType`). So `DeviceModelEntityTypeConfiguration` holds the entity's own shape (key, converters, the composite unique index `ix_device_model_manufacturer_id_name` on `(manufacturer_id, name)`) and `ManufacturerEntityTypeConfiguration` configures the relationship from the owner's side — `HasMany(m => m.Models).WithOne().HasForeignKey(model => model.ManufacturerId).IsRequired().OnDelete(DeleteBehavior.Cascade)` plus `Navigation(m => m.Models).HasField("models").UsePropertyAccessMode(PropertyAccessMode.Field)`. **The aggregate boundary is held by the repository layer**: there is no `IDeviceModelRepository`, no `DbSet<DeviceModel>` is exposed, and the only writes go through `Manufacturer.Models`. Cascade inside the aggregate, `Restrict` on `Device → DeviceModel` across it.

**`AddModel` needs the manufacturer loaded with its models.** `IManufacturerRepository.GetAsync` is `FindAsync`, which loads no navigation; on such an instance `models` is empty and the duplicate check cannot see existing names (the index would still catch it, but as a `DbUpdateException`). `IManufacturerRepository` therefore carries `GetWithModelsAsync(ManufacturerId)` — `Set.Include(m => m.Models).SingleOrDefaultAsync(...)` — and `AddDeviceModelCommandHandler` calls that, never `GetAsync`. Trade-off accepted: adding a model loads every model of that manufacturer, fine for tens per manufacturer; revisit if it is ever thousands. The repository's other addition is `ModelExistsAsync(DeviceModelInternalId)`, the one place a model is looked up from outside its aggregate — `Set.SelectMany(m => m.Models).AnyAsync(...)` (through the aggregate's own `Set`, because capturing the primary-constructor `dbContext` that is also passed to the base is CS9107, an error here), for `CreateDeviceCommandValidator`.

Slices on this aggregate for models (`Manufacturers/Commands/<Feature>/` in Application, records in `.Application.Contracts`; the query interfaces join `IManufacturerAllQueryRepository`, implemented by `ManufacturerQueryRepository`, registered in `ManufacturerInstallationExtensions` — no `DeviceModels` folder or installation class anywhere):

| Feature | Kind | Route | Permission |
|---|---|---|---|
| `AddDeviceModel` | command | `POST /dm/manufacturers/{manufacturerId:guid}/models` | none |
| `GetDeviceModel` | query | `GET /dm/manufacturers/{manufacturerId:guid}/models/{modelId:guid}` | `permission.dm.manufacturers.read` |
| `GetDeviceModelList` | query | `GET /dm/manufacturers/{manufacturerId:guid}/models` (`[AsParameters] ListQueryRequestModel`) | `permission.dm.manufacturers.read` |

`GetDeviceModelByIdQueryCommand(Guid ManufacturerId, Guid Id)` is **keyed by both ids** (`WHERE id = @Id AND manufacturer_id = @ManufacturerId`), so a model addressed under the wrong manufacturer is `NotFound`, not served — the nested route carries no ignored segment. `GetDeviceModelListQueryCommand` takes the `ManufacturerId` as a **first-class constructor argument and property**, not through `ListQuery.Filtering` (which no repository reads and which would make a mandatory scope optional); its repository method is `GetListAsync(ManufacturerId, ListQuery, ct)`, a deliberate departure from the base-`ListQuery`-only convention. `DeviceModelQueryResponse` is `(Id, ManufacturerId, ManufacturerName, Name)` — the manufacturer's name is joined for the detail page's back link.

Blazor (`Presentation.Blazor/Manufacturers/`): `ManufacturerDetailPage` grew a "Models" section under its card — a `MudTable` on `ServerData` dispatching `GetDeviceModelListQueryCommand(new ManufacturerId(Id), …)` and a "New model" button; `AddDeviceModelPage` (`/dm/manufacturers/{ManufacturerId:guid}/models/new`, over `Models/NewDeviceModelFormModel`) and `DeviceModelDetailPage` (`/dm/manufacturers/{ManufacturerId:guid}/models/{Id:guid}`) are new. There is **no nav entry** for models; the manufacturer's page is the entry point. `CreateDevicePage` picks the model through **cascading selects** — the manufacturer select is page state (`selectedManufacturerId`, not on the form model) whose `ValueChanged` loads that manufacturer's models; the form submits `DeviceModelId` only. Permission reuse is deliberate: models are part of the manufacturer catalog, and a dedicated `permission.dm.device-models.*` would need an `appsettings.json` grant plus a UserAccess seed-migration PR for no access-control gain. Design and every decision: `docs/superpowers/specs/2026-09-16-device-models-design.md`.
```

In the `Manufacturers` slices table, change the `GetList` row's Permission cell from `none` to `` `permission.dm.manufacturers.read` ``.

- [ ] **Step 4: Module `CLAUDE.md` — `Repositories` section**

In the first bullet, replace "and `ManufacturerRepository`, which is a one-line declaration with no body at all." with "and `ManufacturerRepository`, which adds the two model lookups `GetWithModelsAsync` and `ModelExistsAsync` (see the `DeviceModel` subsection above)."

Replace the last bullet's opening "`IManufacturerRepository` (`Domain/Manufacturers/`) — plain `IRepository<Manufacturer, ManufacturerId>`, no custom methods and no alternate key; its `ExistsAsync` (from the base) is what `CreateDeviceCommandValidator`'s `MustExistIn` calls." with "`IManufacturerRepository` (`Domain/Manufacturers/`) — `IRepository<Manufacturer, ManufacturerId>` plus `GetWithModelsAsync` and `ModelExistsAsync`, no alternate key; `ModelExistsAsync` is what `CreateDeviceCommandValidator`'s local `MustAsync` calls." In the same bullet, change "(`IGetManufacturerQueryRepository`, `IGetManufacturerListQueryRepository`, `IManufacturerAllQueryRepository` — correctly spelled this time, don't propagate the `Devices` typo here)" to "(`IGetManufacturerQueryRepository`, `IGetManufacturerListQueryRepository`, `IGetDeviceModelQueryRepository`, `IGetDeviceModelListQueryRepository`, all joined by `IManufacturerAllQueryRepository` — correctly spelled this time, don't propagate the `Devices` typo here)".

- [ ] **Step 5: Root `.claude/CLAUDE.md` — three edits**

1. In the `.Infrastructure` bullet under "Per-module project layering" (the one beginning "**Every write-side repository derives from `EfRepository<TAggregateRoot, TId>`**"), replace "`ManufacturerRepository` is a one-line declaration." with "`ManufacturerRepository` was a one-line declaration until it gained the two lookups the `DeviceModel` entity needs (`GetWithModelsAsync`, `ModelExistsAsync`)."

2. Replace the whole bullet beginning "**Cross-aggregate references and alternate keys are checked in the validator, and the database is still the guarantee.**" with:

```markdown
- **Cross-aggregate references and alternate keys are checked in the validator, and the database is still the guarantee.** `CreateDeviceCommandValidator` answers device-model existence with a local `MustAsync(manufacturers.ModelExistsAsync)` on `DeviceModelId` — not `MustExistIn`, which is generic over aggregate roots, and a `DeviceModel` is an entity inside the `Manufacturer` aggregate with no repository of its own — and device-id uniqueness with `MustBeUniqueIn(devices)` on `DeviceId` (`CreateUserCommandValidator` does the same for `UserName`), so an unknown model or a duplicate id comes back as an `ErrorOr` validation error naming the property, alongside any shape errors — `CreateDeviceCommandHandler` itself still checks nothing. Both rules are advisory: the FK `dm.device.device_model_id → dm.device_model.id` and the unique indexes are what actually hold, and a race between the validator's query and `SaveChangesAsync` (the model deleted in between, two concurrent creates with the same id) still surfaces as a `DbUpdateException` over Postgres 23503/23505, not as `ErrorOr`. `RepositoryRuleTests.UniqueIndex_IsTheGuaranteeWhenTheValidatorIsBypassed` documents that. **An invariant inside one aggregate is the aggregate's, not the validator's**: `Manufacturer.AddModel` refuses a duplicate model name itself and returns `Error.Validation` — the same code and text `MustBeUniqueIn` would have produced — with the composite unique index `ix_device_model_manufacturer_id_name` as the guarantee (`DeviceModelTests.UniqueIndex_IsTheGuaranteeUnderTheRace`). Translating Postgres `SqlState`s into `ErrorOr` is a separate, cross-cutting slice — don't bolt a try/catch onto one handler. Design: `docs/superpowers/specs/2026-09-15-repository-existence-and-uniqueness-design.md`, and for the child-entity shape `docs/superpowers/specs/2026-09-16-device-models-design.md`.
```

3. In the **Validation** bullet under "Cross-cutting conventions": replace "`CreateDeviceCommandValidator(IDeviceRepository devices, IManufacturerRepository manufacturers)`, `CreateUserCommandValidator(IUserRepository users)`" (unchanged text, keep it) — and replace the sentence "Two shapes to copy: the rule targets the **wrapper** (`RuleFor(x => x.ManufacturerId).MustExistIn(manufacturers)`), not `.Value`, so `Error.Code` is already the property name and there is no `OverridePropertyName`; and it is guarded with `.When(x => <the shape rule passed>)` so no query runs for a value the shape rule already refused." with "Two shapes to copy: the rule targets the **wrapper** (`RuleFor(x => x.DeviceId).MustBeUniqueIn(devices)`), not `.Value`, so `Error.Code` is already the property name and there is no `OverridePropertyName`; and it is guarded with `.When(x => <the shape rule passed>)` so no query runs for a value the shape rule already refused. For a reference to an **entity inside another aggregate** (a `DeviceModel`), there is no repository for `MustExistIn` to take — write a local `MustAsync` over a named lookup on the owning aggregate's repository (`manufacturers.ModelExistsAsync`) with `MustExistIn`'s message text, as `CreateDeviceCommandValidator` does." Also change "Five validators today: `CreateUser` (shape + `MustBeUniqueIn`), `DeactivateUser`, `ProvisionExternalUser` (…), `CreateDevice` (shape + `MustBeUniqueIn` + `MustExistIn`), `CreateManufacturer`." to "Six validators today: `CreateUser` (shape + `MustBeUniqueIn`), `DeactivateUser`, `ProvisionExternalUser` (deliberately lenient — it runs inside the OIDC callback, see UserAccess's CLAUDE.md), `CreateDevice` (shape + `MustBeUniqueIn` + a local `MustAsync` for the model), `CreateManufacturer`, `AddDeviceModel` (shape only — not-found is the handler's and name uniqueness is `Manufacturer.AddModel`'s)."

4. In the `.Domain` bullet under "Per-module project layering", add a new sub-bullet after the one beginning "Strongly-typed IDs/value objects are declared as trailing `sealed record`s":

```markdown
  - A **child entity** of an aggregate derives from `Entity<TId>`, lives in its aggregate's folder (`Domain/Manufacturers/DeviceModel.cs`, not a folder of its own), has an `internal static Create` that only the root calls, and is added through a behaviour on the root (`Manufacturer.AddModel`) that holds the collection's invariants and returns `ErrorOr<TEntity>`. It gets no repository. When something outside the aggregate holds an FK to it, map it as a regular entity type with `HasMany(...).WithOne()` from the owner plus a `Navigation(...).HasField(...)` — **not `OwnsMany`**, which EF refuses on the principal side of a non-ownership relationship. The owning repository grows a `GetWith<Children>Async` (an `Include`) for the root's behaviour to work on, and any outside yes/no lookup (`ModelExistsAsync`).
```

- [ ] **Step 6: Rebuild the knowledge graph**

Run:
```bash
graphify update .
python .claude/skills/graphify/scripts/graphify_repair.py
```

Expected: both finish without error. `graphify-out/` is gitignored; nothing to commit from this step.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/DeviceManagement/CLAUDE.md .claude/CLAUDE.md
git commit -m "Document device models as entities of the Manufacturer aggregate"
```

---

### Task 12: Finish the branch

- [ ] **Step 1: Final verification from the worktree root**

```bash
dotnet build Sergin.MeterMinder.slnx
dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj
git status
```

Expected: build warning-free, all tests pass, working tree clean.

- [ ] **Step 2: Hand off**

Use `superpowers:finishing-a-development-branch`: push `feat/device-models`, open a PR against `main` whose description summarises the spec's Decisions 1, 2, 4, 5 and 7 (child entity; regular entity type not `OwnsMany`; model-only read models; invariant in the aggregate; destructive migration) and links `docs/superpowers/specs/2026-09-16-device-models-design.md`. After merge, remove the worktree and the branch (`git worktree remove ../Sergin.MeterMinder-device-models && git branch -d feat/device-models`).
