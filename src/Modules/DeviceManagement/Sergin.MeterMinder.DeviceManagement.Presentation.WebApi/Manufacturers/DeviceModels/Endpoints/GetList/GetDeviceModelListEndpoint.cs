using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.DeviceModels.Endpoints.GetList;

internal class GetDeviceModelListEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder
            .MapGet("/manufacturers/{manufacturerId:guid}/models", async (
                [FromRoute] Guid manufacturerId, [AsParameters] ListQueryRequestModel request, ISender sender) =>
            {
                ErrorOr<ListQueryResponse<GetDeviceModelListItem>> res = await sender.Send(
                    new GetDeviceModelListQueryCommand(
                        new ManufacturerId(manufacturerId),
                        request.ToPaggination(), request.Term, request.Filtering, request.Sorting));

                return res.ToApiResult();
            })
            .Produces<ListQueryResponse<GetDeviceModelListItem>>();
    }
}
