using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetAudit;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.GetAudit;

internal class GetDeviceAuditEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/devices/{deviceId:guid}/audit", async ([FromRoute] Guid deviceId, ISender sender) =>
        {
            ErrorOr<AuditInfo> res = await sender.Send(new GetDeviceAuditQueryCommand(deviceId));

            return res.ToApiResult();
        });
    }
}
