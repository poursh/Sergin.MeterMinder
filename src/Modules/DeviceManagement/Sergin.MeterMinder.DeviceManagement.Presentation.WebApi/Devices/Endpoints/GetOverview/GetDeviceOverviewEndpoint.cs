using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOverview;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.GetOverview;

internal class GetDeviceOverviewEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/devices/overview", async (ISender sender) =>
        {
            ErrorOr<DeviceOverview> res = await sender.Send(new GetDeviceOverviewQueryCommand());

            return res.ToApiResult();
        });
    }
}
