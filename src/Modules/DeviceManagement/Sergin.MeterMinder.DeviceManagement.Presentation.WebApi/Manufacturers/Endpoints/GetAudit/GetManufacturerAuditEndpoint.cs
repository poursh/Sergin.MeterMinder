using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetAudit;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.GetAudit;

internal class GetManufacturerAuditEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/manufacturers/{manufacturerId:guid}/audit", async ([FromRoute] Guid manufacturerId, ISender sender) =>
        {
            ErrorOr<AuditInfo> res = await sender.Send(new GetManufacturerAuditQueryCommand(manufacturerId));

            return res.ToApiResult();
        });
    }
}
