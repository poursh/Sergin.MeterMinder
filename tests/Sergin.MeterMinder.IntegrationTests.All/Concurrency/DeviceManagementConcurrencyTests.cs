using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;
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

        ErrorOr<Versioned<ManufacturerQueryResponse>> first =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));
        ErrorOr<Versioned<ManufacturerQueryResponse>> second =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));

        Assert.False(first.IsError);
        Assert.Equal(first.Value.Version, second.Value.Version);
    }

    [Fact]
    public async Task GetDeviceById_ReturnsItsVersion()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid deviceId = await CreateDeviceAsync(dispatcher);

        ErrorOr<Versioned<DeviceQueryResponse>> loaded = await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(deviceId));

        Assert.False(loaded.IsError);
    }

    [Fact]
    public async Task GetManufacturerById_ForAMissingRecord_IsNotFound()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<Versioned<ManufacturerQueryResponse>> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(Guid.CreateVersion7()));

        Assert.Equal(ErrorType.NotFound, loaded.FirstError.Type);
    }

    // A list reads no single aggregate, so it publishes no version: asking for one is a caller bug.
    [Fact]
    public async Task SendVersioned_ForARequestThatPublishesNoVersion_IsNotPublished()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<Versioned<ListQueryResponse<GetManufacturerListItem>>> listed =
            await dispatcher.SendVersionedAsync(new GetManufacturerListQueryCommand(Paggination.Create(10, 1)));

        Assert.Equal(VersionErrors.NotPublished, listed.FirstError);
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
        ErrorOr<Versioned<ManufacturerQueryResponse>> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));
        ErrorOr<Versioned<AddDeviceModelCommandResponse>> added = await dispatcher.SendVersionedAsync(
            new AddDeviceModelCommand(manufacturerId, new DeviceModelName($"model-{Guid.CreateVersion7()}")),
            loaded.Value.Version);
        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);

        return new DeviceModelInternalId(added.Value.Value.Id);
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
