using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.Application;
using Sergin.MeterMinder.Devices;
using Sergin.MeterMinder.Infrastructure.Data;
using Sergin.MeterMinder.Manufacturers;
using Sergin.MeterMinder.Presentation.Blazor;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Modules;

namespace Sergin.MeterMinder;

public sealed class MeterMinderModule : ISerginWebApiModule, ISerginWebUiModule
{
    public string Schema => MeterMinderDbContext.Schema;

    public Assembly ApplicationAssembly => MeterMinderApplicationAssemblyReference.Assembly;

    public Assembly UiAssembly => MeterMinderBlazorAssemblyReference.Assembly;

    public IReadOnlyCollection<SerginNavItem> NavItems => MeterMinderNavigation.Items;

    public void AddServices(IServiceCollection services, IConfigurationSection configuration)
    {
        services.AddModuleDbContext<MeterMinderDbContext, IMeterMinderDbContext, IMeterMinderUnitOfWork>(configuration, MeterMinderDbContext.Schema);

        services.AddDeviceDependencies();
        services.AddManufacturerDependencies();
    }

    public Task MigrateAsync(IServiceProvider services) => services.MigrateDbContextAsync<MeterMinderDbContext>();

    public void MapEndpoints(RouteGroupBuilder group) => group.MapDeviceEndpoints().MapManufacturerEndpoints();
}
