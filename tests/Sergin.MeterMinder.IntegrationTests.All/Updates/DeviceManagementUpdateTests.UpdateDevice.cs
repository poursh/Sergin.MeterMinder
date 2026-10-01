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
        var unknown = Guid.CreateVersion7();

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
        var id = Guid.CreateVersion7();

        ErrorOr<UpdateDeviceCommandResponse> updated = await dispatcher.SendAsync(
            new UpdateDeviceCommand(id, new DeviceId(NewName("device")), new DeviceModelInternalId(Guid.CreateVersion7())));

        Assert.Equal(ErrorType.Forbidden, updated.FirstError.Type);
    }
}
