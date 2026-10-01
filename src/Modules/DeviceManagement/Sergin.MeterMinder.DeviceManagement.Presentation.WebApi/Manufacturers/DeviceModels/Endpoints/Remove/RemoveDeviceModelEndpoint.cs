using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.DeviceModels.Endpoints.Remove;

internal class RemoveDeviceModelEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapDelete("/manufacturers/{manufacturerId:guid}/models/{modelId:guid}", async (
            [FromRoute] Guid manufacturerId, [FromRoute] Guid modelId, ISender sender) =>
        {
            ErrorOr<RemoveDeviceModelCommandResponse> res =
                await sender.Send(new RemoveDeviceModelCommand(new ManufacturerId(manufacturerId), modelId));

            return res.ToApiResult();
        });
    }
}
