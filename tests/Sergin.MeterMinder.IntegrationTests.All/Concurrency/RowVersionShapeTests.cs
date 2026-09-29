using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>
/// What Versioned() adds to a model: a required Guid shadow column row_version marked as a concurrency token on
/// the root only, and on each child an annotation naming its root, so the interceptors can find it.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RowVersionShapeTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private WebApplicationFactory<Program> versionedFactory = default!;

    public Task InitializeAsync()
    {
        versionedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices((context, services) =>
            services.AddModuleDbContext<TestVersionedDbContext, ITestVersionedDbContext, ITestVersionedUnitOfWork>(
                context.Configuration.GetSection("Sergin"), TestVersionedDbContext.Schema)));

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await versionedFactory.DisposeAsync();

    [Fact]
    public void Builder_DeclaresVersioned_AndChildrenNeverTakeIt()
    {
        var registry =
            AggregateFeatureRegistry.FromConfigurationTypes([typeof(PalletAggregateFeatureConfiguration)]);

        Assert.True(registry.For(typeof(Pallet)).Versioned);
        Assert.False(registry.ForChild(typeof(Pallet), typeof(Bay)).Versioned);
    }

    [Fact]
    public void Root_CarriesARequiredConcurrencyTokenColumn()
    {
        IEntityType pallet = FindEntityType(typeof(Pallet));
        IProperty version = Assert.IsAssignableFrom<IProperty>(pallet.FindProperty(RowVersionColumns.RowVersion));

        Assert.True(RowVersionColumns.IsVersioned(pallet));
        Assert.True(version.IsShadowProperty());
        Assert.Equal(typeof(Guid), version.ClrType);
        Assert.False(version.IsNullable);
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(RowVersionColumns.RowVersionColumn, version.GetColumnName());
    }

    [Fact]
    public void Child_HasNoColumn_AndNamesItsRoot()
    {
        IEntityType bay = FindEntityType(typeof(Bay));

        Assert.False(RowVersionColumns.IsVersioned(bay));
        Assert.Null(bay.FindProperty(RowVersionColumns.RowVersion));
        Assert.Equal(FindEntityType(typeof(Pallet)).Name, RowVersionColumns.RootOf(bay));
    }

    private IEntityType FindEntityType(Type entityType)
    {
        using IServiceScope scope = versionedFactory.Services.CreateScope();
        DbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();

        return Assert.IsAssignableFrom<IEntityType>(
            context.GetService<IDesignTimeModel>().Model.FindEntityType(entityType));
    }
}
