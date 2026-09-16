using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.AddDeviceModel;

internal class AddDeviceModelEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder
            .MapPost("/manufacturers/{manufacturerId:guid}/models", async (
                [FromRoute] Guid manufacturerId, [FromBody] NewDeviceModelModel deviceModel, ISender sender) =>
            {
                ErrorOr<AddDeviceModelCommandResponse> res = await sender.Send(
                    new AddDeviceModelCommand(new ManufacturerId(manufacturerId), new DeviceModelName(deviceModel.Name)));

                return res.ToApiResult();
            })
            .Produces<AddDeviceModelCommandResponse>();
    }
}
