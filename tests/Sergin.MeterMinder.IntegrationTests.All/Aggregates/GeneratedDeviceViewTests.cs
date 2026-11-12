using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Lists;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>What the generated Device detail view sends, checked at the dispatcher (a component cannot be driven server-side).</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class GeneratedDeviceViewTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task DeviceDetail_StaleDelete_ReloadsAndKeepsDevice()
    {
        AggregateViewModel model = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Device>();
        CommandView delete = Assert.Single(model.Commands, command => command.Request == typeof(DeleteDeviceCommand));
        Assert.True(delete.RequiresVersion);

        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId deviceModel = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        Guid device = await CatalogSeed.DeviceAsync(dispatcher, $"dev-{marker}", deviceModel);

        ErrorOr<Versioned<DeleteDeviceCommandResponse>> stale =
            await dispatcher.SendVersionedAsync(new DeleteDeviceCommand(new DeviceIntenralId(device)), RowVersion.Create());

        Assert.True(stale.IsError);
        Assert.Equal(VersionErrors.Stale.Code, stale.FirstError.Code);
        Assert.False((await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(new DeviceIntenralId(device)))).IsError);
    }
}
