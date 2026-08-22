using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts;
using Sergin.MeterMinder.DeviceManagement.Devices;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.MeterMinder.DeviceManagement.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Modules;

namespace Sergin.MeterMinder.DeviceManagement;

public sealed class DeviceManagementModule : ISerginWebApiModule, ISerginWebUiModule
{
    public string Schema => DeviceManagementDbContext.Schema;

    public Assembly ApplicationAssembly => DeviceManagementApplicationAssemblyReference.Assembly;

    public Assembly ContractsAssembly => DeviceManagementApplicationContractsAssemblyReference.Assembly;

    public Assembly UiAssembly => DeviceManagementBlazorAssemblyReference.Assembly;

    public IReadOnlyCollection<SerginNavItem> NavItems => DeviceManagementNavigation.Items;

    public void AddServices(IServiceCollection services, IConfigurationSection configuration)
    {
        services.AddModuleDbContext<DeviceManagementDbContext, IDeviceManagementDbContext, IDeviceManagementUnitOfWork>(configuration, DeviceManagementDbContext.Schema);

        services.AddDeviceDependencies();
        services.AddManufacturerDependencies();
    }

    public Task MigrateAsync(IServiceProvider services) => services.MigrateDbContextAsync<DeviceManagementDbContext>();

    public void MapEndpoints(RouteGroupBuilder group) => group.MapDeviceEndpoints().MapManufacturerEndpoints();
}
