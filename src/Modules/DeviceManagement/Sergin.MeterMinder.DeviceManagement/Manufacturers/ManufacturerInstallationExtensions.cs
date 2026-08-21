using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Manufacturers.Repositories;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Manufacturers.Repositories.Queries;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.Create;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.GetList;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Manufacturers.Endpoints.GetOne;

namespace Sergin.MeterMinder.DeviceManagement.Manufacturers;

internal static class ManufacturerInstallationExtensions
{
    internal static IServiceCollection AddManufacturerDependencies(this IServiceCollection services)
    {
        services.AddTransient<IManufacturerRepository, ManufacturerRepository>();
        services.AddTransient<IManufacturerAllQueryRepository, ManufacturerQueryRepository>();
        services.AddTransient<IGetManufacturerQueryRepository, ManufacturerQueryRepository>();
        services.AddTransient<IGetManufacturerListQueryRepository, ManufacturerQueryRepository>();

        return services;
    }

    internal static IEndpointRouteBuilder MapManufacturerEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        new CreateManufacturerEndpoint().MapEndpoint(routeBuilder);
        new GetManufacturerEndpoint().MapEndpoint(routeBuilder);
        new GetManufacturerListEndpoint().MapEndpoint(routeBuilder);

        return routeBuilder;
    }
}
