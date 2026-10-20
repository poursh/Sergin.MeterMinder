using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Permissions;

/// <summary>DeviceManagement registers its six permissions per aggregate, and every request it configures names only registered ones.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceManagementPermissionCatalogTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void Catalog_HoldsEveryDeviceManagementPermission()
    {
        PermissionCatalog catalog = factory.Services.GetRequiredService<PermissionCatalog>();

        PermissionCatalogModule module = Assert.Single(catalog.Grouped(), m => m.Schema == "dm");

        Assert.Equal(["Device", "Manufacturer"], module.Groups.Select(g => g.Name));

        PermissionDefinition[] all =
        [
            DevicePermissions.Read, DevicePermissions.Update, DevicePermissions.Delete,
            ManufacturerPermissions.Read, ManufacturerPermissions.Update, ManufacturerPermissions.Delete,
        ];

        Assert.Equal(6, module.Groups.Sum(g => g.Permissions.Count));
        Assert.All(all, definition => Assert.True(catalog.Contains(definition), definition.Permission.Value));
    }

    [Fact]
    public void EveryDmConfiguration_RequiresOnlyRegisteredPermissions()
    {
        PermissionCatalog catalog = factory.Services.GetRequiredService<PermissionCatalog>();
        CommandConfigurationRegistry registry = factory.Services.GetRequiredService<CommandConfigurationRegistry>();

        Type[] dm = [.. registry.ConfiguredTypes.Where(t => t.FullName!.StartsWith("Sergin.MeterMinder.DeviceManagement", StringComparison.Ordinal))];

        Assert.NotEmpty(dm);
        foreach (Type type in dm)
        {
            Assert.All(
                registry.For(type).RequiredPermissions,
                permission => Assert.True(catalog.TryGet(permission, out _), $"{type.Name} requires unregistered {permission.Value}"));
        }
    }
}
