using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sergin.MeterMinder.DeviceManagement;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Modules;
using Sergin.SharedKernel.Presentation.Blazor.Modules;
using Sergin.UserAccess;

namespace Sergin.MeterMinder.IntegrationTests.All.Permissions;

/// <summary>
/// The catalogue as the UI host checks it at start: every nav entry's required permission must be one an aggregate
/// registered, or the host refuses to start naming the module, the entry and the permission.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PermissionCatalogHostTests(SerginWebApiFactory<Program> factory)
{
    private static readonly PermissionDefinition Unregistered =
        PermissionDefinition.Create("permission.testnav.things.read", "Never registered");

    [Fact]
    public void Host_Starts_WithEveryNavPermissionRegistered()
    {
        PermissionCatalog catalog = factory.Services.GetRequiredService<PermissionCatalog>();
        SerginUiModuleCatalog modules = factory.Services.GetRequiredService<SerginUiModuleCatalog>();

        Assert.All(
            modules.NavItems.Where(item => item.RequiredPermission is not null),
            item => Assert.True(catalog.Contains(item.RequiredPermission!), item.Label));
    }

    [Fact]
    public void NavItem_WithAnUnregisteredPermission_FailsStart()
    {
        using WebApplicationFactory<Program> broken = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton(new SerginUiModuleCatalog(
                [new DeviceManagementModule(), new UserAccessModule(), new UnregisteredNavModule()]))));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => broken.CreateClient());

        Assert.Contains(UnregisteredNavModule.Label, error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(UnregisteredNavModule).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(Unregistered.Permission.Value, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NavItem_ReadingAMalformedDefinition_FailsStartNamingTheDefinitionsClass()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sergin:DevUser:Id"] = Guid.NewGuid().ToString(),
            ["Sergin:DevUser:UserName"] = "dev",
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => builder.AddSerginBlazorApp([new MalformedNavModule()]));

        Assert.Contains(nameof(MalformedNavPermissions), error.Message, StringComparison.Ordinal);
        Assert.Contains(MalformedNavPermissions.BadName, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("type initializer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A definitions class whose initializer rejects its own name; only the nav entry below reads it.</summary>
    private static class MalformedNavPermissions
    {
        public const string BadName = "not-a-permission-name";

        public static PermissionDefinition Bad { get; } = PermissionDefinition.Create(BadName, "Bad");
    }

    /// <summary>A UI module whose nav entry reads <see cref="MalformedNavPermissions"/>, so building the catalogue throws.</summary>
    private sealed class MalformedNavModule : ISerginWebUiModule
    {
        private static Assembly NoComponents => typeof(PermissionDefinition).Assembly;

        public string Schema => "testbad";

        public Assembly ApplicationAssembly => NoComponents;

        public Assembly ContractsAssembly => NoComponents;

        public Assembly ConfigurationsAssembly => NoComponents;

        public Assembly UiAssembly => NoComponents;

        public SerginNavGroup NavGroup { get; } = new("Test", string.Empty);

        public IReadOnlyCollection<SerginNavItem> NavItems =>
            [new("Bad", "/testbad/things", string.Empty, RequiredPermission: MalformedNavPermissions.Bad)];

        public void AddServices(IServiceCollection services, IConfigurationSection configuration)
        {
        }

        public Task MigrateAsync(IServiceProvider services) => Task.CompletedTask;
    }

    /// <summary>
    /// A UI module whose one entry requires a permission no aggregate registered. Only the nav check reads it: its
    /// assemblies hold no routable component, and it is never composed, so nothing calls its services.
    /// </summary>
    private sealed class UnregisteredNavModule : ISerginWebUiModule
    {
        public const string Label = "Unregistered things";

        private static Assembly NoComponents => typeof(PermissionDefinition).Assembly;

        public string Schema => "testnav";

        public Assembly ApplicationAssembly => NoComponents;

        public Assembly ContractsAssembly => NoComponents;

        public Assembly ConfigurationsAssembly => NoComponents;

        public Assembly UiAssembly => NoComponents;

        public SerginNavGroup NavGroup { get; } = new("Test", string.Empty);

        public IReadOnlyCollection<SerginNavItem> NavItems { get; } =
            [new(Label, "/testnav/things", string.Empty, RequiredPermission: Unregistered)];

        public void AddServices(IServiceCollection services, IConfigurationSection configuration)
        {
        }

        public Task MigrateAsync(IServiceProvider services) => Task.CompletedTask;
    }
}
