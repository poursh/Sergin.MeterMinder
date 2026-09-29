using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Add;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

public sealed partial class DeviceManagementConcurrencyTests
{
    [Fact]
    public async Task AddDeviceModel_WithoutAVersion_IsRefusedWith428()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);

        ErrorOr<AddDeviceModelCommandResponse> added = await dispatcher.SendAsync(
            new AddDeviceModelCommand(manufacturerId, new DeviceModelName($"model-{Guid.CreateVersion7()}")));

        Assert.Equal(VersionErrors.RequiredType, (int)added.FirstError.Type);
    }

    [Fact]
    public async Task AddDeviceModel_AtTheCurrentVersion_ReturnsANewOne_AndTheOldOneIsThenStale()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);

        VersionedResult<ManufacturerQueryResponse> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));

        VersionedResult<AddDeviceModelCommandResponse> first = await dispatcher.SendVersionedAsync(
            new AddDeviceModelCommand(manufacturerId, new DeviceModelName($"model-{Guid.CreateVersion7()}")), loaded.Version);
        VersionedResult<AddDeviceModelCommandResponse> replay = await dispatcher.SendVersionedAsync(
            new AddDeviceModelCommand(manufacturerId, new DeviceModelName($"model-{Guid.CreateVersion7()}")), loaded.Version);

        Assert.False(first.Result.IsError, first.Result.IsError ? first.Result.FirstError.Description : string.Empty);
        Assert.NotNull(first.Version);
        Assert.NotEqual(loaded.Version, first.Version);
        Assert.True(VersionErrors.IsStale(replay.Result.FirstError));
        Assert.Equal(1, await CountModelsAsync(dispatcher, manufacturerId));
    }

    [Fact]
    public async Task AddDeviceModel_WithAnotherManufacturersVersion_IsStale()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId a = await CreateManufacturerAsync(dispatcher);
        ManufacturerId b = await CreateManufacturerAsync(dispatcher);

        VersionedResult<ManufacturerQueryResponse> loadedA =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(a.Value));

        VersionedResult<AddDeviceModelCommandResponse> added = await dispatcher.SendVersionedAsync(
            new AddDeviceModelCommand(b, new DeviceModelName($"model-{Guid.CreateVersion7()}")), loadedA.Version);

        Assert.True(VersionErrors.IsStale(added.Result.FirstError));
    }

    [Fact]
    public async Task DeleteDevice_AtAStaleVersion_IsRefused_AndTheDeviceStaysLive()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid deviceId = await CreateDeviceAsync(dispatcher);

        VersionedResult<DeleteDeviceCommandResponse> deleted =
            await dispatcher.SendVersionedAsync(new DeleteDeviceCommand(deviceId), Sergin.SharedKernel.Domain.RowVersion.Create());

        Assert.True(VersionErrors.IsStale(deleted.Result.FirstError));
        Assert.False((await dispatcher.SendAsync(new GetDeviceByIdQueryCommand(deviceId))).IsError);
    }

    [Fact]
    public async Task DeleteDevice_AlreadyDeleted_IsNotFound()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid deviceId = await CreateDeviceAsync(dispatcher);

        VersionedResult<DeviceQueryResponse> loaded = await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(deviceId));
        Assert.False((await dispatcher.SendVersionedAsync(new DeleteDeviceCommand(deviceId), loaded.Version)).Result.IsError);

        VersionedResult<DeleteDeviceCommandResponse> again =
            await dispatcher.SendVersionedAsync(new DeleteDeviceCommand(deviceId), loaded.Version);

        Assert.Equal(ErrorType.NotFound, again.Result.FirstError.Type);
    }

    [Fact]
    public async Task DeleteManufacturer_AfterAModelWasAdded_IsStale()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);

        VersionedResult<ManufacturerQueryResponse> pageLoad =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));
        await AddDeviceModelAsync(dispatcher, manufacturerId);

        VersionedResult<DeleteManufacturerCommandResponse> deleted =
            await dispatcher.SendVersionedAsync(new DeleteManufacturerCommand(manufacturerId.Value), pageLoad.Version);

        Assert.True(VersionErrors.IsStale(deleted.Result.FirstError));
        Assert.False((await dispatcher.SendAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value))).IsError);
    }

    private static async Task<int> CountModelsAsync(ISerginDispatcher dispatcher, ManufacturerId manufacturerId)
    {
        ErrorOr<ListQueryResponse<GetDeviceModelListItem>> models = await dispatcher.SendAsync(
            new GetDeviceModelListQueryCommand(manufacturerId, Paggination.Create(1000, 1)));
        Assert.False(models.IsError);

        return models.Value.Total;
    }
}
