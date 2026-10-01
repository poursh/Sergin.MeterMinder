using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.Update;

internal class UpdateManufacturerEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPut("/manufacturers/{manufacturerId:guid}", async (
            [FromRoute] Guid manufacturerId, [FromBody] UpdateManufacturerModel manufacturer, ISender sender) =>
        {
            ErrorOr<UpdateManufacturerCommandResponse> res = await sender.Send(new UpdateManufacturerCommand(
                manufacturerId,
                new ManufacturerName(manufacturer.Name),
                manufacturer.Address is null ? null : new ManufacturerAddress(manufacturer.Address)));

            return res.ToApiResult();
        });
    }
}
