using System.Reflection;
using Sergin.MeterMinder.DeviceManagement;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations;
using Sergin.SharedKernel.Domain.Securities;
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

    /// <summary>
    /// Application and Presentation reference .Application.Configurations for the permission definitions, so
    /// its public surface must stay the assembly marker plus static classes of <see cref="PermissionDefinition"/>
    /// properties. A public configuration would otherwise be reachable from a handler or a page.
    /// </summary>
    [Fact]
    public void ConfigurationsAssemblies_ExportOnlyTheMarkerAndPermissionDefinitions()
    {
        Type[] markers =
        [
            typeof(DeviceManagementApplicationConfigurationsAssemblyReference),
            typeof(UserAccessApplicationConfigurationsAssemblyReference),
        ];

        string[] offenders =
        [
            .. markers
                .SelectMany(marker => marker.Assembly.GetExportedTypes())
                .Where(type => !markers.Contains(type) && !IsPermissionDefinitionHolder(type))
                .Select(type => type.FullName!),
        ];

        Assert.Empty(offenders);
    }

    private static bool IsPermissionDefinitionHolder(Type type) =>
        type is { IsAbstract: true, IsSealed: true }
        && type.Name.EndsWith("Permissions", StringComparison.Ordinal)
        && type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .All(member => member switch
            {
                PropertyInfo property => property.PropertyType == typeof(PermissionDefinition),
                MethodInfo method => method.IsSpecialName,
                _ => false,
            });
}
