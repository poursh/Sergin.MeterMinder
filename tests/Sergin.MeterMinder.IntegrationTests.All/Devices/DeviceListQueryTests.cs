using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Devices;

/// <summary>
/// List features carry their own request record rather than dispatching the shared generic
/// <c>ListQuery&lt;TItem&gt;</c>, which is what makes them attributable with
/// <c>[RequiredPermissions]</c>. These cover the two things that shift with it: that the concrete record
/// still routes to its handler through MediatR, and that PermissionCheckPipelineBehavior passes for the
/// permissions the host grants its configured development user.
/// </summary>
/// <remarks>
/// The dispatcher is resolved from a scope rather than the root provider because it is scoped — it
/// carries the caller's IUserContext into the root-provider scope it opens per send. A Blazor page
/// always resolves it from its circuit's scope, so this is also the more faithful simulation.
/// </remarks>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceListQueryTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task GetDeviceList_IsDispatchedAndPermitted()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<ListQueryResponse<GetDeviceListItem>> result =
            await dispatcher.SendAsync(new GetDeviceListQueryCommand(Paggination.Create(10, 1)));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);
    }

    /// <summary>
    /// CreateDevicePage fills its manufacturer picker through this query, so the grant it needs
    /// (permission.dm.manufacturers.read) has to be present in the host's Sergin:DevUser:Permissions —
    /// this is the test that fails first if it is dropped.
    /// </summary>
    [Fact]
    public async Task GetManufacturerList_IsDispatchedAndPermitted()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<ListQueryResponse<GetManufacturerListItem>> result =
            await dispatcher.SendAsync(new GetManufacturerListQueryCommand(Paggination.Create(10, 1)));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);
    }
}
