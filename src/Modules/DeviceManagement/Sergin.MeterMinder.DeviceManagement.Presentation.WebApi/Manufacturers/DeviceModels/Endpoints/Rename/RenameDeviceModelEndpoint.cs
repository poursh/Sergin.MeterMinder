using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.DeviceModels.Endpoints.Rename;

internal class RenameDeviceModelEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPut("/manufacturers/{manufacturerId:guid}/models/{modelId:guid}", async (
            [FromRoute] Guid manufacturerId, [FromRoute] Guid modelId, [FromBody] RenameDeviceModelModel deviceModel, ISender sender) =>
        {
            ErrorOr<RenameDeviceModelCommandResponse> res = await sender.Send(new RenameDeviceModelCommand(
                new ManufacturerId(manufacturerId), modelId, new DeviceModelName(deviceModel.Name)));

            return res.ToApiResult();
        });
    }
}
