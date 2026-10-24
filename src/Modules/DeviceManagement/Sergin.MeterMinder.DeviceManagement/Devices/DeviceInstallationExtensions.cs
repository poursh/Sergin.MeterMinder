using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetAudit;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOverview;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Devices.Repositories;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Devices.Repositories.Queries;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.Create;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.Delete;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.GetAudit;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.GetList;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.GetOne;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.GetOverview;
using Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.Update;

namespace Sergin.MeterMinder.DeviceManagement.Devices;

internal static class DeviceInstallationExtensions
{
    internal static IServiceCollection AddDeviceDependencies(this IServiceCollection services)
    {
        services.AddTransient<IDeviceRepository, DeviceRepository>();
        services.AddTransient<IDeviceAllQueryRepositoriy, DeviceQueryRepository>();
        services.AddTransient<IGetDeviceQueryRepository, DeviceQueryRepository>();
        services.AddTransient<IGetDeviceListQueryRepository, DeviceQueryRepository>();
        services.AddTransient<IGetDeviceOverviewQueryRepository, DeviceQueryRepository>();
        services.AddTransient<IGetDeviceAuditQueryRepository, DeviceQueryRepository>();

        return services;
    }

    internal static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        new CreateDeviceEndpoint().MapEndpoint(routeBuilder);
        new GetDeviceEndpoint().MapEndpoint(routeBuilder);
        new GetDeviceListEndpoint().MapEndpoint(routeBuilder);
        new DeleteDeviceEndpoint().MapEndpoint(routeBuilder);
        new UpdateDeviceEndpoint().MapEndpoint(routeBuilder);
        new GetDeviceOverviewEndpoint().MapEndpoint(routeBuilder);
        new GetDeviceAuditEndpoint().MapEndpoint(routeBuilder);

        return routeBuilder;
    }
}
