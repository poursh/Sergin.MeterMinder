using Sergin.SharedKernel.Domain.Securities;

namespace Sergin.MeterMinder.IntegrationTests.All.Permissions;

public sealed class PermissionDefinitionTests
{
    [Fact]
    public void Create_KeepsTheNameAndLabel()
    {
        var definition = PermissionDefinition.Create("permission.dm.devices.read", "View devices");

        Assert.Equal("permission.dm.devices.read", definition.Permission.Value);
        Assert.Equal("View devices", definition.Label);
        Assert.Equal("dm", definition.Schema);
    }

    [Fact]
    public void Create_WithAMalformedName_Throws() =>
        Assert.Throws<ArgumentException>(() => PermissionDefinition.Create("devices.read", "x"));

    [Fact]
    public void Create_WithABlankLabel_Throws() =>
        Assert.Throws<ArgumentException>(() => PermissionDefinition.Create("permission.dm.devices.read", " "));

    [Fact]
    public void Equality_IsByName()
    {
        var first = PermissionDefinition.Create("permission.dm.devices.read", "View devices");
        var second = PermissionDefinition.Create("permission.dm.devices.read", "Read devices");

        Assert.True(first.Equals(second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void AllPlatform_IsTheSysPermission()
    {
        Assert.Equal(Permission.AllPlatform, PermissionDefinition.AllPlatform.Permission);
        Assert.Equal("sys", PermissionDefinition.AllPlatform.Schema);
    }
}
