using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands;

/// <summary>
/// The registry as the real host builds it: one singleton holding every module's declarations, and a bad
/// declaration a test adds stops the host from starting, naming the offending configuration.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class CommandConfigurationHostTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void Host_RegistersOneRegistry_HoldingModuleDeclarations()
    {
        CommandConfigurationRegistry registry = factory.Services.GetRequiredService<CommandConfigurationRegistry>();

        Assert.Same(registry, factory.Services.GetRequiredService<CommandConfigurationRegistry>());
        Assert.Contains(typeof(DeleteDeviceCommand), registry.ConfiguredTypes);
    }

    [Fact]
    public void BadDeclaration_StopsTheHost_NamingTheConfiguration()
    {
        using WebApplicationFactory<Program> broken = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton(CommandConfigurationSource.FromTypes(typeof(VersionedQueryConfiguration)))));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => broken.CreateClient());

        Assert.Contains(nameof(VersionedQueryConfiguration), error.Message, StringComparison.Ordinal);
    }
}
