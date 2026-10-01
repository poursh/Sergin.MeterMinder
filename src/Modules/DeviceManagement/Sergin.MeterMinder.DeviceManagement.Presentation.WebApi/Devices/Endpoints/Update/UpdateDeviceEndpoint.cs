using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.Update;

internal class UpdateDeviceEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPut("/devices/{deviceId:guid}", async (
            [FromRoute] Guid deviceId, [FromBody] UpdateDeviceModel device, ISender sender) =>
        {
            ErrorOr<UpdateDeviceCommandResponse> res = await sender.Send(new UpdateDeviceCommand(
                deviceId, new DeviceId(device.DeviceId), new DeviceModelInternalId(device.DeviceModelId)));

            return res.ToApiResult();
        });
    }
}
