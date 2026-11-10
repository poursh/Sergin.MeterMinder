using Microsoft.Extensions.DependencyInjection;
using Sergin.Administration.Settings.Application.Contracts;
using Sergin.Administration.UserAccess.Application.Contracts;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands.Types;

/// <summary>
/// Regression pins every command-type change must keep green: the exact set of commands guarded by an expected
/// version, and the exact set of text catalog keys (translations are stored by key).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class MigrationPinTests(SerginWebApiFactory<Program> factory)
{
    private static readonly string[] GuardedNames =
    [
        "AddDeviceModel", "ChangeTranslation", "DeleteDevice", "DeleteLanguage", "DeleteManufacturer", "DeleteRole",
        "DeleteTranslation", "GrantRolePermission", "PublishLanguage", "RemoveDeviceModel", "RenameDeviceModel",
        "RenameLanguage", "RenameRole", "RenameScope", "RevokeRolePermission", "SetLanguageDirection",
        "SetRoleInheritable", "UnpublishLanguage", "UpdateDevice", "UpdateManufacturer",
    ];

    [Fact]
    public void GuardedSet_IsExactlyTheTwentyCommands()
    {
        CommandConfigurationRegistry registry = factory.Services.GetRequiredService<CommandConfigurationRegistry>();

        string[] guarded =
        [
            .. new[]
            {
                DeviceManagementApplicationContractsAssemblyReference.Assembly,
                UserAccessApplicationContractsAssemblyReference.Assembly,
                SettingsApplicationContractsAssemblyReference.Assembly,
            }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => registry.For(type).RequiresExpectedVersion)
            .Select(type => type.Name.EndsWith("Command", StringComparison.Ordinal) ? type.Name[..^"Command".Length] : type.Name),
        ];

        Assert.Equal(GuardedNames.Order(StringComparer.Ordinal), guarded.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CatalogKeys_AreUnchanged()
    {
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();

        string[] actual = [.. catalog.Entries.Select(entry => entry.Definition.Key.Value).Order(StringComparer.Ordinal)];
        string path = Path.Combine(AppContext.BaseDirectory, "Commands", "Types", "catalog-keys.txt");
        string[] expected = System.IO.File.ReadAllLines(path);

        Assert.Equal(expected, actual);
    }
}
