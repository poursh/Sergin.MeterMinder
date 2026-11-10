using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.GetOne;

internal class GetDeviceEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/devices/{deviceId:guid}", async ([FromRoute] Guid deviceId, ISender sender) =>
        {
            ErrorOr<DeviceQueryResponse> res = await sender.Send(new GetDeviceByIdQueryCommand(new DeviceIntenralId(deviceId)));

            return res.ToApiResult();
        });
    }
}
