using Sergin.MeterMinder.DeviceManagement;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations;
using Sergin.UserAccess;
using Sergin.UserAccess.Application.Configurations;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands;

/// <summary>
/// Each module names its own .Application.Configurations assembly, distinct from its Application and
/// Contracts assemblies — the placement guard relies on the three being different.
/// </summary>
public sealed class ModuleConfigurationsAssemblyTests
{
    [Fact]
    public void DeviceManagement_NamesItsConfigurationsAssembly()
    {
        DeviceManagementModule module = new();

        Assert.Same(DeviceManagementApplicationConfigurationsAssemblyReference.Assembly, module.ConfigurationsAssembly);
        Assert.NotSame(module.ContractsAssembly, module.ConfigurationsAssembly);
        Assert.NotSame(module.ApplicationAssembly, module.ConfigurationsAssembly);
    }

    [Fact]
    public void UserAccess_NamesItsConfigurationsAssembly()
    {
        UserAccessModule module = new();

        Assert.Same(UserAccessApplicationConfigurationsAssemblyReference.Assembly, module.ConfigurationsAssembly);
        Assert.NotSame(module.ContractsAssembly, module.ConfigurationsAssembly);
        Assert.NotSame(module.ApplicationAssembly, module.ConfigurationsAssembly);
    }
}
