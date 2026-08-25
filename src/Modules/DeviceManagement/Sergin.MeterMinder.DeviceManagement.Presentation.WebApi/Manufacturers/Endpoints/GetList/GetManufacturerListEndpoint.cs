using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.GetList;
internal class GetManufacturerListEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder
            .MapGet("/manufacturers", async ([AsParameters] ListQueryRequestModel request, ISender sender) =>
            {
                ErrorOr<ListQueryResponse<GetManufacturerListItem>> res = await sender.Send(
                    new GetManufacturerListQueryCommand(request.ToPaggination(), request.Term, request.Filtering, request.Sorting));

                return res.ToApiResult();
            })
            .Produces<ListQueryResponse<GetManufacturerListItem>>();
    }
}
