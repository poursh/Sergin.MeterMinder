# DeviceManagement Full CRUD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `UpdateDevice`, `UpdateManufacturer`, `RenameDeviceModel` and `RemoveDeviceModel` to the `dm` module, end to end: domain behaviour, MediatR slice with validator, WebApi endpoint, Blazor page, tests.

**Architecture:** Each operation is a CQRS write slice following the existing `Create`/`Delete`/`Add` slices in the same aggregate folder. All four are `[RequiresExpectedVersion]` and ride the existing optimistic-concurrency path. Model changes go through `Manufacturer` (the aggregate root). The device read model regains `ManufacturerId`/`ManufacturerName` so the edit page can preselect its manufacturer picker.

**Tech Stack:** .NET 10, MediatR, FluentValidation, EF Core (write side), Dapper raw SQL (read side), Blazor Server + MudBlazor 9, gRPC (proto3), xUnit + Testcontainers.PostgreSql.

**Spec:** `docs/superpowers/specs/2026-10-01-dm-full-crud-design.md`

## Global Constraints

- Work only in the worktree `.claude/worktrees/dm-full-crud` (branch `feature/dm-full-crud`). Submodules are already initialised there.
- `TreatWarningsAsErrors=true`, `AnalysisMode=All`, SonarAnalyzer: any warning fails the build. No `@code` blocks in `.razor` files; every line of C# goes in `.razor.cs`.
- File-scoped namespaces everywhere (`namespace X;`).
- Request/response records live in `.Application.Contracts` under `Sergin.MeterMinder.DeviceManagement.Application.Contracts.<Aggregate>[.<Child>].Commands.<Feature>`; handlers and validators in `.Application` under `Sergin.MeterMinder.DeviceManagement.Application.<Aggregate>[.<Child>].Commands.<Feature>`.
- Records are `sealed record`; handlers and validators `internal sealed class`; endpoints `internal class` (never sealed); WebApi `[FromBody]` DTOs plain `record`; Blazor code-behinds `public sealed partial class`; form models `public sealed class` with no DataAnnotations.
- Permissions: `permission.dm.devices.update` on `UpdateDeviceCommand`; `permission.dm.manufacturers.update` on `UpdateManufacturerCommand`, `RenameDeviceModelCommand`, `RemoveDeviceModelCommand`.
- Every response is `<Feature>CommandResponse(Guid Id)`.
- Duplicate model name error is exactly `Error.Validation(nameof(DeviceModel.Name), "'Name' is already in use.")`.
- Device id uniqueness message is exactly `'{PropertyName}' is already in use.`.
- Remove-in-use message is exactly `The model is still used by a device; delete those devices first.`.
- Blazor route templates: `/dm/devices/{Id:guid}/edit`, `/dm/manufacturers/{Id:guid}/edit`, `/dm/manufacturers/{ManufacturerId:guid}/models/{Id:guid}/edit`.
- WebApi routes carry no schema segment.
- No migrations in this plan.
- `EnforceCodeStyleInBuild` can turn an unused `using` (IDE0005) into a build error. If the build flags one in a file this plan created, delete that `using`; do not suppress the rule.
- Tests need Docker running. If `docker info` fails, start Docker Desktop and poll `docker info` until it answers.
- Test command used throughout: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "<filter>"`.

## Review Focus

1. An edit submitted with no changes must succeed (EF writes nothing; the version stays). Pinned in Task 1 (`UpdateDevice_KeepingEverything_Succeeds`).
2. A model name freed by a rename must be reusable by `AddDeviceModel` straight away. Pinned in Task 3 (`RenameDeviceModel_FreesTheOldName`).
3. A model name freed by a remove must be reusable by `AddDeviceModel` straight away (the partial unique index and the Include's soft-delete filter both have to cooperate). Pinned in Task 4 (`RemoveDeviceModel_FreesItsName`).
4. Moving every device off a manufacturer's models must make that manufacturer deletable. Pinned in Task 1 (`UpdateDevice_MovingOffAManufacturer_LetsItBeDeleted`).
5. An edit page for an unknown id must render 200 with the problem panel and placeholder breadcrumbs, not throw. Pinned in Tasks 7–9 (breadcrumb theory with unseeded ids).

---

### Task 1: UpdateDevice slice

**Files:**
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Domain/Devices/Device.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Domain/Devices/IDeviceRepository.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure/Devices/Repositories/DeviceRepository.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Devices/Commands/Update/UpdateDeviceCommand.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Devices/Commands/Update/UpdateDeviceCommandResponse.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/Update/UpdateDeviceCommandHandler.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/Update/UpdateDeviceCommandValidator.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/GlobalUsings.cs`
- Modify: `src/Hosts/Sergin.MeterMinder.Hosts.All/appsettings.json` (`Sergin:DevUser:Permissions`)
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/GlobalUsings.cs`
- Create: `tests/Sergin.MeterMinder.IntegrationTests.All/Updates/DeviceManagementUpdateTests.cs` (shared helpers)
- Create: `tests/Sergin.MeterMinder.IntegrationTests.All/Updates/DeviceManagementUpdateTests.UpdateDevice.cs`
- Create: `tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/DeviceManagementConcurrencyTests.Updates.cs`

**Interfaces:**
- Consumes: `VersionedDispatch.SendAtDeviceVersionAsync` / `SendAtManufacturerVersionAsync` (existing, `tests/.../Concurrency/VersionedDispatch.cs`); the private helpers `CreateManufacturerAsync(ISerginDispatcher) → Task<ManufacturerId>`, `AddDeviceModelAsync(ISerginDispatcher, ManufacturerId) → Task<DeviceModelInternalId>`, `CreateDeviceAsync(ISerginDispatcher) → Task<Guid>` already in `DeviceManagementConcurrencyTests.cs`.
- Produces:
  - `public void Device.Update(DeviceId deviceId, DeviceModelInternalId deviceModelId)`
  - `Task<bool> IDeviceRepository.IsTakenByOtherAsync(DeviceId key, DeviceIntenralId exceptId, CancellationToken cancellationToken = default)`
  - `public sealed record UpdateDeviceCommand(Guid Id, DeviceId DeviceId, DeviceModelInternalId DeviceModelId) : ICommand<UpdateDeviceCommandResponse>` in namespace `Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Update`
  - `public sealed record UpdateDeviceCommandResponse(Guid Id)`
  - Test helpers on `DeviceManagementUpdateTests` (partial class, used by Tasks 2–4): `CreateManufacturerAsync(ISerginDispatcher, string? name = null) → Task<ManufacturerId>`, `AddDeviceModelAsync(ISerginDispatcher, ManufacturerId, string? name = null) → Task<DeviceModelInternalId>`, `CreateDeviceAsync(ISerginDispatcher, DeviceModelInternalId, string? deviceId = null) → Task<Guid>`, `DeleteDeviceAsync(ISerginDispatcher, Guid) → Task`, `ReadModifiedAsync<TEntity>(Expression<Func<TEntity,bool>>) → Task<ModifiedStamp>`, `ReadDeletedAtAsync<TEntity>(Expression<Func<TEntity,bool>>) → Task<DateTime?>`, `AnonymousDispatcher(IServiceScope) → ISerginDispatcher`, `NewName(string prefix) → string`.

- [ ] **Step 1: Add the contract records so tests compile**

`Application.Contracts/Devices/Commands/Update/UpdateDeviceCommand.cs`:

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Update;

[RequiredPermissions("permission.dm.devices.update")]
[RequiresExpectedVersion]
public sealed record UpdateDeviceCommand(Guid Id, DeviceId DeviceId, DeviceModelInternalId DeviceModelId)
    : ICommand<UpdateDeviceCommandResponse>;
```

`Application.Contracts/Devices/Commands/Update/UpdateDeviceCommandResponse.cs`:

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Update;

public sealed record UpdateDeviceCommandResponse(Guid Id);
```

Add to `tests/Sergin.MeterMinder.IntegrationTests.All/GlobalUsings.cs`, after the `Devices.Commands.GetOne` contracts line:

```csharp
global using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Update;
```

Add the same line to `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/GlobalUsings.cs`, after its `Devices.Commands.GetOne` line.

- [ ] **Step 2: Write the shared test helpers**

`tests/Sergin.MeterMinder.IntegrationTests.All/Updates/DeviceManagementUpdateTests.cs`:

```csharp
using System.Linq.Expressions;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.MeterMinder.IntegrationTests.All.Concurrency;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Updates;

/// <summary>
/// The four update-side slices — UpdateDevice, UpdateManufacturer, RenameDeviceModel, RemoveDeviceModel —
/// through ISerginDispatcher from a scope, as the edit pages send them. Guarded sends go through
/// VersionedDispatch; the 428/412 cases live in DeviceManagementConcurrencyTests.Updates.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed partial class DeviceManagementUpdateTests(SerginWebApiFactory<Program> factory)
{
    private static readonly Guid DevUserId = Guid.Parse("01920000-0000-7000-8000-000000000001");

    private static readonly string[] SoftDeleteFilter = [SoftDeleteColumns.QueryFilterName];

    private static string NewName(string prefix) => $"{prefix}-{Guid.CreateVersion7()}";

    private static ISerginDispatcher AnonymousDispatcher(IServiceScope scope)
    {
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = ClaimsPrincipalUserContext.Create(null);

        return scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
    }

    private static async Task<ManufacturerId> CreateManufacturerAsync(ISerginDispatcher dispatcher, string? name = null)
    {
        ErrorOr<CreateManufacturerCommandResponse> created = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName(name ?? NewName("manufacturer")), Address: null));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);
        return new ManufacturerId(created.Value.Id);
    }

    private static async Task<DeviceModelInternalId> AddDeviceModelAsync(
        ISerginDispatcher dispatcher, ManufacturerId manufacturerId, string? name = null)
    {
        ErrorOr<AddDeviceModelCommandResponse> added = await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new AddDeviceModelCommand(manufacturerId, new DeviceModelName(name ?? NewName("model"))));

        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);
        return new DeviceModelInternalId(added.Value.Id);
    }

    private static async Task<Guid> CreateDeviceAsync(
        ISerginDispatcher dispatcher, DeviceModelInternalId modelId, string? deviceId = null)
    {
        ErrorOr<CreateDeviceCommandResponse> created = await dispatcher.SendAsync(
            new CreateDeviceCommand(new DeviceId(deviceId ?? NewName("device")), modelId));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);
        return created.Value.Id;
    }

    private static async Task DeleteDeviceAsync(ISerginDispatcher dispatcher, Guid id)
    {
        ErrorOr<DeleteDeviceCommandResponse> deleted = await dispatcher.SendAtDeviceVersionAsync(id, new DeleteDeviceCommand(id));

        Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Description : string.Empty);
    }

    private async Task<ModifiedStamp> ReadModifiedAsync<TEntity>(Expression<Func<TEntity, bool>> match)
        where TEntity : class
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IDeviceManagementDbContext context = scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>();

        return await context.Set<TEntity>()
            .Where(match)
            .Select(entity => new ModifiedStamp(
                EF.Property<DateTime?>(entity, AuditColumns.ModifiedAtUtc),
                EF.Property<Guid?>(entity, AuditColumns.ModifiedBy)))
            .SingleAsync();
    }

    private async Task<DateTime?> ReadDeletedAtAsync<TEntity>(Expression<Func<TEntity, bool>> match)
        where TEntity : class
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IDeviceManagementDbContext context = scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>();

        return await context.Set<TEntity>()
            .IgnoreQueryFilters(SoftDeleteFilter)
            .Where(match)
            .Select(entity => EF.Property<DateTime?>(entity, SoftDeleteColumns.DeletedAtUtc))
            .SingleAsync();
    }

    private sealed record ModifiedStamp(DateTime? ModifiedAtUtc, Guid? ModifiedBy);
}
```

`UserContextAccessor` and `ClaimsPrincipalUserContext` both live in `Sergin.SharedKernel.Application.Securities.Users`.

- [ ] **Step 3: Write the failing UpdateDevice tests**

`tests/Sergin.MeterMinder.IntegrationTests.All/Updates/DeviceManagementUpdateTests.UpdateDevice.cs`:

```csharp
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Concurrency;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Updates;

public sealed partial class DeviceManagementUpdateTests
{
    [Fact]
    public async Task UpdateDevice_ChangesBothFields_BumpsTheVersion_AndStampsModified()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelInternalId first = await AddDeviceModelAsync(dispatcher, manufacturerId);
        DeviceModelInternalId second = await AddDeviceModelAsync(dispatcher, manufacturerId);
        Guid id = await CreateDeviceAsync(dispatcher, first);
        string newDeviceId = NewName("device");

        ErrorOr<Versioned<DeviceQueryResponse>> loaded = await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(id));
        ErrorOr<Versioned<UpdateDeviceCommandResponse>> updated = await dispatcher.SendVersionedAsync(
            new UpdateDeviceCommand(id, new DeviceId(newDeviceId), second), loaded.Value.Version);

        Assert.False(updated.IsError, updated.IsError ? updated.FirstError.Description : string.Empty);
        Assert.Equal(id, updated.Value.Value.Id);
        Assert.NotEqual(loaded.Value.Version, updated.Value.Version);

        ErrorOr<DeviceQueryResponse> read = await dispatcher.SendAsync(new GetDeviceByIdQueryCommand(id));
        Assert.Equal(newDeviceId, read.Value.DeviceId);
        Assert.Equal(second.Value, read.Value.DeviceModelId);

        ModifiedStamp stamp = await ReadModifiedAsync<Device>(device => device.Id == new DeviceIntenralId(id));
        Assert.NotNull(stamp.ModifiedAtUtc);
        Assert.Equal(DevUserId, stamp.ModifiedBy);
    }

    [Fact]
    public async Task UpdateDevice_KeepingEverything_Succeeds()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, await CreateManufacturerAsync(dispatcher));
        string deviceId = NewName("device");
        Guid id = await CreateDeviceAsync(dispatcher, model, deviceId);

        ErrorOr<UpdateDeviceCommandResponse> updated = await dispatcher.SendAtDeviceVersionAsync(
            id, new UpdateDeviceCommand(id, new DeviceId(deviceId), model));

        Assert.False(updated.IsError, updated.IsError ? updated.FirstError.Description : string.Empty);
    }

    [Fact]
    public async Task UpdateDevice_ToAnotherLiveDevicesId_IsAValidationErrorOnDeviceId()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, await CreateManufacturerAsync(dispatcher));
        string taken = NewName("device");
        await CreateDeviceAsync(dispatcher, model, taken);
        Guid id = await CreateDeviceAsync(dispatcher, model);

        ErrorOr<UpdateDeviceCommandResponse> updated = await dispatcher.SendAtDeviceVersionAsync(
            id, new UpdateDeviceCommand(id, new DeviceId(taken), model));

        Error error = Assert.Single(updated.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(UpdateDeviceCommand.DeviceId), error.Code);
    }

    [Fact]
    public async Task UpdateDevice_ToADeletedDevicesId_Succeeds()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, await CreateManufacturerAsync(dispatcher));
        string freed = NewName("device");
        await DeleteDeviceAsync(dispatcher, await CreateDeviceAsync(dispatcher, model, freed));
        Guid id = await CreateDeviceAsync(dispatcher, model);

        ErrorOr<UpdateDeviceCommandResponse> updated = await dispatcher.SendAtDeviceVersionAsync(
            id, new UpdateDeviceCommand(id, new DeviceId(freed), model));

        Assert.False(updated.IsError, updated.IsError ? updated.FirstError.Description : string.Empty);
    }

    [Fact]
    public async Task UpdateDevice_ToAnUnknownModel_IsAValidationErrorOnDeviceModelId()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, await CreateManufacturerAsync(dispatcher));
        Guid id = await CreateDeviceAsync(dispatcher, model);

        ErrorOr<UpdateDeviceCommandResponse> updated = await dispatcher.SendAtDeviceVersionAsync(
            id, new UpdateDeviceCommand(id, new DeviceId(NewName("device")), new DeviceModelInternalId(Guid.CreateVersion7())));

        Error error = Assert.Single(updated.Errors);
        Assert.Equal(nameof(UpdateDeviceCommand.DeviceModelId), error.Code);
    }

    [Fact]
    public async Task UpdateDevice_ForAnUnknownDevice_IsNotFound()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, await CreateManufacturerAsync(dispatcher));
        Guid unknown = Guid.CreateVersion7();

        ErrorOr<UpdateDeviceCommandResponse> updated = await dispatcher.SendAtDeviceVersionAsync(
            unknown, new UpdateDeviceCommand(unknown, new DeviceId(NewName("device")), model));

        Assert.Equal(ErrorType.NotFound, updated.FirstError.Type);
    }

    [Fact]
    public async Task UpdateDevice_MovingOffAManufacturer_LetsItBeDeleted()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId old = await CreateManufacturerAsync(dispatcher);
        DeviceModelInternalId oldModel = await AddDeviceModelAsync(dispatcher, old);
        DeviceModelInternalId newModel = await AddDeviceModelAsync(dispatcher, await CreateManufacturerAsync(dispatcher));
        string deviceId = NewName("device");
        Guid id = await CreateDeviceAsync(dispatcher, oldModel, deviceId);

        Assert.False((await dispatcher.SendAtDeviceVersionAsync(
            id, new UpdateDeviceCommand(id, new DeviceId(deviceId), newModel))).IsError);

        ErrorOr<DeleteManufacturerCommandResponse> deleted =
            await dispatcher.SendAtManufacturerVersionAsync(old.Value, new DeleteManufacturerCommand(old.Value));

        Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Description : string.Empty);
    }

    [Fact]
    public async Task UpdateDevice_WithoutThePermission_IsForbidden()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = AnonymousDispatcher(scope);
        Guid id = Guid.CreateVersion7();

        ErrorOr<UpdateDeviceCommandResponse> updated = await dispatcher.SendAsync(
            new UpdateDeviceCommand(id, new DeviceId(NewName("device")), new DeviceModelInternalId(Guid.CreateVersion7())));

        Assert.Equal(ErrorType.Forbidden, updated.FirstError.Type);
    }
}
```

`tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/DeviceManagementConcurrencyTests.Updates.cs` (Tasks 2–4 append to this file):

```csharp
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

public sealed partial class DeviceManagementConcurrencyTests
{
    [Fact]
    public async Task UpdateDevice_WithoutAVersion_IsRefusedWith428()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid id = await CreateDeviceAsync(dispatcher);
        DeviceQueryResponse device = (await dispatcher.SendAsync(new GetDeviceByIdQueryCommand(id))).Value;

        ErrorOr<UpdateDeviceCommandResponse> updated = await dispatcher.SendAsync(
            new UpdateDeviceCommand(id, new DeviceId($"device-{Guid.CreateVersion7()}"), new DeviceModelInternalId(device.DeviceModelId)));

        Assert.Equal(VersionErrors.RequiredType, (int)updated.FirstError.Type);
    }

    [Fact]
    public async Task UpdateDevice_WithAStaleVersion_IsRefused_AndNothingIsSaved()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid id = await CreateDeviceAsync(dispatcher);
        Guid other = await CreateDeviceAsync(dispatcher);
        ErrorOr<Versioned<DeviceQueryResponse>> loaded = await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(id));
        ErrorOr<Versioned<DeviceQueryResponse>> otherLoaded = await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(other));

        ErrorOr<Versioned<UpdateDeviceCommandResponse>> updated = await dispatcher.SendVersionedAsync(
            new UpdateDeviceCommand(id, new DeviceId($"device-{Guid.CreateVersion7()}"), new DeviceModelInternalId(loaded.Value.Value.DeviceModelId)),
            otherLoaded.Value.Version);

        Assert.True(VersionErrors.IsStale(updated.FirstError));
        Assert.Equal(loaded.Value.Value.DeviceId, (await dispatcher.SendAsync(new GetDeviceByIdQueryCommand(id))).Value.DeviceId);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet build Sergin.MeterMinder.slnx` — expected: succeeds (no handler yet).
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~UpdateDevice"`
Expected: FAIL. With no handler registered, MediatR throws `InvalidOperationException` ("No service for type ... IRequestHandler"), except the Forbidden and 428 cases, which the pipeline answers before the handler and so may already pass.

- [ ] **Step 5: Implement domain and repository**

In `Device.cs`, add after `Create`:

```csharp
    public void Update(DeviceId deviceId, DeviceModelInternalId deviceModelId)
    {
        DeviceId = deviceId;
        DeviceModelId = deviceModelId;
    }
```

Update the comment above `DeviceId` record: replace `CreateDeviceCommandValidator and NewDeviceFormModel's [StringLength] both read` with `CreateDeviceCommandValidator and UpdateDeviceCommandValidator both read`.

In `IDeviceRepository.cs`, add:

```csharp
    /// <summary>
    /// Whether a live device other than <paramref name="exceptId"/> already uses <paramref name="key"/> —
    /// <c>IsTakenAsync</c> for an update, where the device's own unchanged id must not count. Advisory: the
    /// partial unique index <c>ix_device_device_id</c> is the guarantee.
    /// </summary>
    Task<bool> IsTakenByOtherAsync(DeviceId key, DeviceIntenralId exceptId, CancellationToken cancellationToken = default);
```

In `DeviceRepository.cs`, add after `IsTakenAsync`:

```csharp
    public Task<bool> IsTakenByOtherAsync(DeviceId key, DeviceIntenralId exceptId, CancellationToken cancellationToken = default)
        => AnyAsync(d => d.DeviceId == key && d.Id != exceptId, cancellationToken);
```

- [ ] **Step 6: Implement handler and validator**

`Application/Devices/Commands/Update/UpdateDeviceCommandHandler.cs`:

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Update;

// A mutation of an existing aggregate, so the handler owns not-found. GetAsync hides a deleted device through
// the soft-delete query filter, so updating one is a not-found too.
internal sealed class UpdateDeviceCommandHandler(
    IDeviceManagementUnitOfWork unitOfWork,
    IDeviceRepository repository) : ICommandHandler<UpdateDeviceCommand, UpdateDeviceCommandResponse>
{
    public async Task<ErrorOr<UpdateDeviceCommandResponse>> Handle(
        UpdateDeviceCommand request, CancellationToken cancellationToken)
    {
        Device? device = await repository.GetAsync(new DeviceIntenralId(request.Id), cancellationToken);

        if (device is null)
        {
            return Error.NotFound();
        }

        device.Update(request.DeviceId, request.DeviceModelId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateDeviceCommandResponse(device.Id.Value);
    }
}
```

`Application/Devices/Commands/Update/UpdateDeviceCommandValidator.cs`:

```csharp
using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Update;

// CreateDeviceCommandValidator's rules, with one difference: uniqueness excludes the device itself, so a save
// that keeps the device's own id passes. That is a local MustAsync over IsTakenByOtherAsync rather than
// MustBeUniqueIn, whose IsTakenAsync cannot tell "taken by me" from "taken by another"; the message is
// MustBeUniqueIn's so the two read alike.
internal sealed class UpdateDeviceCommandValidator : AbstractValidator<UpdateDeviceCommand>
{
    public UpdateDeviceCommandValidator(IDeviceRepository devices, IManufacturerRepository manufacturers)
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.DeviceId.Value)
            .NotEmpty()
            .MaximumLength(DeviceId.MaxLength)
            .OverridePropertyName(nameof(UpdateDeviceCommand.DeviceId));

        RuleFor(x => x.DeviceModelId.Value)
            .NotEmpty()
            .OverridePropertyName(nameof(UpdateDeviceCommand.DeviceModelId));

        RuleFor(x => x.DeviceId)
            .MustAsync(async (command, deviceId, cancellationToken) =>
                !await devices.IsTakenByOtherAsync(deviceId, new DeviceIntenralId(command.Id), cancellationToken))
            .WithMessage("'{PropertyName}' is already in use.")
            .When(x => !string.IsNullOrWhiteSpace(x.DeviceId.Value) && x.Id != Guid.Empty);

        RuleFor(x => x.DeviceModelId)
            .MustAsync(manufacturers.ModelExistsAsync)
            .WithMessage("'{PropertyName}' must refer to an existing DeviceModel.")
            .When(x => x.DeviceModelId.Value != Guid.Empty);
    }
}
```

In `src/Hosts/Sergin.MeterMinder.Hosts.All/appsettings.json`, `Sergin:DevUser:Permissions` becomes:

```json
      "Permissions": [
        "permission.dm.devices.read",
        "permission.dm.devices.update",
        "permission.dm.devices.delete",
        "permission.dm.manufacturers.read",
        "permission.dm.manufacturers.update",
        "permission.dm.manufacturers.delete",
        "permission.ua.users.read"
      ]
```

(Both `.update` entries go in now so Tasks 2–4 need no config edit.)

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet build Sergin.MeterMinder.slnx` — expected: 0 warnings, 0 errors.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~UpdateDevice"`
Expected: all PASS.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~DevUser|FullyQualifiedName~AuthOptions"` — expected: PASS (the new permission strings must be valid `Permission` values).

- [ ] **Step 8: Commit**

```bash
git add -A src/Modules/DeviceManagement src/Hosts/Sergin.MeterMinder.Hosts.All/appsettings.json tests/Sergin.MeterMinder.IntegrationTests.All
git commit -m "UpdateDevice: change a device's id and model, version-guarded

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: UpdateManufacturer slice

**Files:**
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Domain/Manufacturers/Manufacturer.cs`
- Create: `.../Application.Contracts/Manufacturers/Commands/Update/UpdateManufacturerCommand.cs`
- Create: `.../Application.Contracts/Manufacturers/Commands/Update/UpdateManufacturerCommandResponse.cs`
- Create: `.../Application/Manufacturers/Commands/Update/UpdateManufacturerCommandHandler.cs`
- Create: `.../Application/Manufacturers/Commands/Update/UpdateManufacturerCommandValidator.cs`
- Modify: `.../Application/GlobalUsings.cs`, `tests/.../GlobalUsings.cs`
- Create: `tests/.../Updates/DeviceManagementUpdateTests.UpdateManufacturer.cs`
- Modify: `tests/.../Concurrency/DeviceManagementConcurrencyTests.Updates.cs`

(`...` = `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement`)

**Interfaces:**
- Consumes: Task 1 test helpers (`CreateManufacturerAsync`, `AnonymousDispatcher`, `NewName`, `ReadModifiedAsync`, `DevUserId`).
- Produces:
  - `public void Manufacturer.Update(ManufacturerName name, ManufacturerAddress? address)`
  - `public sealed record UpdateManufacturerCommand(Guid Id, ManufacturerName Name, ManufacturerAddress? Address) : ICommand<UpdateManufacturerCommandResponse>` in `Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Update`
  - `public sealed record UpdateManufacturerCommandResponse(Guid Id)`

- [ ] **Step 1: Add the contract records**

`UpdateManufacturerCommand.cs`:

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Update;

[RequiredPermissions("permission.dm.manufacturers.update")]
[RequiresExpectedVersion]
public sealed record UpdateManufacturerCommand(Guid Id, ManufacturerName Name, ManufacturerAddress? Address)
    : ICommand<UpdateManufacturerCommandResponse>;
```

`UpdateManufacturerCommandResponse.cs`:

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Update;

public sealed record UpdateManufacturerCommandResponse(Guid Id);
```

Add `global using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Update;` to both `Application/GlobalUsings.cs` and `tests/.../GlobalUsings.cs`, after the `Manufacturers.Commands.GetOne` contracts line.

- [ ] **Step 2: Write the failing tests**

`tests/.../Updates/DeviceManagementUpdateTests.UpdateManufacturer.cs`:

```csharp
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.IntegrationTests.All.Concurrency;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Updates;

public sealed partial class DeviceManagementUpdateTests
{
    [Fact]
    public async Task UpdateManufacturer_ChangesNameAndAddress_AndStampsModified()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId id = await CreateManufacturerAsync(dispatcher);
        string name = NewName("manufacturer");

        ErrorOr<UpdateManufacturerCommandResponse> updated = await dispatcher.SendAtManufacturerVersionAsync(
            id.Value, new UpdateManufacturerCommand(id.Value, new ManufacturerName(name), new ManufacturerAddress("1 Meter Way")));

        Assert.False(updated.IsError, updated.IsError ? updated.FirstError.Description : string.Empty);
        ManufacturerQueryResponse read = (await dispatcher.SendAsync(new GetManufacturerByIdQueryCommand(id.Value))).Value;
        Assert.Equal(name, read.Name);
        Assert.Equal("1 Meter Way", read.Address);

        ModifiedStamp stamp = await ReadModifiedAsync<Manufacturer>(m => m.Id == id);
        Assert.NotNull(stamp.ModifiedAtUtc);
        Assert.Equal(DevUserId, stamp.ModifiedBy);
    }

    [Fact]
    public async Task UpdateManufacturer_WithANullAddress_ClearsIt()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId id = await CreateManufacturerAsync(dispatcher);
        string name = NewName("manufacturer");
        Assert.False((await dispatcher.SendAtManufacturerVersionAsync(
            id.Value, new UpdateManufacturerCommand(id.Value, new ManufacturerName(name), new ManufacturerAddress("1 Meter Way")))).IsError);

        ErrorOr<UpdateManufacturerCommandResponse> cleared = await dispatcher.SendAtManufacturerVersionAsync(
            id.Value, new UpdateManufacturerCommand(id.Value, new ManufacturerName(name), Address: null));

        Assert.False(cleared.IsError, cleared.IsError ? cleared.FirstError.Description : string.Empty);
        Assert.Null((await dispatcher.SendAsync(new GetManufacturerByIdQueryCommand(id.Value))).Value.Address);
    }

    [Fact]
    public async Task UpdateManufacturer_WithABlankName_IsAValidationErrorOnName()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId id = await CreateManufacturerAsync(dispatcher);

        ErrorOr<UpdateManufacturerCommandResponse> updated = await dispatcher.SendAtManufacturerVersionAsync(
            id.Value, new UpdateManufacturerCommand(id.Value, new ManufacturerName(string.Empty), Address: null));

        Assert.Contains(updated.Errors, error => error.Code == nameof(UpdateManufacturerCommand.Name));
    }

    [Fact]
    public async Task UpdateManufacturer_ForAnUnknownManufacturer_IsNotFound()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid unknown = Guid.CreateVersion7();

        ErrorOr<UpdateManufacturerCommandResponse> updated = await dispatcher.SendAtManufacturerVersionAsync(
            unknown, new UpdateManufacturerCommand(unknown, new ManufacturerName(NewName("manufacturer")), Address: null));

        Assert.Equal(ErrorType.NotFound, updated.FirstError.Type);
    }

    [Fact]
    public async Task UpdateManufacturer_WithoutThePermission_IsForbidden()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = AnonymousDispatcher(scope);

        ErrorOr<UpdateManufacturerCommandResponse> updated = await dispatcher.SendAsync(
            new UpdateManufacturerCommand(Guid.CreateVersion7(), new ManufacturerName(NewName("manufacturer")), Address: null));

        Assert.Equal(ErrorType.Forbidden, updated.FirstError.Type);
    }
}
```

Add `using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;` to the top of `DeviceManagementConcurrencyTests.Updates.cs` (between the `Domain.Devices` and `Domain.Manufacturers.DeviceModels` lines), then append inside the class:

```csharp
    [Fact]
    public async Task UpdateManufacturer_WithoutAVersion_IsRefusedWith428()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId id = await CreateManufacturerAsync(dispatcher);

        ErrorOr<UpdateManufacturerCommandResponse> updated = await dispatcher.SendAsync(
            new UpdateManufacturerCommand(id.Value, new ManufacturerName($"manufacturer-{Guid.CreateVersion7()}"), Address: null));

        Assert.Equal(VersionErrors.RequiredType, (int)updated.FirstError.Type);
    }

    [Fact]
    public async Task UpdateManufacturer_WithAStaleVersion_IsRefused_AndNothingIsSaved()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId id = await CreateManufacturerAsync(dispatcher);
        ManufacturerId other = await CreateManufacturerAsync(dispatcher);
        ErrorOr<Versioned<ManufacturerQueryResponse>> loaded = await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(id.Value));
        ErrorOr<Versioned<ManufacturerQueryResponse>> otherLoaded = await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(other.Value));

        ErrorOr<Versioned<UpdateManufacturerCommandResponse>> updated = await dispatcher.SendVersionedAsync(
            new UpdateManufacturerCommand(id.Value, new ManufacturerName($"manufacturer-{Guid.CreateVersion7()}"), Address: null),
            otherLoaded.Value.Version);

        Assert.True(VersionErrors.IsStale(updated.FirstError));
        Assert.Equal(loaded.Value.Value.Name, (await dispatcher.SendAsync(new GetManufacturerByIdQueryCommand(id.Value))).Value.Name);
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~UpdateManufacturer"`
Expected: FAIL (no handler registered for `UpdateManufacturerCommand`).

- [ ] **Step 4: Implement**

In `Manufacturer.cs`, add after `Create`:

```csharp
    public void Update(ManufacturerName name, ManufacturerAddress? address)
    {
        Name = name;
        Address = address;
    }
```

`Application/Manufacturers/Commands/Update/UpdateManufacturerCommandHandler.cs`:

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Update;

internal sealed class UpdateManufacturerCommandHandler(
    IDeviceManagementUnitOfWork unitOfWork,
    IManufacturerRepository repository) : ICommandHandler<UpdateManufacturerCommand, UpdateManufacturerCommandResponse>
{
    public async Task<ErrorOr<UpdateManufacturerCommandResponse>> Handle(
        UpdateManufacturerCommand request, CancellationToken cancellationToken)
    {
        Manufacturer? manufacturer = await repository.GetAsync(new ManufacturerId(request.Id), cancellationToken);

        if (manufacturer is null)
        {
            return Error.NotFound();
        }

        manufacturer.Update(request.Name, request.Address);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateManufacturerCommandResponse(manufacturer.Id.Value);
    }
}
```

`Application/Manufacturers/Commands/Update/UpdateManufacturerCommandValidator.cs`:

```csharp
using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Update;

// CreateManufacturerCommandValidator's shape rules plus the id. Manufacturer names are not unique, so there is
// no repository rule; not-found is the handler's.
internal sealed class UpdateManufacturerCommandValidator : AbstractValidator<UpdateManufacturerCommand>
{
    public UpdateManufacturerCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Name.Value)
            .NotEmpty()
            .MaximumLength(ManufacturerName.MaxLength)
            .OverridePropertyName(nameof(UpdateManufacturerCommand.Name));

        // Optional, but when supplied it has to be a real value — an empty string is not "no address".
        RuleFor(x => x.Address!.Value)
            .NotEmpty()
            .MaximumLength(ManufacturerAddress.MaxLength)
            .OverridePropertyName(nameof(UpdateManufacturerCommand.Address))
            .When(x => x.Address is not null);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet build Sergin.MeterMinder.slnx` — expected: 0 warnings, 0 errors.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~UpdateManufacturer"` — expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add -A src/Modules/DeviceManagement tests/Sergin.MeterMinder.IntegrationTests.All
git commit -m "UpdateManufacturer: change a manufacturer's name and address, version-guarded

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: RenameDeviceModel slice

**Files:**
- Modify: `.../Domain/Manufacturers/Manufacturer.cs`
- Modify: `.../Domain/Manufacturers/DeviceModels/DeviceModel.cs`
- Create: `.../Application.Contracts/Manufacturers/DeviceModels/Commands/Rename/RenameDeviceModelCommand.cs`
- Create: `.../Application.Contracts/Manufacturers/DeviceModels/Commands/Rename/RenameDeviceModelCommandResponse.cs`
- Create: `.../Application/Manufacturers/DeviceModels/Commands/Rename/RenameDeviceModelCommandHandler.cs`
- Create: `.../Application/Manufacturers/DeviceModels/Commands/Rename/RenameDeviceModelCommandValidator.cs`
- Modify: `.../Application/GlobalUsings.cs`, `tests/.../GlobalUsings.cs`
- Create: `tests/.../Updates/DeviceManagementUpdateTests.RenameDeviceModel.cs`
- Modify: `tests/.../Concurrency/DeviceManagementConcurrencyTests.Updates.cs`

**Interfaces:**
- Consumes: Task 1 test helpers.
- Produces:
  - `internal void DeviceModel.Rename(DeviceModelName name)`
  - `public ErrorOr<DeviceModel> Manufacturer.RenameModel(DeviceModelInternalId id, DeviceModelName name)`
  - `private static Error Manufacturer.NameInUse()` (used by Task 3 and the existing `AddModel`)
  - `public sealed record RenameDeviceModelCommand(ManufacturerId ManufacturerId, Guid Id, DeviceModelName Name) : ICommand<RenameDeviceModelCommandResponse>` in `Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Rename`
  - `public sealed record RenameDeviceModelCommandResponse(Guid Id)`

- [ ] **Step 1: Add the contract records**

`RenameDeviceModelCommand.cs`:

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Rename;

// Guarded by the manufacturer's version and permission: renaming a model changes the Manufacturer aggregate.
[RequiredPermissions("permission.dm.manufacturers.update")]
[RequiresExpectedVersion]
public sealed record RenameDeviceModelCommand(ManufacturerId ManufacturerId, Guid Id, DeviceModelName Name)
    : ICommand<RenameDeviceModelCommandResponse>;
```

`RenameDeviceModelCommandResponse.cs`:

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Rename;

public sealed record RenameDeviceModelCommandResponse(Guid Id);
```

Add `global using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Rename;` to both `Application/GlobalUsings.cs` and `tests/.../GlobalUsings.cs`, after the `DeviceModels.Commands.GetOne` contracts line.

- [ ] **Step 2: Write the failing tests**

`tests/.../Updates/DeviceManagementUpdateTests.RenameDeviceModel.cs`:

```csharp
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Concurrency;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Updates;

public sealed partial class DeviceManagementUpdateTests
{
    [Fact]
    public async Task RenameDeviceModel_ChangesItsName()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, manufacturerId);
        string name = NewName("model");

        ErrorOr<RenameDeviceModelCommandResponse> renamed = await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new RenameDeviceModelCommand(manufacturerId, model.Value, new DeviceModelName(name)));

        Assert.False(renamed.IsError, renamed.IsError ? renamed.FirstError.Description : string.Empty);
        Assert.Equal(model.Value, renamed.Value.Id);
        DeviceModelQueryResponse read =
            (await dispatcher.SendAsync(new GetDeviceModelByIdQueryCommand(manufacturerId.Value, model.Value))).Value;
        Assert.Equal(name, read.Name);
    }

    [Fact]
    public async Task RenameDeviceModel_ToItsOwnName_Succeeds()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        string name = NewName("model");
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, manufacturerId, name);

        ErrorOr<RenameDeviceModelCommandResponse> renamed = await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new RenameDeviceModelCommand(manufacturerId, model.Value, new DeviceModelName(name)));

        Assert.False(renamed.IsError, renamed.IsError ? renamed.FirstError.Description : string.Empty);
    }

    [Fact]
    public async Task RenameDeviceModel_ToASiblingsName_IsAValidationErrorOnName()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        string sibling = NewName("model");
        await AddDeviceModelAsync(dispatcher, manufacturerId, sibling);
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, manufacturerId);

        ErrorOr<RenameDeviceModelCommandResponse> renamed = await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new RenameDeviceModelCommand(manufacturerId, model.Value, new DeviceModelName(sibling)));

        Error error = Assert.Single(renamed.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(DeviceModel.Name), error.Code);
        Assert.Equal("'Name' is already in use.", error.Description);
    }

    [Fact]
    public async Task RenameDeviceModel_FreesTheOldName()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        string oldName = NewName("model");
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, manufacturerId, oldName);
        Assert.False((await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new RenameDeviceModelCommand(manufacturerId, model.Value, new DeviceModelName(NewName("model"))))).IsError);

        ErrorOr<AddDeviceModelCommandResponse> readded = await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new AddDeviceModelCommand(manufacturerId, new DeviceModelName(oldName)));

        Assert.False(readded.IsError, readded.IsError ? readded.FirstError.Description : string.Empty);
    }

    [Fact]
    public async Task RenameDeviceModel_ForAnUnknownModel_IsNotFound()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);

        ErrorOr<RenameDeviceModelCommandResponse> renamed = await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new RenameDeviceModelCommand(manufacturerId, Guid.CreateVersion7(), new DeviceModelName(NewName("model"))));

        Assert.Equal(ErrorType.NotFound, renamed.FirstError.Type);
    }

    [Fact]
    public async Task RenameDeviceModel_ForAnUnknownManufacturer_IsNotFound()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId unknown = new(Guid.CreateVersion7());

        ErrorOr<RenameDeviceModelCommandResponse> renamed = await dispatcher.SendAtManufacturerVersionAsync(
            unknown.Value, new RenameDeviceModelCommand(unknown, Guid.CreateVersion7(), new DeviceModelName(NewName("model"))));

        Assert.Equal(ErrorType.NotFound, renamed.FirstError.Type);
    }

    [Fact]
    public async Task RenameDeviceModel_WithoutThePermission_IsForbidden()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = AnonymousDispatcher(scope);

        ErrorOr<RenameDeviceModelCommandResponse> renamed = await dispatcher.SendAsync(new RenameDeviceModelCommand(
            new ManufacturerId(Guid.CreateVersion7()), Guid.CreateVersion7(), new DeviceModelName(NewName("model"))));

        Assert.Equal(ErrorType.Forbidden, renamed.FirstError.Type);
    }
}
```

Append to `DeviceManagementConcurrencyTests.Updates.cs` inside the class:

```csharp
    [Fact]
    public async Task RenameDeviceModel_WithoutAVersion_IsRefusedWith428()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, manufacturerId);

        ErrorOr<RenameDeviceModelCommandResponse> renamed = await dispatcher.SendAsync(
            new RenameDeviceModelCommand(manufacturerId, model.Value, new DeviceModelName($"model-{Guid.CreateVersion7()}")));

        Assert.Equal(VersionErrors.RequiredType, (int)renamed.FirstError.Type);
    }

    [Fact]
    public async Task RenameDeviceModel_WithAStaleVersion_IsRefused()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        ErrorOr<Versioned<ManufacturerQueryResponse>> before =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));
        // Adding the model moves the manufacturer's version, so `before` is now stale.
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, manufacturerId);

        ErrorOr<Versioned<RenameDeviceModelCommandResponse>> renamed = await dispatcher.SendVersionedAsync(
            new RenameDeviceModelCommand(manufacturerId, model.Value, new DeviceModelName($"model-{Guid.CreateVersion7()}")),
            before.Value.Version);

        Assert.True(VersionErrors.IsStale(renamed.FirstError));
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~RenameDeviceModel"`
Expected: FAIL (no handler registered).

- [ ] **Step 4: Implement domain**

In `DeviceModel.cs`, add after `Create`:

```csharp
    // internal: Manufacturer.RenameModel is the only caller; it holds the name-uniqueness invariant.
    internal void Rename(DeviceModelName name) => Name = name;
```

In `Manufacturer.cs`, change `AddModel`'s body to use a shared helper, and add `RenameModel`. The full `AddModel` + new members:

```csharp
    public ErrorOr<DeviceModel> AddModel(DeviceModelName name)
    {
        if (models.Exists(model => model.Name == name))
        {
            return NameInUse();
        }

        var model = DeviceModel.Create(Id, name);
        models.Add(model);

        return model;
    }

    /// <summary>
    /// Renames one of this manufacturer's models. Refuses a name another model of this manufacturer already uses,
    /// with the same error <see cref="AddModel"/> returns; renaming a model to its own current name succeeds.
    /// Requires the manufacturer to have been loaded with its models (<c>GetWithModelsAsync</c>).
    /// </summary>
    public ErrorOr<DeviceModel> RenameModel(DeviceModelInternalId id, DeviceModelName name)
    {
        DeviceModel? model = models.Find(candidate => candidate.Id == id);

        if (model is null)
        {
            return Error.NotFound();
        }

        if (models.Exists(other => other.Id != id && other.Name == name))
        {
            return NameInUse();
        }

        model.Rename(name);

        return model;
    }

    // Error.Validation, so it renders through the path already built for validator errors: Description shown,
    // one snackbar in Blazor, one ValidationProblem entry on the API. The code and text are what MustBeUniqueIn
    // would have produced for a Name property.
    private static Error NameInUse() => Error.Validation(nameof(DeviceModel.Name), "'Name' is already in use.");
```

Keep `AddModel`'s existing XML doc comment above it. Move the two-line `// Error.Validation...` comment that sat inside `AddModel` onto `NameInUse` as shown (delete it from `AddModel`). `models.Exists`/`Find` replace `Any` because `models` is a `List<T>` (CA1860-family analyzers prefer them); if the analyzer is silent either way, keep `Exists`.

- [ ] **Step 5: Implement handler and validator**

`Application/Manufacturers/DeviceModels/Commands/Rename/RenameDeviceModelCommandHandler.cs`:

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Rename;

// The AddDeviceModel shape: the handler owns not-found for the manufacturer, the aggregate owns not-found for
// the model and the name-uniqueness invariant.
internal sealed class RenameDeviceModelCommandHandler(
    IDeviceManagementUnitOfWork unitOfWork,
    IManufacturerRepository repository) : ICommandHandler<RenameDeviceModelCommand, RenameDeviceModelCommandResponse>
{
    public async Task<ErrorOr<RenameDeviceModelCommandResponse>> Handle(
        RenameDeviceModelCommand request, CancellationToken cancellationToken)
    {
        // GetWithModelsAsync, not GetAsync: RenameModel checks the name against the loaded collection.
        Manufacturer? manufacturer = await repository.GetWithModelsAsync(request.ManufacturerId, cancellationToken);

        if (manufacturer is null)
        {
            return Error.NotFound();
        }

        ErrorOr<DeviceModel> renamed = manufacturer.RenameModel(new DeviceModelInternalId(request.Id), request.Name);

        if (renamed.IsError)
        {
            return renamed.Errors;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RenameDeviceModelCommandResponse(renamed.Value.Id.Value);
    }
}
```

`Application/Manufacturers/DeviceModels/Commands/Rename/RenameDeviceModelCommandValidator.cs`:

```csharp
using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Rename;

// Shape only, like AddDeviceModelCommandValidator: not-found is the handler's and the aggregate's, and name
// uniqueness is Manufacturer.RenameModel's.
internal sealed class RenameDeviceModelCommandValidator : AbstractValidator<RenameDeviceModelCommand>
{
    public RenameDeviceModelCommandValidator()
    {
        RuleFor(x => x.ManufacturerId.Value)
            .NotEmpty()
            .OverridePropertyName(nameof(RenameDeviceModelCommand.ManufacturerId));

        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Name.Value)
            .NotEmpty()
            .MaximumLength(DeviceModelName.MaxLength)
            .OverridePropertyName(nameof(RenameDeviceModelCommand.Name));
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet build Sergin.MeterMinder.slnx` — expected: 0 warnings, 0 errors.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~RenameDeviceModel|FullyQualifiedName~DeviceModelTests"`
Expected: all PASS (`DeviceModelTests` guards that `AddModel` still behaves after the helper extraction).

- [ ] **Step 7: Commit**

```bash
git add -A src/Modules/DeviceManagement tests/Sergin.MeterMinder.IntegrationTests.All
git commit -m "RenameDeviceModel: rename a model through its manufacturer

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: RemoveDeviceModel slice

**Files:**
- Modify: `.../Domain/Manufacturers/Manufacturer.cs`
- Modify: `.../Domain/Devices/IDeviceRepository.cs`
- Modify: `.../Infrastructure/Devices/Repositories/DeviceRepository.cs`
- Create: `.../Application.Contracts/Manufacturers/DeviceModels/Commands/Remove/RemoveDeviceModelCommand.cs`
- Create: `.../Application.Contracts/Manufacturers/DeviceModels/Commands/Remove/RemoveDeviceModelCommandResponse.cs`
- Create: `.../Application/Manufacturers/DeviceModels/Commands/Remove/RemoveDeviceModelCommandHandler.cs`
- Create: `.../Application/Manufacturers/DeviceModels/Commands/Remove/RemoveDeviceModelCommandValidator.cs`
- Modify: `.../Application/GlobalUsings.cs`, `tests/.../GlobalUsings.cs`
- Create: `tests/.../Updates/DeviceManagementUpdateTests.RemoveDeviceModel.cs`
- Modify: `tests/.../Concurrency/DeviceManagementConcurrencyTests.Updates.cs`

**Interfaces:**
- Consumes: Task 1 test helpers, including `ReadDeletedAtAsync<TEntity>` and `DeleteDeviceAsync`.
- Produces:
  - `public ErrorOr<Deleted> Manufacturer.RemoveModel(DeviceModelInternalId id)`
  - `Task<bool> IDeviceRepository.AnyUsingModelAsync(DeviceModelInternalId modelId, CancellationToken cancellationToken = default)`
  - `public sealed record RemoveDeviceModelCommand(ManufacturerId ManufacturerId, Guid Id) : ICommand<RemoveDeviceModelCommandResponse>` in `Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove`
  - `public sealed record RemoveDeviceModelCommandResponse(Guid Id)`

- [ ] **Step 1: Add the contract records**

`RemoveDeviceModelCommand.cs`:

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;

// Guarded by the manufacturer's version and permission: removing a model changes the Manufacturer aggregate.
[RequiredPermissions("permission.dm.manufacturers.update")]
[RequiresExpectedVersion]
public sealed record RemoveDeviceModelCommand(ManufacturerId ManufacturerId, Guid Id)
    : ICommand<RemoveDeviceModelCommandResponse>;
```

`RemoveDeviceModelCommandResponse.cs`:

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;

public sealed record RemoveDeviceModelCommandResponse(Guid Id);
```

Add `global using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;` to both `Application/GlobalUsings.cs` and `tests/.../GlobalUsings.cs`, after the `DeviceModels.Commands.Rename` line from Task 3.

- [ ] **Step 2: Write the failing tests**

`tests/.../Updates/DeviceManagementUpdateTests.RemoveDeviceModel.cs`:

```csharp
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Concurrency;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Updates;

public sealed partial class DeviceManagementUpdateTests
{
    [Fact]
    public async Task RemoveDeviceModel_SoftDeletesTheModelOnly_AndMovesTheManufacturersVersion()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, manufacturerId);
        ErrorOr<Versioned<ManufacturerQueryResponse>> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));

        ErrorOr<Versioned<RemoveDeviceModelCommandResponse>> removed = await dispatcher.SendVersionedAsync(
            new RemoveDeviceModelCommand(manufacturerId, model.Value), loaded.Value.Version);

        Assert.False(removed.IsError, removed.IsError ? removed.FirstError.Description : string.Empty);
        Assert.Equal(model.Value, removed.Value.Value.Id);
        Assert.NotEqual(loaded.Value.Version, removed.Value.Version);

        ErrorOr<DeviceModelQueryResponse> read =
            await dispatcher.SendAsync(new GetDeviceModelByIdQueryCommand(manufacturerId.Value, model.Value));
        Assert.Equal(ErrorType.NotFound, read.FirstError.Type);
        Assert.NotNull(await ReadDeletedAtAsync<DeviceModel>(m => m.Id == model));
        Assert.False((await dispatcher.SendAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value))).IsError);
    }

    [Fact]
    public async Task RemoveDeviceModel_UsedByALiveDevice_IsAValidationError()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, manufacturerId);
        await CreateDeviceAsync(dispatcher, model);

        ErrorOr<RemoveDeviceModelCommandResponse> removed = await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new RemoveDeviceModelCommand(manufacturerId, model.Value));

        Error error = Assert.Single(removed.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(RemoveDeviceModelCommand.Id), error.Code);
        Assert.Equal("The model is still used by a device; delete those devices first.", error.Description);
        Assert.False((await dispatcher.SendAsync(new GetDeviceModelByIdQueryCommand(manufacturerId.Value, model.Value))).IsError);
    }

    [Fact]
    public async Task RemoveDeviceModel_UsedOnlyByADeletedDevice_Succeeds()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, manufacturerId);
        await DeleteDeviceAsync(dispatcher, await CreateDeviceAsync(dispatcher, model));

        ErrorOr<RemoveDeviceModelCommandResponse> removed = await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new RemoveDeviceModelCommand(manufacturerId, model.Value));

        Assert.False(removed.IsError, removed.IsError ? removed.FirstError.Description : string.Empty);
    }

    [Fact]
    public async Task RemoveDeviceModel_FreesItsName()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        string name = NewName("model");
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, manufacturerId, name);
        Assert.False((await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new RemoveDeviceModelCommand(manufacturerId, model.Value))).IsError);

        ErrorOr<AddDeviceModelCommandResponse> readded = await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new AddDeviceModelCommand(manufacturerId, new DeviceModelName(name)));

        Assert.False(readded.IsError, readded.IsError ? readded.FirstError.Description : string.Empty);
    }

    [Fact]
    public async Task RemoveDeviceModel_ForAnUnknownModel_IsNotFound()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);

        ErrorOr<RemoveDeviceModelCommandResponse> removed = await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new RemoveDeviceModelCommand(manufacturerId, Guid.CreateVersion7()));

        Assert.Equal(ErrorType.NotFound, removed.FirstError.Type);
    }

    [Fact]
    public async Task RemoveDeviceModel_WithoutThePermission_IsForbidden()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = AnonymousDispatcher(scope);

        ErrorOr<RemoveDeviceModelCommandResponse> removed = await dispatcher.SendAsync(
            new RemoveDeviceModelCommand(new ManufacturerId(Guid.CreateVersion7()), Guid.CreateVersion7()));

        Assert.Equal(ErrorType.Forbidden, removed.FirstError.Type);
    }
}
```

Append to `DeviceManagementConcurrencyTests.Updates.cs` inside the class:

```csharp
    [Fact]
    public async Task RemoveDeviceModel_WithoutAVersion_IsRefusedWith428()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, manufacturerId);

        ErrorOr<RemoveDeviceModelCommandResponse> removed =
            await dispatcher.SendAsync(new RemoveDeviceModelCommand(manufacturerId, model.Value));

        Assert.Equal(VersionErrors.RequiredType, (int)removed.FirstError.Type);
    }

    [Fact]
    public async Task RemoveDeviceModel_WithAStaleVersion_IsRefused_AndTheModelStays()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        ErrorOr<Versioned<ManufacturerQueryResponse>> before =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, manufacturerId);

        ErrorOr<Versioned<RemoveDeviceModelCommandResponse>> removed = await dispatcher.SendVersionedAsync(
            new RemoveDeviceModelCommand(manufacturerId, model.Value), before.Value.Version);

        Assert.True(VersionErrors.IsStale(removed.FirstError));
        Assert.False((await dispatcher.SendAsync(new GetDeviceModelByIdQueryCommand(manufacturerId.Value, model.Value))).IsError);
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~RemoveDeviceModel"`
Expected: FAIL (no handler registered).

- [ ] **Step 4: Implement domain and repository**

In `Manufacturer.cs`, add after `RenameModel`:

```csharp
    /// <summary>
    /// Removes one of this manufacturer's models. The model is dropped from the collection; Manufacturer is
    /// soft-deletable and the model inherits it, so <c>SoftDeleteInterceptor</c> turns the orphaned row into a
    /// soft delete rather than a DELETE. Whether a device still uses the model is not visible from inside this
    /// aggregate; <c>RemoveDeviceModelCommandValidator</c> checks it.
    /// Requires the manufacturer to have been loaded with its models (<c>GetWithModelsAsync</c>).
    /// </summary>
    public ErrorOr<Deleted> RemoveModel(DeviceModelInternalId id)
    {
        DeviceModel? model = models.Find(candidate => candidate.Id == id);

        if (model is null)
        {
            return Error.NotFound();
        }

        models.Remove(model);

        return Result.Deleted;
    }
```

In `IDeviceRepository.cs`, add:

```csharp
    /// <summary>
    /// Whether any live device uses the model — what <c>RemoveDeviceModelCommandValidator</c> refuses a removal
    /// over. Deleted devices are hidden by the soft-delete query filter.
    /// </summary>
    Task<bool> AnyUsingModelAsync(DeviceModelInternalId modelId, CancellationToken cancellationToken = default);
```

In `DeviceRepository.cs`, add after `AnyUsingManufacturerAsync`:

```csharp
    public Task<bool> AnyUsingModelAsync(DeviceModelInternalId modelId, CancellationToken cancellationToken = default)
        => AnyAsync(d => d.DeviceModelId == modelId, cancellationToken);
```

- [ ] **Step 5: Implement handler and validator**

`Application/Manufacturers/DeviceModels/Commands/Remove/RemoveDeviceModelCommandHandler.cs`:

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Remove;

internal sealed class RemoveDeviceModelCommandHandler(
    IDeviceManagementUnitOfWork unitOfWork,
    IManufacturerRepository repository) : ICommandHandler<RemoveDeviceModelCommand, RemoveDeviceModelCommandResponse>
{
    public async Task<ErrorOr<RemoveDeviceModelCommandResponse>> Handle(
        RemoveDeviceModelCommand request, CancellationToken cancellationToken)
    {
        // GetWithModelsAsync, not GetAsync: RemoveModel finds the model in the loaded collection.
        Manufacturer? manufacturer = await repository.GetWithModelsAsync(request.ManufacturerId, cancellationToken);

        if (manufacturer is null)
        {
            return Error.NotFound();
        }

        ErrorOr<Deleted> removed = manufacturer.RemoveModel(new DeviceModelInternalId(request.Id));

        if (removed.IsError)
        {
            return removed.Errors;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RemoveDeviceModelCommandResponse(request.Id);
    }
}
```

`Application/Manufacturers/DeviceModels/Commands/Remove/RemoveDeviceModelCommandValidator.cs`:

```csharp
using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Remove;

// A soft delete fires no foreign key, so dm.device.device_model_id's RESTRICT no longer stops a model in use
// from going: this rule does, as DeleteManufacturerCommandValidator does for a whole manufacturer. Advisory like
// every repository rule — a device created between this query and the save still lands on a deleted model.
internal sealed class RemoveDeviceModelCommandValidator : AbstractValidator<RemoveDeviceModelCommand>
{
    public RemoveDeviceModelCommandValidator(IDeviceRepository devices)
    {
        RuleFor(x => x.ManufacturerId.Value)
            .NotEmpty()
            .OverridePropertyName(nameof(RemoveDeviceModelCommand.ManufacturerId));

        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Id)
            .MustAsync(async (id, cancellationToken) =>
                !await devices.AnyUsingModelAsync(new DeviceModelInternalId(id), cancellationToken))
            .WithMessage("The model is still used by a device; delete those devices first.")
            .When(x => x.Id != Guid.Empty);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet build Sergin.MeterMinder.slnx` — expected: 0 warnings, 0 errors.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~RemoveDeviceModel"` — expected: all PASS.

If `RemoveDeviceModel_SoftDeletesTheModelOnly...` fails with a hard DELETE (the row is gone from `ReadDeletedAtAsync`'s query), read `SoftDeleteInterceptorTests` ("a dropped child is soft-deleted alone") for how the test aggregate's relationship is configured, and compare with `ManufacturerEntityTypeConfiguration`. Do not change the interceptor; report the mismatch.

- [ ] **Step 7: Run the whole update and concurrency suites**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~DeviceManagementUpdateTests|FullyQualifiedName~DeviceManagementConcurrencyTests|FullyQualifiedName~DeviceManagementSoftDeleteTests"`
Expected: all PASS.

- [ ] **Step 8: Commit**

```bash
git add -A src/Modules/DeviceManagement tests/Sergin.MeterMinder.IntegrationTests.All
git commit -m "RemoveDeviceModel: soft-delete an unused model through its manufacturer

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Device read model regains its manufacturer

**Files:**
- Modify: `.../Application.Contracts/Devices/Commands/GetOne/DeviceQueryResponse.cs`
- Modify: `.../Infrastructure/Devices/Repositories/Queries/DeviceQueryRepository.cs` (`GetDeviceById` SQL)
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts/**/devices.proto` (locate with `find src/Modules/DeviceManagement -name devices.proto -not -path '*/obj/*'`)
- Modify: `.../Presentation.Grpc.Server/Devices/DeviceGrpcService.cs`
- Modify: `.../Presentation.Grpc.Client/Devices/GetDeviceByIdGrpcInvoker.cs`
- Modify: `.../Presentation.Blazor/Devices/Pages/DeviceDetailPage.razor`
- Modify: `tests/.../Devices/DeviceReadModelTests.cs`

**Interfaces:**
- Produces: `public sealed record DeviceQueryResponse(Guid Id, string DeviceId, Guid DeviceModelId, string DeviceModelName, Guid ManufacturerId, string ManufacturerName)` — Task 7's `EditDevicePage` reads `ManufacturerId`.

- [ ] **Step 1: Write the failing tests**

In `tests/.../Devices/DeviceReadModelTests.cs`:

1. Rename `GetDeviceById_CarriesDeviceModelName` to `GetDeviceById_CarriesDeviceModelAndManufacturer`, and add after its two `Assert.Equal` lines:

```csharp
        Assert.Equal(manufacturerId.Value, device.Value.ManufacturerId);
        Assert.Equal(manufacturerName, device.Value.ManufacturerName);
```

and change its manufacturer creation to capture the name:

```csharp
        string manufacturerName = $"manufacturer-{Guid.CreateVersion7()}";
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher, manufacturerName);
```

2. Change the helper to take an optional name:

```csharp
    private static async Task<ManufacturerId> CreateManufacturerAsync(ISerginDispatcher dispatcher, string? name = null)
    {
        ErrorOr<CreateManufacturerCommandResponse> created = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName(name ?? $"manufacturer-{Guid.CreateVersion7()}"), Address: null));
```

(rest of the helper unchanged).

3. Replace `DeviceDetailPage_RendersDeviceModelName_NotItsId` with:

```csharp
    [Fact]
    public async Task DeviceDetailPage_LinksTheModelByName()
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
        Assert.Contains($"href=\"/dm/manufacturers/{manufacturerId.Value}/models/{modelId.Value}\"", html, StringComparison.Ordinal);
        Assert.Contains($">{modelName.Value}</a>", html, StringComparison.Ordinal);
    }
```

4. In the class `<summary>`, replace `The manufacturer is deliberately absent from these read models (spec, Decision 4).` with `The detail read model also carries the manufacturer, so the detail page can link the model (dm-full-crud spec, Decision 7); the list item stays model-only.` and replace `The last test renders the detail page itself` with `The last test renders the detail page itself and finds the model link`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: FAIL — `CS1061 'DeviceQueryResponse' does not contain a definition for 'ManufacturerId'`.

- [ ] **Step 3: Implement**

`DeviceQueryResponse.cs`:

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOne;

public sealed record DeviceQueryResponse(
    Guid Id, string DeviceId, Guid DeviceModelId, string DeviceModelName, Guid ManufacturerId, string ManufacturerName);
```

In `DeviceQueryRepository.GetDeviceById`, the SQL becomes:

```sql
SELECT d.id, d.device_id AS deviceId, d.device_model_id AS deviceModelId, dm_.name AS deviceModelName,
       m_.id AS manufacturerId, m_.name AS manufacturerName,
       d.row_version AS rowVersion
FROM dm.device d
JOIN dm.device_model dm_ ON dm_.id = d.device_model_id
JOIN dm.manufacturer m_ ON m_.id = dm_.manufacturer_id
WHERE d.id = @Id AND d.deleted_at_utc IS NULL;
```

(keep it inside the existing `"""` literal; `rowVersion` stays last for `splitOn`). `dm.manufacturer` is the table `ManufacturerQueryRepository` already reads.

In `devices.proto`, `DeviceData` gains:

```proto
  string manufacturer_id = 5;
  string manufacturer_name = 6;
```

In `DeviceGrpcService.cs`, the `DeviceData` initializer gains:

```csharp
                    ManufacturerId = response.ManufacturerId.ToString(),
                    ManufacturerName = response.ManufacturerName,
```

In `GetDeviceByIdGrpcInvoker.cs`, the constructor call becomes:

```csharp
            : new DeviceQueryResponse(
                Guid.Parse(reply.Success.Id),
                reply.Success.DeviceId,
                Guid.Parse(reply.Success.DeviceModelId),
                reply.Success.DeviceModelName,
                Guid.Parse(reply.Success.ManufacturerId),
                reply.Success.ManufacturerName);
```

In `DeviceDetailPage.razor`, replace the comment and model line:

```razor
            @* Text, not a link: the model's page is nested under its manufacturer, and this read model
               deliberately carries no manufacturer id (spec, Decision 4). *@
            <MudText Typo="Typo.body2">Model: @device.DeviceModelName</MudText>
```

with:

```razor
            <MudText Typo="Typo.body2">
                Model:
                <MudLink Href="@($"/dm/manufacturers/{device.ManufacturerId}/models/{device.DeviceModelId}")">@device.DeviceModelName</MudLink>
            </MudText>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Sergin.MeterMinder.slnx` — expected: 0 warnings, 0 errors.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~DeviceReadModelTests|FullyQualifiedName~DeviceGrpcRoundTripTests|FullyQualifiedName~DeviceManagementConcurrencyTests"`
Expected: all PASS. `DeviceGrpcRoundTripTests` compares the local and remote records with `Assert.Equal`, so it now covers the two new fields.

If the `MudLink` renders its `href` differently from the asserted `href="..."` (e.g. extra attributes before it), keep the assertion on the exact `href="/dm/manufacturers/.../models/..."` substring — attribute order does not matter for a substring match.

- [ ] **Step 5: Commit**

```bash
git add -A src/Modules/DeviceManagement tests/Sergin.MeterMinder.IntegrationTests.All
git commit -m "Device read model carries its manufacturer; detail page links the model

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: WebApi endpoints

**Files:**
- Create: `.../Presentation.WebApi/Devices/Endpoints/Update/UpdateDeviceEndpoint.cs`
- Create: `.../Presentation.WebApi/Devices/Endpoints/Update/UpdateDeviceModel.cs`
- Create: `.../Presentation.WebApi/Manufacturers/Endpoints/Update/UpdateManufacturerEndpoint.cs`
- Create: `.../Presentation.WebApi/Manufacturers/Endpoints/Update/UpdateManufacturerModel.cs`
- Create: `.../Presentation.WebApi/Manufacturers/DeviceModels/Endpoints/Rename/RenameDeviceModelEndpoint.cs`
- Create: `.../Presentation.WebApi/Manufacturers/DeviceModels/Endpoints/Rename/RenameDeviceModelModel.cs`
- Create: `.../Presentation.WebApi/Manufacturers/DeviceModels/Endpoints/Remove/RemoveDeviceModelEndpoint.cs`
- Modify: `.../Presentation.WebApi/GlobalUsings.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement/Devices/DeviceInstallationExtensions.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement/Manufacturers/ManufacturerInstallationExtensions.cs`

**Interfaces:**
- Consumes: the four command records from Tasks 1–4.
- Produces: `PUT /devices/{deviceId:guid}`, `PUT /manufacturers/{manufacturerId:guid}`, `PUT /manufacturers/{manufacturerId:guid}/models/{modelId:guid}`, `DELETE /manufacturers/{manufacturerId:guid}/models/{modelId:guid}`. `If-Match`/`ETag` come from the group's `ExpectedVersionEndpointFilter` (already applied by `UseSerginWebApiAsync`); nothing per endpoint.

There is no API host, so these endpoints have no integration test (the filter itself is covered by `ExpectedVersionEndpointFilterTests`). Verification is the build.

- [ ] **Step 1: Add the global usings**

Append to `.../Presentation.WebApi/GlobalUsings.cs`:

```csharp
global using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Update;
global using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Update;
global using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;
global using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Rename;
```

- [ ] **Step 2: Write the endpoints and request DTOs**

`Devices/Endpoints/Update/UpdateDeviceModel.cs`:

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.Update;

// Like NewDeviceModel, "model of an updated device", not "device model".
public record UpdateDeviceModel(string DeviceId, Guid DeviceModelId);
```

`Devices/Endpoints/Update/UpdateDeviceEndpoint.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.Update;

internal class UpdateDeviceEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPut("/devices/{deviceId:guid}", async (
            [FromRoute] Guid deviceId, [FromBody] UpdateDeviceModel device, ISender sender) =>
        {
            ErrorOr<UpdateDeviceCommandResponse> res = await sender.Send(new UpdateDeviceCommand(
                deviceId, new DeviceId(device.DeviceId), new DeviceModelInternalId(device.DeviceModelId)));

            return res.ToApiResult();
        });
    }
}
```

`Manufacturers/Endpoints/Update/UpdateManufacturerModel.cs`:

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.Update;

public record UpdateManufacturerModel(string Name, string? Address);
```

`Manufacturers/Endpoints/Update/UpdateManufacturerEndpoint.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.Update;

internal class UpdateManufacturerEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPut("/manufacturers/{manufacturerId:guid}", async (
            [FromRoute] Guid manufacturerId, [FromBody] UpdateManufacturerModel manufacturer, ISender sender) =>
        {
            ErrorOr<UpdateManufacturerCommandResponse> res = await sender.Send(new UpdateManufacturerCommand(
                manufacturerId,
                new ManufacturerName(manufacturer.Name),
                manufacturer.Address is null ? null : new ManufacturerAddress(manufacturer.Address)));

            return res.ToApiResult();
        });
    }
}
```

`Manufacturers/DeviceModels/Endpoints/Rename/RenameDeviceModelModel.cs`:

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.DeviceModels.Endpoints.Rename;

public record RenameDeviceModelModel(string Name);
```

`Manufacturers/DeviceModels/Endpoints/Rename/RenameDeviceModelEndpoint.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.DeviceModels.Endpoints.Rename;

internal class RenameDeviceModelEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPut("/manufacturers/{manufacturerId:guid}/models/{modelId:guid}", async (
            [FromRoute] Guid manufacturerId, [FromRoute] Guid modelId, [FromBody] RenameDeviceModelModel deviceModel, ISender sender) =>
        {
            ErrorOr<RenameDeviceModelCommandResponse> res = await sender.Send(new RenameDeviceModelCommand(
                new ManufacturerId(manufacturerId), modelId, new DeviceModelName(deviceModel.Name)));

            return res.ToApiResult();
        });
    }
}
```

`Manufacturers/DeviceModels/Endpoints/Remove/RemoveDeviceModelEndpoint.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.DeviceModels.Endpoints.Remove;

internal class RemoveDeviceModelEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapDelete("/manufacturers/{manufacturerId:guid}/models/{modelId:guid}", async (
            [FromRoute] Guid manufacturerId, [FromRoute] Guid modelId, ISender sender) =>
        {
            ErrorOr<RemoveDeviceModelCommandResponse> res =
                await sender.Send(new RemoveDeviceModelCommand(new ManufacturerId(manufacturerId), modelId));

            return res.ToApiResult();
        });
    }
}
```

- [ ] **Step 3: Map them**

In `DeviceInstallationExtensions.cs`, add `using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.Update;` (keep usings sorted) and in `MapDeviceEndpoints` after `new DeleteDeviceEndpoint()...`:

```csharp
        new UpdateDeviceEndpoint().MapEndpoint(routeBuilder);
```

In `ManufacturerInstallationExtensions.cs`, add usings for `...WebApi.Manufacturers.Endpoints.Update`, `...WebApi.Manufacturers.DeviceModels.Endpoints.Rename`, `...WebApi.Manufacturers.DeviceModels.Endpoints.Remove`, and in `MapManufacturerEndpoints`:
- after `new DeleteManufacturerEndpoint()...`: `new UpdateManufacturerEndpoint().MapEndpoint(routeBuilder);`
- after `new GetDeviceModelListEndpoint()...`: `new RenameDeviceModelEndpoint().MapEndpoint(routeBuilder);` then `new RemoveDeviceModelEndpoint().MapEndpoint(routeBuilder);`

- [ ] **Step 4: Build**

Run: `dotnet build Sergin.MeterMinder.slnx` — expected: 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add -A src/Modules/DeviceManagement
git commit -m "WebApi: PUT device, manufacturer and model; DELETE model

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: EditDevicePage

**Files:**
- Create: `.../Presentation.Blazor/Devices/Models/EditDeviceFormModel.cs`
- Create: `.../Presentation.Blazor/Devices/Pages/EditDevicePage.razor`
- Create: `.../Presentation.Blazor/Devices/Pages/EditDevicePage.razor.cs`
- Modify: `.../Presentation.Blazor/Devices/Pages/DeviceDetailPage.razor` (Edit button)
- Modify: `.../Presentation.Blazor/GlobalUsings.cs`, `.../Presentation.Blazor/_Imports.razor`
- Modify: `tests/.../Shell/BreadcrumbRenderingTests.cs`, `tests/.../Shell/ModulePageRenderingTests.cs`

**Interfaces:**
- Consumes: `UpdateDeviceCommand` (Task 1), `DeviceQueryResponse.ManufacturerId` (Task 5), `ISerginDispatcher.SendVersionedAsync<TResponse>(IRequest<ErrorOr<TResponse>>, RowVersion? expected = null, CancellationToken = default) → Task<ErrorOr<Versioned<TResponse>>>`, `ISerginFormValidator.RulesFor`, `VersionErrors.IsStale(Error)`.
- Produces: route `/dm/devices/{Id:guid}/edit`; breadcrumb trail `Devices › <device id or "Device"> (links /dm/devices/{Id}) › Edit`.

- [ ] **Step 1: Add the usings**

Append to `.../Presentation.Blazor/GlobalUsings.cs`:

```csharp
global using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Update;
global using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Update;
global using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;
global using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Rename;
```

Append the same four namespaces to `_Imports.razor` as `@using` lines (after the `DeviceModels.Commands.GetList` line). Tasks 8 and 9 rely on these.

- [ ] **Step 2: Write the failing rendering tests**

In `tests/.../Shell/ModulePageRenderingTests.cs`, add to the `Page_RendersServerSide_WithNavFromBothModules` theory:

```csharp
    [InlineData("/dm/devices/01920000-0000-7000-8000-000000000001/edit")]
```

In `tests/.../Shell/BreadcrumbRenderingTests.cs`, add (Tasks 8 and 9 add `InlineData` rows to this theory):

```csharp
    /// <summary>
    /// An edit page for a record that does not exist still renders: the problem panel instead of the form, and a
    /// trail whose record step keeps its placeholder but still links to the detail page by the route id.
    /// </summary>
    [Theory]
    [InlineData("/dm/devices/" + UnseededId + "/edit", "/dm/devices", "Device", "Edit")]
    public async Task EditPage_ForAnUnknownRecord_LinksItsListAndRecord_AndNamesItselfLast(
        string path, string listHref, string recordPlaceholder, string title)
    {
        string strip = await GetBreadcrumbStripAsync(path);
        string recordHref = path[..path.LastIndexOf('/')];

        Assert.Contains($"href=\"{listHref}\"", strip, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"{recordHref}\">{recordPlaceholder}</a>", strip, StringComparison.Ordinal);
        Assert.Contains(Current(title), strip, StringComparison.Ordinal);
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~EditPage_ForAnUnknownRecord|FullyQualifiedName~Page_RendersServerSide"`
Expected: FAIL — `/dm/devices/.../edit` returns 404.

- [ ] **Step 4: Write the form model and page**

`Devices/Models/EditDeviceFormModel.cs`:

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Models;

/// <summary>
/// The binding target for the edit form. No DataAnnotations: the fields are validated by the pipeline's own
/// <c>UpdateDeviceCommandValidator</c> through <c>ISerginFormValidator</c>.
/// </summary>
public sealed class EditDeviceFormModel
{
    public string DeviceId { get; set; } = string.Empty;

    // The manufacturer is page state on EditDevicePage, not part of what is submitted.
    public Guid DeviceModelId { get; set; }
}
```

`Devices/Pages/EditDevicePage.razor`:

```razor
@page "/dm/devices/{Id:guid}/edit"

<PageTitle>Edit device</PageTitle>

<SerginBreadcrumbs Trail="Trail" />

<SerginProblemPanel Problem="problem" />

@if (version is not null)
{
    <MudText Typo="Typo.h4" Class="mb-4">Edit device</MudText>

    <MudForm @ref="form" Model="model" Validation="@validation" @bind-IsValid="isValid"
             SuppressImplicitSubmission="false" onsubmit="@OnSubmit">
        <MudCard>
            <MudCardContent>
                <MudTextField @bind-Value="model.DeviceId" Label="Device ID" For="@(() => model.DeviceId)" Immediate="true" />

                @* Page state, not on the command: For= only gives the form-level validation a member path, which finds no rule. *@
                <MudSelect T="Guid" Value="selectedManufacturerId" ValueChanged="OnManufacturerChangedAsync" Label="Manufacturer"
                           For="@(() => selectedManufacturerId)">
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
                           Color="Color.Primary" Disabled="@(submitting || !isValid)">Save</MudButton>
                <MudButton Href="@($"/dm/devices/{Id}")">Cancel</MudButton>
            </MudCardActions>
        </MudCard>
    </MudForm>
}
```

`Devices/Pages/EditDevicePage.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Models;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Pages;

public sealed partial class EditDevicePage
{
    private readonly EditDeviceFormModel model = new();

    private IReadOnlyCollection<GetManufacturerListItem> manufacturers = [];
    private IReadOnlyCollection<GetDeviceModelListItem> deviceModels = [];

    // Page state only: the manufacturer narrows the model picker and is not part of the command.
    private Guid selectedManufacturerId;

    // The device id as loaded, for the trail: model.DeviceId changes as the user types.
    private string? loadedDeviceId;
    private RowVersion? version;
    private SerginProblem? problem;

    private MudForm form = default!;
    private bool isValid;
    private bool submitting;
    private Func<object, string, Task<IEnumerable<string>>> validation = default!;

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private ISerginFormValidator FormValidator { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    // An EventCallback, not a bare delegate: splatted onto MudForm's <form> it gets a receiver, so the
    // page re-renders after SubmitAsync.
    private EventCallback OnSubmit => EventCallback.Factory.Create(this, SubmitAsync);

    // A property, not a field: the record step reads the loaded device id. It links by the route id, so it is a
    // link on the not-found path too.
    private IReadOnlyList<SerginBreadcrumb> Trail =>
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Devices),
        new(loadedDeviceId ?? "Device", $"/dm/devices/{Id}"),
        new("Edit"),
    ];

    protected override void OnInitialized() => validation = FormValidator.RulesFor(ToCommand);

    protected override Task OnParametersSetAsync() => LoadAsync();

    // Fills the form from the stored device and keeps the version it was read at. Called again after a stale
    // save, so the next submit is against what is there now.
    private async Task LoadAsync()
    {
        ErrorOr<Versioned<DeviceQueryResponse>> loaded = await Dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(Id));

        if (loaded.IsError)
        {
            version = null;
            loadedDeviceId = null;
            problem = ErrorPresenter.Present(loaded.FirstError);

            return;
        }

        problem = null;
        DeviceQueryResponse device = loaded.Value.Value;
        version = loaded.Value.Version;
        loadedDeviceId = device.DeviceId;
        model.DeviceId = device.DeviceId;

        await LoadManufacturersAsync();
        await LoadModelsAsync(device.ManufacturerId);
        model.DeviceModelId = device.DeviceModelId;
    }

    private async Task LoadManufacturersAsync()
    {
        // One page of 200, as on CreateDevicePage: there is no server-side search to fall back on.
        ErrorOr<ListQueryResponse<GetManufacturerListItem>> result =
            await Dispatcher.SendAsync(new GetManufacturerListQueryCommand(Paggination.Create(200, 1)));

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return;
        }

        manufacturers = result.Value.Data;
    }

    private async Task OnManufacturerChangedAsync(Guid manufacturerId)
    {
        model.DeviceModelId = Guid.Empty;
        await LoadModelsAsync(manufacturerId);
    }

    private async Task LoadModelsAsync(Guid manufacturerId)
    {
        selectedManufacturerId = manufacturerId;
        deviceModels = [];

        if (manufacturerId == Guid.Empty)
        {
            return;
        }

        // Same 200-row caveat as the manufacturer picker above.
        ErrorOr<ListQueryResponse<GetDeviceModelListItem>> result = await Dispatcher.SendAsync(
            new GetDeviceModelListQueryCommand(new ManufacturerId(manufacturerId), Paggination.Create(200, 1)));

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return;
        }

        deviceModels = result.Value.Data;
    }

    // One mapping for both the field-by-field validation and the submit, so the two cannot drift. The id is in
    // it so the uniqueness rule can leave this device out.
    private UpdateDeviceCommand ToCommand() =>
        new(Id, new DeviceId(model.DeviceId), new DeviceModelInternalId(model.DeviceModelId));

    private async Task SubmitAsync()
    {
        await form.ValidateAsync();

        if (!form.IsValid || version is not { } expectedVersion)
        {
            return;
        }

        submitting = true;

        ErrorOr<Versioned<UpdateDeviceCommandResponse>> result = await Dispatcher.SendVersionedAsync(ToCommand(), expectedVersion);

        submitting = false;

        if (result.IsError)
        {
            // Every error, not the first: validation yields one per broken rule.
            ErrorPresenter.Notify(result.Errors);

            // Someone changed the device after this page loaded it: show what is there now, with its version.
            if (result.Errors.Exists(VersionErrors.IsStale))
            {
                await LoadAsync();
            }

            return;
        }

        Navigation.NavigateTo($"/dm/devices/{Id}");
    }
}
```

In `DeviceDetailPage.razor`, inside `<MudCardActions>`, before the Delete button:

```razor
            <MudButton Variant="Variant.Outlined" Color="Color.Primary" Href="@($"/dm/devices/{Id}/edit")">Edit</MudButton>
```

If `Sergin.SharedKernel.Domain` (for `RowVersion`) or `MudBlazor` turns out unused in the code-behind, remove that `using` — IDE0005 fails the build.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet build Sergin.MeterMinder.slnx` — expected: 0 warnings, 0 errors.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~BreadcrumbRenderingTests|FullyQualifiedName~ModulePageRenderingTests"`
Expected: all PASS (the route guard accepts `/dm/devices/{Id:guid}/edit` because it starts with `/dm/`).

- [ ] **Step 6: Commit**

```bash
git add -A src/Modules/DeviceManagement tests/Sergin.MeterMinder.IntegrationTests.All
git commit -m "EditDevicePage: edit a device's id and model from its detail page

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: EditManufacturerPage

**Files:**
- Create: `.../Presentation.Blazor/Manufacturers/Models/EditManufacturerFormModel.cs`
- Create: `.../Presentation.Blazor/Manufacturers/Pages/EditManufacturerPage.razor`
- Create: `.../Presentation.Blazor/Manufacturers/Pages/EditManufacturerPage.razor.cs`
- Modify: `.../Presentation.Blazor/Manufacturers/Pages/ManufacturerDetailPage.razor` (Edit button)
- Modify: `tests/.../Shell/BreadcrumbRenderingTests.cs`, `tests/.../Shell/ModulePageRenderingTests.cs`

**Interfaces:**
- Consumes: `UpdateManufacturerCommand` (Task 2), the Blazor usings from Task 7 Step 1, the `EditPage_ForAnUnknownRecord_...` theory from Task 7.
- Produces: route `/dm/manufacturers/{Id:guid}/edit`; trail `Manufacturers › <name or "Manufacturer"> (links /dm/manufacturers/{Id}) › Edit`.

- [ ] **Step 1: Write the failing rendering tests**

Add to `ModulePageRenderingTests.Page_RendersServerSide_WithNavFromBothModules`:

```csharp
    [InlineData("/dm/manufacturers/01920000-0000-7000-8000-000000000001/edit")]
```

Add to `BreadcrumbRenderingTests.EditPage_ForAnUnknownRecord_LinksItsListAndRecord_AndNamesItselfLast`:

```csharp
    [InlineData("/dm/manufacturers/" + UnseededId + "/edit", "/dm/manufacturers", "Manufacturer", "Edit")]
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~EditPage_ForAnUnknownRecord|FullyQualifiedName~Page_RendersServerSide"`
Expected: FAIL — the manufacturer edit route returns 404.

- [ ] **Step 3: Write the form model and page**

`Manufacturers/Models/EditManufacturerFormModel.cs`:

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Models;

/// <summary>
/// The binding target for the edit form. No DataAnnotations: the fields are validated by the pipeline's own
/// <c>UpdateManufacturerCommandValidator</c> through <c>ISerginFormValidator</c>.
/// </summary>
public sealed class EditManufacturerFormModel
{
    public string Name { get; set; } = string.Empty;

    // Optional: a blank field is "no address", which the page maps to null before dispatching.
    public string? Address { get; set; }
}
```

`Manufacturers/Pages/EditManufacturerPage.razor`:

```razor
@page "/dm/manufacturers/{Id:guid}/edit"

<PageTitle>Edit manufacturer</PageTitle>

<SerginBreadcrumbs Trail="Trail" />

<SerginProblemPanel Problem="problem" />

@if (version is not null)
{
    <MudText Typo="Typo.h4" Class="mb-4">Edit manufacturer</MudText>

    <MudForm @ref="form" Model="model" Validation="@validation" @bind-IsValid="isValid"
             SuppressImplicitSubmission="false" onsubmit="@OnSubmit">
        <MudCard>
            <MudCardContent>
                <MudTextField @bind-Value="model.Name" Label="Name" For="@(() => model.Name)" Immediate="true" />
                <MudTextField @bind-Value="model.Address" Label="Address" For="@(() => model.Address)" Immediate="true" />
            </MudCardContent>
            <MudCardActions>
                <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled"
                           Color="Color.Primary" Disabled="@(submitting || !isValid)">Save</MudButton>
                <MudButton Href="@($"/dm/manufacturers/{Id}")">Cancel</MudButton>
            </MudCardActions>
        </MudCard>
    </MudForm>
}
```

`Manufacturers/Pages/EditManufacturerPage.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Models;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

public sealed partial class EditManufacturerPage
{
    private readonly EditManufacturerFormModel model = new();

    // The name as loaded, for the trail: model.Name changes as the user types.
    private string? loadedName;
    private RowVersion? version;
    private SerginProblem? problem;

    private MudForm form = default!;
    private bool isValid;
    private bool submitting;
    private Func<object, string, Task<IEnumerable<string>>> validation = default!;

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private ISerginFormValidator FormValidator { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    // An EventCallback, not a bare delegate: splatted onto MudForm's <form> it gets a receiver, so the
    // page re-renders after SubmitAsync.
    private EventCallback OnSubmit => EventCallback.Factory.Create(this, SubmitAsync);

    private IReadOnlyList<SerginBreadcrumb> Trail =>
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Manufacturers),
        new(loadedName ?? "Manufacturer", $"/dm/manufacturers/{Id}"),
        new("Edit"),
    ];

    protected override void OnInitialized() => validation = FormValidator.RulesFor(ToCommand);

    protected override Task OnParametersSetAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        ErrorOr<Versioned<ManufacturerQueryResponse>> loaded =
            await Dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(Id));

        if (loaded.IsError)
        {
            version = null;
            loadedName = null;
            problem = ErrorPresenter.Present(loaded.FirstError);

            return;
        }

        problem = null;
        version = loaded.Value.Version;
        loadedName = loaded.Value.Value.Name;
        model.Name = loaded.Value.Value.Name;
        model.Address = loaded.Value.Value.Address;
    }

    // One mapping for both the field-by-field validation and the submit, so the two cannot drift.
    // A blank address means "no address": the validator refuses an empty non-null Address.
    private UpdateManufacturerCommand ToCommand() => new(
        Id,
        new ManufacturerName(model.Name),
        string.IsNullOrWhiteSpace(model.Address) ? null : new ManufacturerAddress(model.Address));

    private async Task SubmitAsync()
    {
        await form.ValidateAsync();

        if (!form.IsValid || version is not { } expectedVersion)
        {
            return;
        }

        submitting = true;

        ErrorOr<Versioned<UpdateManufacturerCommandResponse>> result =
            await Dispatcher.SendVersionedAsync(ToCommand(), expectedVersion);

        submitting = false;

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.Errors);

            // Someone changed the manufacturer (added a model, say) after this page loaded it.
            if (result.Errors.Exists(VersionErrors.IsStale))
            {
                await LoadAsync();
            }

            return;
        }

        Navigation.NavigateTo($"/dm/manufacturers/{Id}");
    }
}
```

In `ManufacturerDetailPage.razor`, inside `<MudCardActions>`, before the Delete button:

```razor
            <MudButton Variant="Variant.Outlined" Color="Color.Primary" Href="@($"/dm/manufacturers/{Id}/edit")">Edit</MudButton>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Sergin.MeterMinder.slnx` — expected: 0 warnings, 0 errors.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~BreadcrumbRenderingTests|FullyQualifiedName~ModulePageRenderingTests"` — expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A src/Modules/DeviceManagement tests/Sergin.MeterMinder.IntegrationTests.All
git commit -m "EditManufacturerPage: edit a manufacturer's name and address

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: RenameDeviceModelPage and Remove button

**Files:**
- Create: `.../Presentation.Blazor/Manufacturers/DeviceModels/Models/RenameDeviceModelFormModel.cs`
- Create: `.../Presentation.Blazor/Manufacturers/DeviceModels/Pages/RenameDeviceModelPage.razor`
- Create: `.../Presentation.Blazor/Manufacturers/DeviceModels/Pages/RenameDeviceModelPage.razor.cs`
- Modify: `.../Presentation.Blazor/Manufacturers/DeviceModels/Pages/DeviceModelDetailPage.razor`
- Modify: `.../Presentation.Blazor/Manufacturers/DeviceModels/Pages/DeviceModelDetailPage.razor.cs`
- Modify: `tests/.../Shell/BreadcrumbRenderingTests.cs`, `tests/.../Shell/ModulePageRenderingTests.cs`

**Interfaces:**
- Consumes: `RenameDeviceModelCommand` (Task 3), `RemoveDeviceModelCommand` (Task 4), `GetDeviceModelByIdQueryCommand(Guid ManufacturerId, Guid Id) → DeviceModelQueryResponse(Guid Id, Guid ManufacturerId, string ManufacturerName, string Name)` (unversioned), `GetManufacturerByIdQueryCommand` (versioned — the model's changes are guarded by the manufacturer's version).
- Produces: route `/dm/manufacturers/{ManufacturerId:guid}/models/{Id:guid}/edit`; trail `Manufacturers › <manufacturer> › <model> › Rename`.

- [ ] **Step 1: Write the failing tests**

Add to `ModulePageRenderingTests.Page_RendersServerSide_WithNavFromBothModules`:

```csharp
    [InlineData("/dm/manufacturers/01920000-0000-7000-8000-000000000001/models/01920000-0000-7000-8000-000000000002/edit")]
```

Add to `BreadcrumbRenderingTests` (a separate fact — this trail has two placeholder steps):

```csharp
    [Fact]
    public async Task RenameModelPage_ForAnUnknownModel_LinksManufacturerAndModel_AndNamesItselfLast()
    {
        const string ModelId = "01920000-0000-7000-8000-000000000002";
        string strip = await GetBreadcrumbStripAsync($"/dm/manufacturers/{UnseededId}/models/{ModelId}/edit");

        Assert.Contains("href=\"/dm/manufacturers\"", strip, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"/dm/manufacturers/{UnseededId}\">Manufacturer</a>", strip, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"/dm/manufacturers/{UnseededId}/models/{ModelId}\">Device model</a>", strip, StringComparison.Ordinal);
        Assert.Contains(Current("Rename"), strip, StringComparison.Ordinal);
    }
```

Add to `tests/.../Updates/DeviceManagementUpdateTests.RenameDeviceModel.cs` (inside the class; add `using System.Net;` to that file):

```csharp
    [Fact]
    public async Task DeviceModelDetailPage_OffersRenameAndRemove()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelInternalId model = await AddDeviceModelAsync(dispatcher, manufacturerId);

        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(
            new Uri($"/dm/manufacturers/{manufacturerId.Value}/models/{model.Value}", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"href=\"/dm/manufacturers/{manufacturerId.Value}/models/{model.Value}/edit\"", html, StringComparison.Ordinal);
        Assert.Contains(">Remove<", html, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~RenameModelPage|FullyQualifiedName~Page_RendersServerSide|FullyQualifiedName~DeviceModelDetailPage_OffersRenameAndRemove"`
Expected: FAIL — 404 on the edit route; no Rename/Remove on the detail page.

- [ ] **Step 3: Write the rename form model and page**

`Manufacturers/DeviceModels/Models/RenameDeviceModelFormModel.cs`:

```csharp
namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Models;

/// <summary>
/// The binding target for the rename form. No DataAnnotations: the field is validated by the pipeline's own
/// <c>RenameDeviceModelCommandValidator</c> through <c>ISerginFormValidator</c>.
/// </summary>
public sealed class RenameDeviceModelFormModel
{
    public string Name { get; set; } = string.Empty;
}
```

`Manufacturers/DeviceModels/Pages/RenameDeviceModelPage.razor`:

```razor
@page "/dm/manufacturers/{ManufacturerId:guid}/models/{Id:guid}/edit"

<PageTitle>Rename device model</PageTitle>

<SerginBreadcrumbs Trail="Trail" />

<SerginProblemPanel Problem="problem" />

@if (manufacturerVersion is not null)
{
    <MudText Typo="Typo.h4" Class="mb-4">Rename device model</MudText>

    <MudForm @ref="form" Model="model" Validation="@validation" @bind-IsValid="isValid"
             SuppressImplicitSubmission="false" onsubmit="@OnSubmit">
        <MudCard>
            <MudCardContent>
                <MudTextField @bind-Value="model.Name" Label="Name" For="@(() => model.Name)" Immediate="true" />
            </MudCardContent>
            <MudCardActions>
                <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled"
                           Color="Color.Primary" Disabled="@(submitting || !isValid)">Save</MudButton>
                <MudButton Href="@ModelHref">Cancel</MudButton>
            </MudCardActions>
        </MudCard>
    </MudForm>
}
```

`Manufacturers/DeviceModels/Pages/RenameDeviceModelPage.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Models;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Pages;

public sealed partial class RenameDeviceModelPage
{
    private readonly RenameDeviceModelFormModel model = new();

    private string? manufacturerName;
    private string? loadedModelName;

    // The model has no version of its own: a rename changes the Manufacturer aggregate, so it is guarded by the
    // manufacturer's version.
    private RowVersion? manufacturerVersion;
    private SerginProblem? problem;

    private MudForm form = default!;
    private bool isValid;
    private bool submitting;
    private Func<object, string, Task<IEnumerable<string>>> validation = default!;

    [Parameter]
    public Guid ManufacturerId { get; set; }

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private ISerginFormValidator FormValidator { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private string ModelHref => $"/dm/manufacturers/{ManufacturerId}/models/{Id}";

    // An EventCallback, not a bare delegate: splatted onto MudForm's <form> it gets a receiver, so the
    // page re-renders after SubmitAsync.
    private EventCallback OnSubmit => EventCallback.Factory.Create(this, SubmitAsync);

    private IReadOnlyList<SerginBreadcrumb> Trail =>
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Manufacturers),
        new(manufacturerName ?? "Manufacturer", $"/dm/manufacturers/{ManufacturerId}"),
        new(loadedModelName ?? "Device model", ModelHref),
        new("Rename"),
    ];

    protected override void OnInitialized() => validation = FormValidator.RulesFor(ToCommand);

    protected override Task OnParametersSetAsync() => LoadAsync();

    // Two reads: the model for its current name, the manufacturer for the version the rename is guarded by.
    private async Task LoadAsync()
    {
        ErrorOr<DeviceModelQueryResponse> loadedModel =
            await Dispatcher.SendAsync(new GetDeviceModelByIdQueryCommand(ManufacturerId, Id));
        ErrorOr<Versioned<ManufacturerQueryResponse>> loadedManufacturer =
            await Dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(ManufacturerId));

        if (loadedModel.IsError || loadedManufacturer.IsError)
        {
            manufacturerVersion = null;
            loadedModelName = null;
            manufacturerName = null;
            problem = ErrorPresenter.Present(loadedModel.IsError ? loadedModel.FirstError : loadedManufacturer.FirstError);

            return;
        }

        problem = null;
        manufacturerVersion = loadedManufacturer.Value.Version;
        manufacturerName = loadedModel.Value.ManufacturerName;
        loadedModelName = loadedModel.Value.Name;
        model.Name = loadedModel.Value.Name;
    }

    // One mapping for both the field-by-field validation and the submit, so the two cannot drift.
    private RenameDeviceModelCommand ToCommand() =>
        new(new ManufacturerId(ManufacturerId), Id, new DeviceModelName(model.Name));

    private async Task SubmitAsync()
    {
        await form.ValidateAsync();

        if (!form.IsValid || manufacturerVersion is not { } expectedVersion)
        {
            return;
        }

        submitting = true;

        ErrorOr<Versioned<RenameDeviceModelCommandResponse>> result =
            await Dispatcher.SendVersionedAsync(ToCommand(), expectedVersion);

        submitting = false;

        if (result.IsError)
        {
            // A duplicate name arrives as one validation error from the aggregate, a stale manufacturer as a
            // version error.
            ErrorPresenter.Notify(result.Errors);

            if (result.Errors.Exists(VersionErrors.IsStale))
            {
                await LoadAsync();
            }

            return;
        }

        Navigation.NavigateTo(ModelHref);
    }
}
```

- [ ] **Step 4: Add Rename and Remove to the model detail page**

`DeviceModelDetailPage.razor` — replace the `<MudCard>` block with:

```razor
    <MudCard>
        <MudCardContent>
            <MudText Typo="Typo.h5">@deviceModel.Name</MudText>
            <MudText Typo="Typo.body2">
                Manufacturer:
                <MudLink Href="@($"/dm/manufacturers/{deviceModel.ManufacturerId}")">@deviceModel.ManufacturerName</MudLink>
            </MudText>
            <MudText Typo="Typo.body2">@deviceModel.Id</MudText>
        </MudCardContent>
        <MudCardActions>
            <MudButton Variant="Variant.Outlined" Color="Color.Primary"
                       Href="@($"/dm/manufacturers/{ManufacturerId}/models/{Id}/edit")">Rename</MudButton>
            <MudButton Variant="Variant.Outlined" Color="Color.Error" Disabled="removing" OnClick="RemoveAsync">Remove</MudButton>
        </MudCardActions>
    </MudCard>
```

`DeviceModelDetailPage.razor.cs` — full new content:

```csharp
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Pages;

public sealed partial class DeviceModelDetailPage
{
    private DeviceModelQueryResponse? deviceModel;
    private SerginProblem? problem;
    private bool removing;

    // The manufacturer's version: removing a model changes the Manufacturer aggregate, which is what is guarded.
    private RowVersion? manufacturerVersion;

    [Parameter]
    public Guid ManufacturerId { get; set; }

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private IDialogService DialogService { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    // A property, not a field: both tail steps are the page title's words until the load fills them in, and
    // on the not-found path they stay that way beside the problem panel. The manufacturer step links by the
    // route parameter, not the read model's id, so it is a link before the load too.
    private IReadOnlyList<SerginBreadcrumb> Trail =>
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Manufacturers),
        new(deviceModel?.ManufacturerName ?? "Manufacturer", $"/dm/manufacturers/{ManufacturerId}"),
        new(deviceModel?.Name ?? "Device model"),
    ];

    protected override Task OnParametersSetAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        ErrorOr<DeviceModelQueryResponse> result =
            await Dispatcher.SendAsync(new GetDeviceModelByIdQueryCommand(ManufacturerId, Id));

        if (result.IsError)
        {
            deviceModel = null;
            manufacturerVersion = null;
            problem = ErrorPresenter.Present(result.FirstError);

            return;
        }

        problem = null;
        deviceModel = result.Value;

        ErrorOr<Versioned<ManufacturerQueryResponse>> manufacturer =
            await Dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(ManufacturerId));

        // A failed read leaves no version; RemoveAsync then reports it instead of sending a fabricated one.
        manufacturerVersion = manufacturer.IsError ? null : manufacturer.Value.Version;
    }

    private async Task RemoveAsync()
    {
        bool? confirmed = await DialogService.ShowMessageBoxAsync(
            "Remove device model",
            $"Remove {deviceModel?.Name}? It disappears from its manufacturer and every list.",
            yesText: "Remove",
            cancelText: "Cancel");

        if (confirmed != true)
        {
            return;
        }

        if (manufacturerVersion is not { } expectedVersion)
        {
            ErrorPresenter.Notify(Error.NotFound());
            await LoadAsync();

            return;
        }

        removing = true;

        ErrorOr<Versioned<RemoveDeviceModelCommandResponse>> result = await Dispatcher.SendVersionedAsync(
            new RemoveDeviceModelCommand(new ManufacturerId(ManufacturerId), Id), expectedVersion);

        removing = false;

        if (result.IsError)
        {
            // An in-use model arrives as one validation error; a stale manufacturer as a version error.
            ErrorPresenter.Notify(result.Errors);

            if (result.Errors.Exists(VersionErrors.IsStale))
            {
                await LoadAsync();
            }

            return;
        }

        Navigation.NavigateTo($"/dm/manufacturers/{ManufacturerId}");
    }
}
```

`IUiErrorPresenter` has both `Notify(Error)` and `Notify(IReadOnlyList<Error>)`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet build Sergin.MeterMinder.slnx` — expected: 0 warnings, 0 errors.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~BreadcrumbRenderingTests|FullyQualifiedName~ModulePageRenderingTests|FullyQualifiedName~DeviceManagementUpdateTests"`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add -A src/Modules/DeviceManagement tests/Sergin.MeterMinder.IntegrationTests.All
git commit -m "RenameDeviceModelPage and a Remove button on the model detail page

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Docs, full suite, graph

**Files:**
- Modify: `.claude/CLAUDE.md`
- Modify: `src/Modules/DeviceManagement/CLAUDE.md`

- [ ] **Step 1: Update the root CLAUDE.md**

In the "Optimistic concurrency" bullet, replace:

```
guarded: `DeleteDevice`, `DeleteManufacturer`, `AddDeviceModel`.
```

with:

```
guarded: `DeleteDevice`, `DeleteManufacturer`, `AddDeviceModel`, `UpdateDevice`, `UpdateManufacturer`, `RenameDeviceModel`, `RemoveDeviceModel`.
```

In "The Blazor UI host" bullet about the DevUser host, replace:

```
grants `permission.dm.devices.read`, `permission.dm.devices.delete`, `permission.dm.manufacturers.read`, `permission.dm.manufacturers.delete` and `permission.ua.users.read`
```

with:

```
grants `permission.dm.devices.read`, `permission.dm.devices.update`, `permission.dm.devices.delete`, `permission.dm.manufacturers.read`, `permission.dm.manufacturers.update`, `permission.dm.manufacturers.delete` and `permission.ua.users.read`
```

In the "Validation" bullet, replace `Six validators today:` sentence's list end `` `AddDeviceModel` (shape only — not-found is the handler's and name uniqueness is `Manufacturer.AddModel`'s).`` with:

```
`AddDeviceModel` (shape only — not-found is the handler's and name uniqueness is `Manufacturer.AddModel`'s), `UpdateDevice` (shape + a self-excluding `IsTakenByOtherAsync` rule + the model rule), `UpdateManufacturer`, `RenameDeviceModel` (shape only — uniqueness is `Manufacturer.RenameModel`'s), `RemoveDeviceModel` (refuses a model a live device uses).
```

and change `Six validators today` to `Ten validators today`.

In the "Blazor UI conventions" MudForm bullet, replace `An update page copies exactly this shape with an `Update<X>Command` and a `ToCommand()` that includes the id.` with:

```
An update page copies exactly this shape with an `Update<X>Command` and a `ToCommand()` that includes the id; it loads the record through `SendVersionedAsync`, renders the form only once a version is in hand, and reloads on a stale answer — `EditDevicePage`, `EditManufacturerPage` and `RenameDeviceModelPage` are the references.
```

- [ ] **Step 2: Update the module CLAUDE.md**

In `src/Modules/DeviceManagement/CLAUDE.md`, in the versioning paragraph (line starting "`Device` and `Manufacturer` are also **versioned**"), replace `` `DeleteDeviceCommand`, `DeleteManufacturerCommand` and `AddDeviceModelCommand` carry `[RequiresExpectedVersion]` `` with:

```
`DeleteDeviceCommand`, `DeleteManufacturerCommand`, `AddDeviceModelCommand`, `UpdateDeviceCommand`, `UpdateManufacturerCommand`, `RenameDeviceModelCommand` and `RemoveDeviceModelCommand` carry `[RequiresExpectedVersion]`
```

and replace `` `DeviceQueryResponse`/`ManufacturerQueryResponse` stay version-free `` with `` `DeviceQueryResponse`/`ManufacturerQueryResponse` stay version-free; `DeviceQueryResponse` also carries `ManufacturerId`/`ManufacturerName` (joined through the model) so `EditDevicePage` can preselect its manufacturer and the detail page can link the model ``.

Add a new paragraph after the soft-delete paragraph:

```
**Updates.** `UpdateDevice` (`permission.dm.devices.update`; `PUT /devices/{id}`; `EditDevicePage` at `/dm/devices/{Id}/edit`) changes both `DeviceId` and `DeviceModelId`; its validator checks uniqueness with `IDeviceRepository.IsTakenByOtherAsync`, which leaves the device itself out — `MustBeUniqueIn` cannot. `UpdateManufacturer` (`permission.dm.manufacturers.update`; `PUT /manufacturers/{id}`; `EditManufacturerPage`) changes name and address. Model changes are manufacturer changes, under the same permission and the manufacturer's version: `RenameDeviceModel` (`PUT /manufacturers/{id}/models/{modelId}`; `RenameDeviceModelPage` at `.../models/{Id}/edit`) goes through `Manufacturer.RenameModel`, which shares `AddModel`'s duplicate-name error; `RemoveDeviceModel` (`DELETE /manufacturers/{id}/models/{modelId}`; a Remove button on the model detail page) drops the model from `Manufacturer.Models`, which `SoftDeleteInterceptor` turns into a soft delete, and `RemoveDeviceModelCommandValidator` refuses while a live device uses the model (`IDeviceRepository.AnyUsingModelAsync`). Tests: `tests/.../Updates/`. Design: `docs/superpowers/specs/2026-10-01-dm-full-crud-design.md`.
```

- [ ] **Step 3: Run the full suite**

Run: `dotnet build Sergin.MeterMinder.slnx` — expected: 0 warnings, 0 errors.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`
Expected: all PASS. Report the pass count.

- [ ] **Step 4: Refresh the graph (only if `graphify-out/graph.json` exists in the worktree)**

```bash
graphify update . && python .claude/skills/graphify/scripts/graphify_repair.py
```

The graph is gitignored; nothing to commit from this step.

- [ ] **Step 5: Commit**

```bash
git add .claude/CLAUDE.md src/Modules/DeviceManagement/CLAUDE.md
git commit -m "Docs: DeviceManagement update slices, permissions and edit pages

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
