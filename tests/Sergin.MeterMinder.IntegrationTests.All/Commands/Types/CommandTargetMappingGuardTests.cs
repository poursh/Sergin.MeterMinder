using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sergin.Administration.UserAccess.Application.Configurations;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.SharedKernel.Hosts.Commands;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.Modules;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands.Types;

/// <summary>
/// The mapping guard (host start): a local typed record's root is mapped by a module DbContext, and its child is
/// reachable from the root through EF navigations. The model comes from a test context that never connects.
/// </summary>
public sealed class CommandTargetMappingGuardTests
{
    private static readonly Assembly DmContracts = typeof(DeleteDeviceCommand).Assembly;
    private static readonly Assembly UaContracts = typeof(CreateUserCommand).Assembly;

    [Fact]
    public void MappedRootAndReachableChildren_Pass() =>
        Assert.Null(Record.Exception(() => CommandTargetMappingGuard.EnsureMapped(
            [typeof(MappedDeleteCommand), typeof(MappedChildRemoveCommand), typeof(MappedGrandChildRemoveCommand), typeof(TestUntypedCommand)],
            Models())));

    [Fact]
    public void UnmappedRoot_IsRefused()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            CommandTargetMappingGuard.EnsureMapped([typeof(TestDeleteCommand)], Models()));

        Assert.Contains(typeof(TestDeleteCommand).FullName!, refused.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(TestTypedRoot).FullName!, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildUnreachableFromTheRoot_IsRefused()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            CommandTargetMappingGuard.EnsureMapped([typeof(UnreachableChildRemoveCommand)], Models()));

        Assert.Contains(typeof(UnreachableChildRemoveCommand).FullName!, refused.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(MappingLoose).FullName!, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MappingGuard_IgnoresTypesNotPassedAsLocal()
    {
        // A remote module's records are mapped by no context in this process: the host hands the mapping guard its
        // local Contracts assemblies alone, while the shape guard reads both.
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sergin:ApplicationName"] = "Test",
            ["Sergin:ConnectionStrings:Database"] = "Host=localhost;Database=mapping",
        });
        builder.AddSerginCore([new LocalTestModule()], [new RemoteTestModule()]);

        using ServiceProvider services = builder.Services.BuildServiceProvider();

        Assert.Equal([DmContracts], services.GetRequiredService<LocalContractsAssemblies>().Assemblies);
        Assert.Contains(UaContracts, services.GetRequiredService<CommandTypeGuard>().Assemblies);

        // And the guard checks only what it is given: an unmapped root outside the list is not looked for.
        Assert.Null(Record.Exception(() => CommandTargetMappingGuard.EnsureMapped([typeof(MappedDeleteCommand)], Models())));
    }

    /// <summary>What the host bootstraps run: local Contracts types against every module context's model.</summary>
    internal static void EnsureRealHostMapped(IServiceProvider services) =>
        CommandTargetMappingGuard.EnsureMapped(services.GetRequiredService<LocalContractsAssemblies>().RequestTypes(), services);

    private static IModel[] Models()
    {
        using MappingTestDbContext context =
            new(new DbContextOptionsBuilder<MappingTestDbContext>().UseNpgsql("Host=unused").Options);

        return [context.Model];
    }
}

internal sealed class LocalTestModule : ISerginModule
{
    public string Name => "MappingLocal";

    public string Schema => "mappinglocal";

    public Assembly ApplicationAssembly => DeviceManagementApplicationAssemblyReference.Assembly;

    public Assembly ContractsAssembly => typeof(DeleteDeviceCommand).Assembly;

    public Assembly ConfigurationsAssembly => DeviceManagementApplicationConfigurationsAssemblyReference.Assembly;

    public void AddServices(IServiceCollection services, IConfigurationSection configuration)
    {
    }

    public Task MigrateAsync(IServiceProvider services) => Task.CompletedTask;
}

internal sealed class RemoteTestModule : ISerginRemoteModule
{
    public string Name => "MappingRemote";

    public string Schema => "mappingremote";

    public Assembly ContractsAssembly => typeof(CreateUserCommand).Assembly;

    public Assembly ConfigurationsAssembly => UserAccessApplicationConfigurationsAssemblyReference.Assembly;

    public void AddRemoteServices(IServiceCollection services, IConfigurationSection configuration)
    {
    }
}
