using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Delete;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.Delete;

internal class DeleteManufacturerEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapDelete("/manufacturers/{manufacturerId:guid}", async ([FromRoute] Guid manufacturerId, ISender sender) =>
        {
            ErrorOr<DeleteManufacturerCommandResponse> res = await sender.Send(new DeleteManufacturerCommand(manufacturerId));

            return res.ToApiResult();
        });
    }
}
