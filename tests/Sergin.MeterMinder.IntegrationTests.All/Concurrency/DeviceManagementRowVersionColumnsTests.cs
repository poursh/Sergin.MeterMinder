using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Outbox;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>The dm model: both roots carry the row_version token, DeviceModel names Manufacturer as its root, and an unconfigured type carries nothing.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceManagementRowVersionColumnsTests(SerginWebApiFactory<Program> factory)
{
    [Theory]
    [InlineData(typeof(Device))]
    [InlineData(typeof(Manufacturer))]
    public void Root_CarriesTheToken(Type entityType)
    {
        IProperty version = Assert.IsAssignableFrom<IProperty>(FindEntityType(entityType).FindProperty(RowVersionColumns.RowVersion));

        Assert.True(version.IsConcurrencyToken);
        Assert.False(version.IsNullable);
        Assert.Equal(RowVersionColumns.RowVersionColumn, version.GetColumnName());
    }

    [Fact]
    public void DeviceModel_HasNoColumn_AndNamesManufacturer()
    {
        IEntityType model = FindEntityType(typeof(DeviceModel));

        Assert.Null(model.FindProperty(RowVersionColumns.RowVersion));
        Assert.Equal(FindEntityType(typeof(Manufacturer)).Name, RowVersionColumns.RootOf(model));
    }

    [Fact]
    public void UnconfiguredType_CarriesNone()
    {
        IEntityType outbox = FindEntityType(typeof(OutboxMessage));

        Assert.False(RowVersionColumns.IsVersioned(outbox));
        Assert.Null(RowVersionColumns.RootOf(outbox));
        Assert.Null(outbox.FindProperty(RowVersionColumns.RowVersion));
    }

    private IEntityType FindEntityType(Type entityType)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext context = Assert.IsAssignableFrom<DbContext>(
            scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>());

        return Assert.IsAssignableFrom<IEntityType>(
            context.GetService<IDesignTimeModel>().Model.FindEntityType(entityType));
    }
}
