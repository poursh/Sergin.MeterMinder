using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetOne;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.DeviceModels.Endpoints.GetOne;

internal class GetDeviceModelEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/manufacturers/{manufacturerId:guid}/models/{modelId:guid}", async (
            [FromRoute] Guid manufacturerId, [FromRoute] Guid modelId, ISender sender) =>
        {
            ErrorOr<DeviceModelQueryResponse> res = await sender.Send(
                new GetDeviceModelByIdQueryCommand(manufacturerId, modelId));

            return res.ToApiResult();
        });
    }
}
