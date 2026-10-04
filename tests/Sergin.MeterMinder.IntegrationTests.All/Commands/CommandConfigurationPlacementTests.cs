using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.MeterMinder.IntegrationTests.All.Aggregates;
using Sergin.SharedKernel.Modules;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands;

/// <summary>
/// Command and aggregate feature configurations are read from a module's ConfigurationsAssembly only, so one
/// written in its Application or Contracts assembly would be ignored. AddSerginCore refuses that, for local
/// and remote modules alike. This test assembly holds configurations of both kinds, which makes it the
/// misplaced assembly in every case below.
/// </summary>
public sealed class CommandConfigurationPlacementTests
{
    private static readonly Assembly MisplacedAssembly = typeof(CommandConfigurationPlacementTests).Assembly;
    private static readonly Assembly DmApplication = DeviceManagementApplicationAssemblyReference.Assembly;
    private static readonly Assembly DmContracts = typeof(DeleteDeviceCommand).Assembly;
    private static readonly Assembly DmConfigurations = DeviceManagementApplicationConfigurationsAssemblyReference.Assembly;

    private static readonly string CommandConfiguration = typeof(ConfiguredTestCommandConfiguration).FullName!;
    private static readonly string AggregateConfiguration = typeof(UnmappedEntityAggregateFeatureConfiguration).FullName!;

    [Fact]
    public void ConfigurationInTheApplicationAssembly_StopsComposition_NamingBothKinds()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CreateBuilder().AddSerginCore([new PlacementModule(MisplacedAssembly, DmContracts, DmConfigurations)]));

        Assert.Contains(CommandConfiguration, error.Message, StringComparison.Ordinal);
        Assert.Contains(AggregateConfiguration, error.Message, StringComparison.Ordinal);
        Assert.Contains(".Application.Configurations", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationInTheContractsAssembly_StopsComposition()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CreateBuilder().AddSerginCore([new PlacementModule(DmApplication, MisplacedAssembly, DmConfigurations)]));

        Assert.Contains(CommandConfiguration, error.Message, StringComparison.Ordinal);
        Assert.Contains(AggregateConfiguration, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAssemblyScannedTwice_NamesEachTypeOnce()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CreateBuilder().AddSerginCore([new PlacementModule(MisplacedAssembly, MisplacedAssembly, DmConfigurations)]));

        Assert.Equal(1, error.Message.Split(CommandConfiguration).Length - 1);
    }

    [Fact]
    public void RemoteModuleConfigurationInContracts_StopsComposition()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CreateBuilder().AddSerginCore([], [new PlacementRemoteModule(MisplacedAssembly, DmConfigurations)]));

        Assert.Contains(CommandConfiguration, error.Message, StringComparison.Ordinal);
        Assert.Contains(AggregateConfiguration, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationsAssemblyAlsoApplication_Composes()
    {
        IConfigurationSection section =
            CreateBuilder().AddSerginCore([new PlacementModule(DmConfigurations, DmContracts, DmConfigurations)]);

        Assert.NotNull(section);
    }

    [Fact]
    public void ConfigurationsAssemblyAlsoContracts_Composes()
    {
        IConfigurationSection section =
            CreateBuilder().AddSerginCore([new PlacementModule(DmApplication, DmConfigurations, DmConfigurations)]);

        Assert.NotNull(section);
    }

    [Fact]
    public void TheRealModules_Compose() =>
        Assert.NotNull(CreateBuilder().AddSerginCore([new PlacementModule(DmApplication, DmContracts, DmConfigurations)]));

    private static HostApplicationBuilder CreateBuilder()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sergin:ApplicationName"] = "Test",
            ["Sergin:ConnectionStrings:Database"] = "Host=localhost;Database=placement",
        });
        return builder;
    }

    private sealed class PlacementModule(Assembly application, Assembly contracts, Assembly configurations) : ISerginModule
    {
        public string Schema => "placement";

        public Assembly ApplicationAssembly => application;

        public Assembly ContractsAssembly => contracts;

        public Assembly ConfigurationsAssembly => configurations;

        public void AddServices(IServiceCollection services, IConfigurationSection configuration)
        {
        }

        public Task MigrateAsync(IServiceProvider services) => Task.CompletedTask;
    }

    private sealed class PlacementRemoteModule(Assembly contracts, Assembly configurations) : ISerginRemoteModule
    {
        public string Schema => "placement";

        public Assembly ContractsAssembly => contracts;

        public Assembly ConfigurationsAssembly => configurations;

        public void AddRemoteServices(IServiceCollection services, IConfigurationSection configuration)
        {
        }
    }
}
