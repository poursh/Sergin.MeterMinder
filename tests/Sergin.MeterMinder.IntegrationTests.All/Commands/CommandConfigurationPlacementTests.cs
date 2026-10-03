using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.SharedKernel.Modules;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands;

/// <summary>
/// Only a module's ContractsAssembly is read for command configurations, so one written in the
/// ApplicationAssembly would be ignored and its request would run unprotected. AddSerginCore refuses that.
/// </summary>
public sealed class CommandConfigurationPlacementTests
{
    [Fact]
    public void ConfigurationInTheApplicationAssembly_StopsComposition_NamingIt()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Sergin:ApplicationName"] = "Test" });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            builder.AddSerginCore([new MisplacedConfigurationModule()]));

        Assert.Contains(typeof(ConfiguredTestCommandConfiguration).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(".Application.Contracts", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A module whose "application" assembly is this test assembly, which holds configurations.</summary>
    private sealed class MisplacedConfigurationModule : ISerginModule
    {
        public string Schema => "misplaced";

        public Assembly ApplicationAssembly => typeof(CommandConfigurationPlacementTests).Assembly;

        public Assembly ContractsAssembly => typeof(DeleteDeviceCommand).Assembly;

        public Assembly ConfigurationsAssembly => typeof(DeleteDeviceCommand).Assembly;

        public void AddServices(IServiceCollection services, IConfigurationSection configuration)
        {
        }

        public Task MigrateAsync(IServiceProvider services) => Task.CompletedTask;
    }
}
