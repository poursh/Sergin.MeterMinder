using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.SharedKernel.Application.Dispatching;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.GetOne;

internal class GetManufacturerEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/manufacturers/{manufacturerId:guid}", async ([FromRoute] Guid manufacturerId, ISerginSender sender) =>
        {
            ErrorOr<ManufacturerQueryResponse> res = await sender.SendAsync(new GetManufacturerByIdQueryCommand(manufacturerId));

            return res.ToApiResult();
        });
    }
}
