using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sergin.MeterMinder.IntegrationTests.All.Tasks;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Tasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Modules;
using Sergin.SharedKernel.Presentation.Blazor.Modules;
using Sergin.SharedKernel.Presentation.Blazor.Navigation;
using Sergin.SharedKernel.Presentation.Blazor.Tasks.Runs;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// The shell's own drawer entries and their startup guards: the Tasks entry the host adds when a module opts into
/// tasks, the route rule for the shell's pages, the nav permission check over shell entries, and the reserved
/// <c>sys</c> schema.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ShellNavEntryTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void RealHost_ShowsTaskRunsUnderAdministration_WithItsCaption()
    {
        SerginUiModuleCatalog catalog = factory.Services.GetRequiredService<SerginUiModuleCatalog>();
        SerginNavSection administration = catalog.NavSections.Single(section => section.Group == SerginNavGroups.Administration);

        Assert.Equal(["User Access", "Settings", "Tasks"], administration.Subsections.Select(subsection => subsection.Caption?.Default));
        Assert.Equal(SerginNavGroups.Administration, catalog.GroupOf(SerginTaskRunsPage.Route));
    }

    [Fact]
    public void ShellEntriesFor_IsEmpty_WithoutATasksRegistration() =>
        Assert.Empty(SerginWebUiExtensions.ShellEntriesFor(new ServiceCollection()));

    [Fact]
    public void ShellEntriesFor_HoldsTheTasksEntry_WithOne()
    {
        ServiceCollection services = new();
        services.AddSingleton(new TasksRegistration("dm", typeof(object)));

        Assert.Equal([SerginTaskNavigation.Entry], SerginWebUiExtensions.ShellEntriesFor(services));
    }

    [Fact]
    public void ShellRoute_OutsideSys_IsAViolation()
    {
        SerginUiModuleCatalog catalog = factory.Services.GetRequiredService<SerginUiModuleCatalog>();

        string[] violations = [.. SerginWebUiExtensions.RouteViolations(catalog, typeof(ScheduleProbePage).Assembly)];

        Assert.Contains(violations, violation => violation.Contains(nameof(ScheduleProbePage), StringComparison.Ordinal));
        Assert.Empty(SerginWebUiExtensions.RouteViolations(catalog, typeof(SerginUiModuleCatalog).Assembly));
    }

    [Theory]
    [InlineData("sys")]
    [InlineData("Sys")]
    public void ModuleWithSchemaSys_IsRefused(string schema)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CreateBuilder().AddSerginCore([new SysModule(schema)]));

        Assert.Contains(nameof(SysModule), error.Message, StringComparison.Ordinal);
        Assert.Contains("sys", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteModuleWithSchemaSys_IsRefused()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CreateBuilder().AddSerginCore([], [new SysRemoteModule()]));

        Assert.Contains(nameof(SysRemoteModule), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnregisteredShellPermission_IsRefused()
    {
        var unregistered = PermissionDefinition.Create("permission.zz.things.read", "Read things");
        SerginUiModuleCatalog catalog = new(
            [],
            [new SerginShellNavEntry(SerginNavGroups.Administration, null, [new SerginNavItem(TestText.Of("Shell thing"), "/sys/thing", string.Empty, 1, unregistered)])]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            SerginWebUiExtensions.ValidateNavPermissions(catalog, PermissionCatalog.Empty));

        Assert.Contains("Shell thing", error.Message, StringComparison.Ordinal);
    }

    private static HostApplicationBuilder CreateBuilder()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sergin:ApplicationName"] = "Test",
            ["Sergin:ConnectionStrings:Database"] = "Host=localhost;Database=shellnav",
        });
        return builder;
    }

    private sealed class SysModule(string schema) : ISerginModule
    {
        public string Name => "Sys";

        public string Schema => schema;

        public Assembly ApplicationAssembly => typeof(SysModule).Assembly;

        public Assembly ContractsAssembly => typeof(SysModule).Assembly;

        public Assembly ConfigurationsAssembly => typeof(SysModule).Assembly;

        public void AddServices(IServiceCollection services, IConfigurationSection configuration)
        {
        }

        public Task MigrateAsync(IServiceProvider services) => Task.CompletedTask;
    }

    private sealed class SysRemoteModule : ISerginRemoteModule
    {
        public string Name => "SysRemote";

        public string Schema => "sys";

        public Assembly ContractsAssembly => typeof(SysRemoteModule).Assembly;

        public Assembly ConfigurationsAssembly => typeof(SysRemoteModule).Assembly;

        public void AddRemoteServices(IServiceCollection services, IConfigurationSection configuration)
        {
        }
    }
}
