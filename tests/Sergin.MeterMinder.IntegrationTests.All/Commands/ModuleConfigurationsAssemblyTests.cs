using System.Reflection;
using Sergin.MeterMinder.DeviceManagement;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations;
using Sergin.Administration.Settings;
using Sergin.Administration.Settings.Application.Configurations;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.Administration.UserAccess;
using Sergin.Administration.UserAccess.Application.Configurations;

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

    [Fact]
    public void Settings_NamesItsConfigurationsAssembly()
    {
        SettingsModule module = new();

        Assert.Same(SettingsApplicationConfigurationsAssemblyReference.Assembly, module.ConfigurationsAssembly);
        Assert.NotSame(module.ContractsAssembly, module.ConfigurationsAssembly);
        Assert.NotSame(module.ApplicationAssembly, module.ConfigurationsAssembly);
    }

    /// <summary>
    /// Application and Presentation reference .Application.Configurations for the permission definitions and the
    /// declared texts, so its public surface must stay the assembly marker plus static classes of
    /// <see cref="PermissionDefinition"/> properties and static classes of text declarations. A public configuration
    /// would otherwise be reachable from a handler or a page.
    /// </summary>
    [Fact]
    public void ConfigurationsAssemblies_ExportOnlyTheMarkerPermissionAndTextDefinitions()
    {
        Type[] markers =
        [
            typeof(DeviceManagementApplicationConfigurationsAssemblyReference),
            typeof(UserAccessApplicationConfigurationsAssemblyReference),
            typeof(SettingsApplicationConfigurationsAssemblyReference),
        ];

        string[] offenders =
        [
            .. markers
                .SelectMany(marker => marker.Assembly.GetExportedTypes())
                .Where(type => !markers.Contains(type) && !IsPermissionDefinitionHolder(type) && !IsTextDeclarationHolder(type))
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

    // A <Name>Texts class: its category and the texts and fields declared in it, nothing else.
    private static bool IsTextDeclarationHolder(Type type) =>
        type is { IsAbstract: true, IsSealed: true }
        && type.Name.EndsWith("Texts", StringComparison.Ordinal)
        && type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .All(member => member switch
            {
                FieldInfo field => field.IsInitOnly
                    && (field.FieldType == typeof(TextCategory) || field.FieldType == typeof(TextDefinition) || field.FieldType == typeof(FieldText)
                        || field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(EnumTexts<>)),
                _ => false,
            });
}
