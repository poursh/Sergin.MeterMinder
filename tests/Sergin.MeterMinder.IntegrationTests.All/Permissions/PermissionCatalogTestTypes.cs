using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Securities;

namespace Sergin.MeterMinder.IntegrationTests.All.Permissions;

internal sealed class CatalogTestRoot : AggregateRoot<Guid>;

internal sealed class OtherCatalogTestRoot : AggregateRoot<Guid>;

internal static class CatalogTestPermissions
{
    public static PermissionDefinition Read { get; } =
        PermissionDefinition.Create("permission.test.things.read", "View things");

    public static PermissionDefinition Update { get; } =
        PermissionDefinition.Create("permission.test.things.update", "Change things");

    public static PermissionDefinition ReadRelabelled { get; } =
        PermissionDefinition.Create("permission.test.things.read", "See things");

    public static PermissionDefinition Sys { get; } =
        PermissionDefinition.Create("permission.sys.other", "Other platform thing");
}

internal static class MalformedCatalogPermissions
{
    public static PermissionDefinition Bad { get; } = PermissionDefinition.Create("bad", "x");
}

internal sealed class CatalogTestRootConfiguration : IAggregateFeatureConfiguration<CatalogTestRoot>
{
    // Update first, and Read twice: ordering and de-duplication are the catalogue's job.
    public void Configure(AggregateFeatureBuilder<CatalogTestRoot> builder) =>
        builder.Permissions(CatalogTestPermissions.Update, CatalogTestPermissions.Read, CatalogTestPermissions.Read);
}

internal sealed class OtherCatalogTestRootConfiguration : IAggregateFeatureConfiguration<OtherCatalogTestRoot>
{
    public void Configure(AggregateFeatureBuilder<OtherCatalogTestRoot> builder) =>
        builder.Permissions(CatalogTestPermissions.Read);
}

internal sealed class RelabelledCatalogTestRootConfiguration : IAggregateFeatureConfiguration<OtherCatalogTestRoot>
{
    public void Configure(AggregateFeatureBuilder<OtherCatalogTestRoot> builder) =>
        builder.Permissions(CatalogTestPermissions.ReadRelabelled);
}

internal sealed class SysCatalogTestRootConfiguration : IAggregateFeatureConfiguration<CatalogTestRoot>
{
    public void Configure(AggregateFeatureBuilder<CatalogTestRoot> builder) =>
        builder.Permissions(CatalogTestPermissions.Sys);
}

internal sealed class MalformedCatalogTestRootConfiguration : IAggregateFeatureConfiguration<CatalogTestRoot>
{
    public void Configure(AggregateFeatureBuilder<CatalogTestRoot> builder) =>
        builder.Permissions(MalformedCatalogPermissions.Bad);
}
