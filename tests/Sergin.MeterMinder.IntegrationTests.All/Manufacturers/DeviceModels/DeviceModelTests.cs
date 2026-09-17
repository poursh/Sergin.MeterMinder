using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Add;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Manufacturers.DeviceModels;

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
