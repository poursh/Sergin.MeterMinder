using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Dispatching;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.GetList;
internal class GetDeviceListEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder
            .MapGet("/devices", async ([AsParameters]ListQueryRequestModel request, ISerginSender sender) =>
            {
                ErrorOr<ListQueryResponse<GetDeviceListItem>> res = await sender.SendAsync(
                    request.ToListQuery<GetDeviceListItem>());

                return res.ToApiResult();
            })
            .Produces<ListQueryResponse<GetDeviceListItem>>();
    }
}
