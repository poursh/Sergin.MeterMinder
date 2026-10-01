using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.Delete;

internal class DeleteDeviceEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapDelete("/devices/{deviceId:guid}", async ([FromRoute] Guid deviceId, ISender sender) =>
        {
            ErrorOr<DeleteDeviceCommandResponse> res = await sender.Send(new DeleteDeviceCommand(deviceId));

            return res.ToApiResult();
        });
    }
}
