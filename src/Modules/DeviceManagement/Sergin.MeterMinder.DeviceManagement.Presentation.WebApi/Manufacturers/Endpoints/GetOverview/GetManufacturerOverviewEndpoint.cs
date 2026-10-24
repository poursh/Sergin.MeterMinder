using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOverview;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.GetOverview;

internal class GetManufacturerOverviewEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/manufacturers/{manufacturerId:guid}/overview", async ([FromRoute] Guid manufacturerId, ISender sender) =>
        {
            ErrorOr<ManufacturerOverview> res = await sender.Send(new GetManufacturerOverviewQueryCommand(manufacturerId));

            return res.ToApiResult();
        });
    }
}
