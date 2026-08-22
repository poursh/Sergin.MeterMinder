using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.SharedKernel.Application.Dispatching;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.GetOne;

internal class GetDeviceEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/devices/{deviceId:guid}", async ([FromRoute] Guid deviceId, ISerginSender sender) =>
        {
            ErrorOr<DeviceQueryResponse> res = await sender.SendAsync(new GetDeviceByIdQueryCommand(deviceId));

            return res.ToApiResult();
        });
    }
}
