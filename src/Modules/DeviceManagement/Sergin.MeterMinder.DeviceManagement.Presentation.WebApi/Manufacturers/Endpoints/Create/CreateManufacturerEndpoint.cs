using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Dispatching;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.Create;

internal class CreateManufacturerEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder
            .MapPost("/manufacturers", async ([FromBody] NewManufacturerModel manufacturer, ISerginSender sender) =>
            {
                ErrorOr<CreateManufacturerCommandResponse> res = await sender.SendAsync(
                    new CreateManufacturerCommand(
                        new ManufacturerName(manufacturer.Name),
                        manufacturer.Address is null ? null : new ManufacturerAddress(manufacturer.Address)));

                return res.ToApiResult();
            })
            .Produces<CreateManufacturerCommandResponse>();
    }
}
