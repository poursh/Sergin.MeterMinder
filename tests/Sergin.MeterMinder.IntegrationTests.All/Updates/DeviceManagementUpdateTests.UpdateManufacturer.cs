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
        var unknown = Guid.CreateVersion7();

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
