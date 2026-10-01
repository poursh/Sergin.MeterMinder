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
