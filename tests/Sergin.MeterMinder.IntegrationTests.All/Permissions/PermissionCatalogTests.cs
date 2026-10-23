using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Permissions;

public sealed class PermissionCatalogTests
{
    private static PermissionCatalog Build(params Type[] configurations) =>
        PermissionCatalog.FromSources([ModuleConfigurationSource.FromTypes(configurations)]);

    [Fact]
    public void Catalog_ListsWhatEachAggregateRegistered()
    {
        PermissionCatalog catalog = Build(typeof(CatalogTestRootConfiguration));

        Assert.Contains(catalog.Entries, e => e.Definition == CatalogTestPermissions.Read && e.AggregateRoot == typeof(CatalogTestRoot));
        Assert.Contains(catalog.Entries, e => e.Definition == CatalogTestPermissions.Update && e.AggregateRoot == typeof(CatalogTestRoot));
        Assert.Contains(catalog.Entries, e => e.Definition == PermissionDefinition.AllPlatform && e.AggregateRoot is null);
        Assert.Equal(3, catalog.Entries.Count);
    }

    [Fact]
    public void Catalog_GroupsByModuleThenAggregate()
    {
        IReadOnlyList<PermissionCatalogModule> grouped = Build(typeof(CatalogTestRootConfiguration)).Grouped();

        Assert.Equal(["sys", "test"], grouped.Select(m => m.Schema));
        PermissionCatalogGroup group = Assert.Single(grouped[1].Groups);
        Assert.Equal("CatalogTestRoot", group.Name);
        Assert.Equal(
            ["permission.test.things.read", "permission.test.things.update"],
            group.Permissions.Select(p => p.Permission.Value));
        Assert.Equal("Platform", Assert.Single(grouped[0].Groups).Name);
    }

    [Fact]
    public void Catalog_SamePermissionFromTwoAggregates_Fails()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            Build(typeof(CatalogTestRootConfiguration), typeof(OtherCatalogTestRootConfiguration)));

        Assert.Contains("CatalogTestRoot", exception.Message, StringComparison.Ordinal);
        Assert.Contains("OtherCatalogTestRoot", exception.Message, StringComparison.Ordinal);
        Assert.Contains("permission.test.things.read", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_SameNameWithDifferentLabels_Fails()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            PermissionCatalog.FromSources(
            [
                ModuleConfigurationSource.FromTypes(typeof(CatalogTestRootConfiguration)),
                ModuleConfigurationSource.FromTypes(typeof(RelabelledCatalogTestRootConfiguration)),
            ]));

        Assert.Contains("permission.test.things.read", exception.Message, StringComparison.Ordinal);
        Assert.Contains("more than one label", exception.Message, StringComparison.Ordinal);
        Assert.Contains("See things", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("more than one aggregate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_PermissionOutsideTheModuleSchema_Fails()
    {
        var source = ModuleConfigurationSource.FromTypesInSchema("dm", typeof(CatalogTestRootConfiguration));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => PermissionCatalog.FromSources([source]));

        Assert.Contains("permission.test.things.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("dm", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_FromTypesSource_SkipsTheSchemaCheck() =>
        Assert.NotNull(Build(typeof(CatalogTestRootConfiguration)));

    [Fact]
    public void Catalog_RegisteringSys_Fails()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Build(typeof(SysCatalogTestRootConfiguration)));

        Assert.Contains("permission.sys.other", exception.Message, StringComparison.Ordinal);
        Assert.Contains("sys", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_WithAMalformedDefinition_FailsNamingTheClass()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Build(typeof(MalformedCatalogTestRootConfiguration)));

        Assert.Contains("MalformedCatalogPermissions", exception.Message, StringComparison.Ordinal);
        Assert.Contains("MalformedCatalogTestRootConfiguration", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGet_FindsByName_AndMissesAnUnknownOne()
    {
        PermissionCatalog catalog = Build(typeof(CatalogTestRootConfiguration));

        Assert.True(catalog.TryGet(CatalogTestPermissions.Read.Permission, out PermissionDefinition? found));
        Assert.Equal("View things", found.Label);
        Assert.True(catalog.Contains(CatalogTestPermissions.Update));
        Assert.False(catalog.TryGet(Permission.Create("permission.test.nothing.read"), out _));
        Assert.False(PermissionCatalog.Empty.Contains(CatalogTestPermissions.Read));
        Assert.True(PermissionCatalog.Empty.Contains(PermissionDefinition.AllPlatform));
    }

    [Fact]
    public void PermissionsOf_AnUnconfiguredRoot_IsEmpty() =>
        Assert.Empty(AggregateFeatureRegistry.Empty.PermissionsOf(typeof(CatalogTestRoot)));

    [Collection(nameof(IntegrationTestCollection))]
    public sealed class RealHost(SerginWebApiFactory<Program> factory)
    {
        [Fact]
        public void RealHost_CatalogHoldsAllPlatform()
        {
            PermissionCatalog catalog = factory.Services.GetRequiredService<PermissionCatalog>();

            Assert.True(catalog.Contains(PermissionDefinition.AllPlatform));
        }
    }
}
