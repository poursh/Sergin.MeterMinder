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
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId));

        ErrorOr<Versioned<RemoveDeviceModelCommandResponse>> removed = await dispatcher.SendVersionedAsync(
            new RemoveDeviceModelCommand(manufacturerId, model), loaded.Value.Version);

        Assert.False(removed.IsError, removed.IsError ? removed.FirstError.Description : string.Empty);
        Assert.Equal(model.Value, removed.Value.Value.Id);
        Assert.NotEqual(loaded.Value.Version, removed.Value.Version);

        ErrorOr<DeviceModelQueryResponse> read =
            await dispatcher.SendAsync(new GetDeviceModelByIdQueryCommand(manufacturerId, model));
        Assert.Equal(ErrorType.NotFound, read.FirstError.Type);
        Assert.NotNull(await ReadDeletedAtAsync<DeviceModel>(m => m.Id == model));
        Assert.False((await dispatcher.SendAsync(new GetManufacturerByIdQueryCommand(manufacturerId))).IsError);
    }

    // DeviceModelListView's bulk Remove: the child command carries the root's version, so each send must carry the
    // version the one before it returned; the first version is stale by the second send.
    [Fact]
    public async Task RemoveDeviceModel_InARun_ChainsTheManufacturersVersion()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelInternalId first = await AddDeviceModelAsync(dispatcher, manufacturerId);
        DeviceModelInternalId second = await AddDeviceModelAsync(dispatcher, manufacturerId);
        ErrorOr<Versioned<ManufacturerQueryResponse>> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId));

        ErrorOr<Versioned<RemoveDeviceModelCommandResponse>> removedFirst = await dispatcher.SendVersionedAsync(
            new RemoveDeviceModelCommand(manufacturerId, first), loaded.Value.Version);
        ErrorOr<Versioned<RemoveDeviceModelCommandResponse>> secondAtStale = await dispatcher.SendVersionedAsync(
            new RemoveDeviceModelCommand(manufacturerId, second), loaded.Value.Version);
        ErrorOr<Versioned<RemoveDeviceModelCommandResponse>> removedSecond = await dispatcher.SendVersionedAsync(
            new RemoveDeviceModelCommand(manufacturerId, second), removedFirst.Value.Version);

        Assert.False(removedFirst.IsError, removedFirst.IsError ? removedFirst.FirstError.Description : string.Empty);
        Assert.True(secondAtStale.Errors.Exists(VersionErrors.IsStale));
        Assert.False(removedSecond.IsError, removedSecond.IsError ? removedSecond.FirstError.Description : string.Empty);
        Assert.NotEqual(removedFirst.Value.Version, removedSecond.Value.Version);
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
            manufacturerId.Value, new RemoveDeviceModelCommand(manufacturerId, model));

        Error error = Assert.Single(removed.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(RemoveDeviceModelCommand.Id), error.Code);
        Assert.Equal("The model is still used by a device; delete those devices first.", error.Description);
        Assert.False((await dispatcher.SendAsync(new GetDeviceModelByIdQueryCommand(manufacturerId, model))).IsError);
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
            manufacturerId.Value, new RemoveDeviceModelCommand(manufacturerId, model));

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
            manufacturerId.Value, new RemoveDeviceModelCommand(manufacturerId, model))).IsError);

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
            manufacturerId.Value, new RemoveDeviceModelCommand(manufacturerId, new DeviceModelInternalId(Guid.CreateVersion7())));

        Assert.Equal(ErrorType.NotFound, removed.FirstError.Type);
    }

    [Fact]
    public async Task RemoveDeviceModel_WithoutThePermission_IsForbidden()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = AnonymousDispatcher(scope);

        ErrorOr<RemoveDeviceModelCommandResponse> removed = await dispatcher.SendAsync(
            new RemoveDeviceModelCommand(new ManufacturerId(Guid.CreateVersion7()), new DeviceModelInternalId(Guid.CreateVersion7())));

        Assert.Equal(ErrorType.Forbidden, removed.FirstError.Type);
    }
}
