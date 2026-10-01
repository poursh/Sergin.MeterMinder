using System.Linq.Expressions;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.MeterMinder.IntegrationTests.All.Concurrency;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.SoftDelete;

/// <summary>
/// The two delete slices through ISerginDispatcher, as the detail pages send them: a deleted device leaves
/// every read and frees its device id while its row stays, stamped with the dispatcher's user; a manufacturer
/// whose models a live device uses is refused as a validation error; one without takes its models with it;
/// and a caller without the permission is refused.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceManagementSoftDeleteTests(SerginWebApiFactory<Program> factory)
{
    private static readonly Guid DevUserId = Guid.Parse("01920000-0000-7000-8000-000000000001");

    private static readonly string[] FilterName = [SoftDeleteColumns.QueryFilterName];

    [Fact]
    public async Task DeleteDevice_HidesIt_FreesItsId_AndKeepsTheStampedRow()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        DeviceModelInternalId modelId = await AddDeviceModelAsync(dispatcher, await CreateManufacturerAsync(dispatcher));
        DeviceId deviceId = new($"device-{Guid.CreateVersion7()}");
        Guid id = await CreateDeviceAsync(dispatcher, deviceId, modelId);

        ErrorOr<DeleteDeviceCommandResponse> deleted = await dispatcher.SendAtDeviceVersionAsync(id, new DeleteDeviceCommand(id));
        Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Description : string.Empty);

        ErrorOr<DeviceQueryResponse> detail = await dispatcher.SendAsync(new GetDeviceByIdQueryCommand(id));
        Assert.Equal(ErrorType.NotFound, detail.FirstError.Type);

        ErrorOr<DeleteDeviceCommandResponse> again = await dispatcher.SendAtDeviceVersionAsync(id, new DeleteDeviceCommand(id));
        Assert.Equal(ErrorType.NotFound, again.FirstError.Type);

        Guid replacement = await CreateDeviceAsync(dispatcher, deviceId, modelId);
        Assert.NotEqual(id, replacement);

        DeletedStamp stamp = await ReadDeletedAsync<Device>(device => device.Id == new DeviceIntenralId(id));
        Assert.NotNull(stamp.DeletedAtUtc);
        Assert.Equal(DevUserId, stamp.DeletedBy);
    }

    [Fact]
    public async Task DeletedDevice_IsNotCountedInTheList()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        DeviceModelInternalId modelId = await AddDeviceModelAsync(dispatcher, await CreateManufacturerAsync(dispatcher));
        Guid id = await CreateDeviceAsync(dispatcher, new DeviceId($"device-{Guid.CreateVersion7()}"), modelId);

        int before = await CountDevicesAsync(dispatcher);
        Assert.False((await dispatcher.SendAtDeviceVersionAsync(id, new DeleteDeviceCommand(id))).IsError);
        int after = await CountDevicesAsync(dispatcher);

        Assert.Equal(before - 1, after);
    }

    [Fact]
    public async Task DeleteManufacturer_WhoseModelIsInUse_IsAValidationError()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        await CreateDeviceAsync(
            dispatcher, new DeviceId($"device-{Guid.CreateVersion7()}"), await AddDeviceModelAsync(dispatcher, manufacturerId));

        ErrorOr<DeleteManufacturerCommandResponse> deleted =
            await dispatcher.SendAtManufacturerVersionAsync(manufacturerId.Value, new DeleteManufacturerCommand(manufacturerId.Value));

        Error error = Assert.Single(deleted.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(DeleteManufacturerCommand.Id), error.Code);

        Assert.False((await dispatcher.SendAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value))).IsError);
    }

    [Fact]
    public async Task DeleteManufacturer_TakesItsModelsWithIt()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelInternalId modelId = await AddDeviceModelAsync(dispatcher, manufacturerId);

        // A deleted device no longer holds its model's manufacturer.
        Guid deviceId = await CreateDeviceAsync(dispatcher, new DeviceId($"device-{Guid.CreateVersion7()}"), modelId);
        Assert.False((await dispatcher.SendAtDeviceVersionAsync(deviceId, new DeleteDeviceCommand(deviceId))).IsError);

        ErrorOr<DeleteManufacturerCommandResponse> deleted =
            await dispatcher.SendAtManufacturerVersionAsync(manufacturerId.Value, new DeleteManufacturerCommand(manufacturerId.Value));
        Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Description : string.Empty);

        ErrorOr<ManufacturerQueryResponse> detail =
            await dispatcher.SendAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));
        Assert.Equal(ErrorType.NotFound, detail.FirstError.Type);

        ErrorOr<ListQueryResponse<GetDeviceModelListItem>> models = await dispatcher.SendAsync(
            new GetDeviceModelListQueryCommand(manufacturerId, Paggination.Create(10, 1)));
        Assert.False(models.IsError);
        Assert.Empty(models.Value.Data);

        DeletedStamp manufacturer = await ReadDeletedAsync<Manufacturer>(m => m.Id == manufacturerId);
        DeletedStamp model = await ReadDeletedAsync<DeviceModel>(m => m.Id == modelId);
        Assert.NotNull(manufacturer.DeletedAtUtc);
        Assert.Equal(manufacturer.DeletedAtUtc, model.DeletedAtUtc);
        Assert.Equal(DevUserId, model.DeletedBy);

        // The deleted model is no longer a valid reference for a new device.
        ErrorOr<CreateDeviceCommandResponse> onDeletedModel = await dispatcher.SendAsync(
            new CreateDeviceCommand(new DeviceId($"device-{Guid.CreateVersion7()}"), modelId));
        Assert.Contains(onDeletedModel.Errors, error => error.Code == nameof(CreateDeviceCommand.DeviceModelId));
    }

    [Fact]
    public async Task Delete_WithoutThePermission_IsForbidden()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = ClaimsPrincipalUserContext.Create(null);
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<DeleteDeviceCommandResponse> device = await dispatcher.SendAsync(new DeleteDeviceCommand(Guid.CreateVersion7()));
        ErrorOr<DeleteManufacturerCommandResponse> manufacturer =
            await dispatcher.SendAsync(new DeleteManufacturerCommand(Guid.CreateVersion7()));

        Assert.Equal(ErrorType.Forbidden, device.FirstError.Type);
        Assert.Equal(ErrorType.Forbidden, manufacturer.FirstError.Type);
    }

    private async Task<DeletedStamp> ReadDeletedAsync<TEntity>(Expression<Func<TEntity, bool>> match)
        where TEntity : class
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IDeviceManagementDbContext context = scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>();

        return await context.Set<TEntity>()
            .IgnoreQueryFilters(FilterName)
            .Where(match)
            .Select(entity => new DeletedStamp(
                EF.Property<DateTime?>(entity, SoftDeleteColumns.DeletedAtUtc),
                EF.Property<Guid?>(entity, SoftDeleteColumns.DeletedBy)))
            .SingleAsync();
    }

    private static async Task<int> CountDevicesAsync(ISerginDispatcher dispatcher)
    {
        ErrorOr<ListQueryResponse<GetDeviceListItem>> list =
            await dispatcher.SendAsync(new GetDeviceListQueryCommand(Paggination.Create(1, 1)));

        Assert.False(list.IsError);
        return list.Value.Total;
    }

    private static async Task<ManufacturerId> CreateManufacturerAsync(ISerginDispatcher dispatcher)
    {
        ErrorOr<CreateManufacturerCommandResponse> created = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName($"manufacturer-{Guid.CreateVersion7()}"), Address: null));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);
        return new ManufacturerId(created.Value.Id);
    }

    private static async Task<DeviceModelInternalId> AddDeviceModelAsync(ISerginDispatcher dispatcher, ManufacturerId manufacturerId)
    {
        ErrorOr<AddDeviceModelCommandResponse> added = await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new AddDeviceModelCommand(manufacturerId, new DeviceModelName($"model-{Guid.CreateVersion7()}")));

        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);
        return new DeviceModelInternalId(added.Value.Id);
    }

    private static async Task<Guid> CreateDeviceAsync(ISerginDispatcher dispatcher, DeviceId deviceId, DeviceModelInternalId modelId)
    {
        ErrorOr<CreateDeviceCommandResponse> created = await dispatcher.SendAsync(new CreateDeviceCommand(deviceId, modelId));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);
        return created.Value.Id;
    }

    private sealed record DeletedStamp(DateTime? DeletedAtUtc, Guid? DeletedBy);
}
