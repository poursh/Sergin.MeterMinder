using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Add;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>
/// DeviceManagement's versioned slices through ISerginDispatcher, the way a Blazor page sends them: a GetOne
/// hands back the aggregate's version beside its read model, and a guarded write is accepted at the current
/// version and refused at any other.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed partial class DeviceManagementConcurrencyTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task GetManufacturerById_ReturnsItsVersion_AndTheSameOneTwice()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);

        VersionedResult<ManufacturerQueryResponse> first =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));
        VersionedResult<ManufacturerQueryResponse> second =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));

        Assert.False(first.Result.IsError);
        Assert.NotNull(first.Version);
        Assert.Equal(first.Version, second.Version);
    }

    [Fact]
    public async Task GetDeviceById_ReturnsItsVersion()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid deviceId = await CreateDeviceAsync(dispatcher);

        VersionedResult<DeviceQueryResponse> loaded = await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(deviceId));

        Assert.False(loaded.Result.IsError);
        Assert.NotNull(loaded.Version);
    }

    [Fact]
    public async Task GetManufacturerById_ForAMissingRecord_HasNoVersion()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        VersionedResult<ManufacturerQueryResponse> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(Guid.CreateVersion7()));

        Assert.Equal(ErrorType.NotFound, loaded.Result.FirstError.Type);
        Assert.Null(loaded.Version);
    }

    private static async Task<ManufacturerId> CreateManufacturerAsync(ISerginDispatcher dispatcher)
    {
        ErrorOr<CreateManufacturerCommandResponse> created = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName($"maker-{Guid.CreateVersion7()}"), Address: null));
        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return new ManufacturerId(created.Value.Id);
    }

    private static async Task<DeviceModelInternalId> AddDeviceModelAsync(ISerginDispatcher dispatcher, ManufacturerId manufacturerId)
    {
        VersionedResult<ManufacturerQueryResponse> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));
        VersionedResult<AddDeviceModelCommandResponse> added = await dispatcher.SendVersionedAsync(
            new AddDeviceModelCommand(manufacturerId, new DeviceModelName($"model-{Guid.CreateVersion7()}")),
            loaded.Version);
        Assert.False(added.Result.IsError, added.Result.IsError ? added.Result.FirstError.Description : string.Empty);

        return new DeviceModelInternalId(added.Result.Value.Id);
    }

    private static async Task<Guid> CreateDeviceAsync(ISerginDispatcher dispatcher)
    {
        DeviceModelInternalId modelId = await AddDeviceModelAsync(dispatcher, await CreateManufacturerAsync(dispatcher));
        ErrorOr<CreateDeviceCommandResponse> created = await dispatcher.SendAsync(
            new CreateDeviceCommand(new DeviceId($"device-{Guid.CreateVersion7()}"), modelId));
        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return created.Value.Id;
    }
}
