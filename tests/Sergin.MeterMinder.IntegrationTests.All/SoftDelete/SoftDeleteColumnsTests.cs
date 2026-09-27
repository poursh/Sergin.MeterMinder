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

namespace Sergin.MeterMinder.IntegrationTests.All.SoftDelete;

/// <summary>
/// The EF side of the soft-delete feature on the real dm model: each type configured SoftDeletable() (and
/// DeviceModel, as Manufacturer's child) carries the two nullable shadow columns, the named query filter and
/// the pair CHECK constraint, and its unique index is partial over live rows. A type nothing configured
/// carries none of it. Read off the design-time model: the runtime model EF queries with drops check
/// constraints, which only migrations need.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class SoftDeleteColumnsTests(SerginWebApiFactory<Program> factory)
{
    public static TheoryData<Type, string> SoftDeletableTypes => new()
    {
        { typeof(Device), "device" },
        { typeof(Manufacturer), "manufacturer" },
        { typeof(DeviceModel), "device_model" },
    };

    [Theory]
    [MemberData(nameof(SoftDeletableTypes))]
    public void SoftDeletableType_CarriesTheWholeShape(Type entityType, string table)
    {
        IEntityType mapped = FindEntityType(entityType);

        Assert.True(SoftDeleteColumns.IsSoftDeletable(mapped));

        AssertColumn(mapped, SoftDeleteColumns.DeletedAtUtc, SoftDeleteColumns.DeletedAtUtcColumn, typeof(DateTime?));
        AssertColumn(mapped, SoftDeleteColumns.DeletedBy, SoftDeleteColumns.DeletedByColumn, typeof(Guid?));

        Assert.NotNull(mapped.FindDeclaredQueryFilter(SoftDeleteColumns.QueryFilterName));

        ICheckConstraint check = Assert.IsAssignableFrom<ICheckConstraint>(
            mapped.FindCheckConstraint(SoftDeleteColumns.PairCheckName(table)));
        Assert.Equal(SoftDeleteColumns.PairCheckSql, check.Sql);
    }

    [Theory]
    [InlineData(typeof(Device))]
    [InlineData(typeof(DeviceModel))]
    public void UniqueIndex_IsPartialOverLiveRows(Type entityType)
    {
        IIndex unique = Assert.Single(FindEntityType(entityType).GetIndexes(), index => index.IsUnique);

        Assert.Equal(SoftDeleteColumns.NotDeletedSql, unique.GetFilter());
    }

    [Fact]
    public void UnconfiguredType_CarriesNone()
    {
        IEntityType outbox = FindEntityType(typeof(OutboxMessage));

        Assert.False(SoftDeleteColumns.IsSoftDeletable(outbox));
        Assert.Null(outbox.FindProperty(SoftDeleteColumns.DeletedAtUtc));
        Assert.Empty(outbox.GetDeclaredQueryFilters());
    }

    private IEntityType FindEntityType(Type entityType)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext context = Assert.IsAssignableFrom<DbContext>(
            scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>());

        return Assert.IsAssignableFrom<IEntityType>(
            context.GetService<IDesignTimeModel>().Model.FindEntityType(entityType));
    }

    private static void AssertColumn(IEntityType entityType, string property, string column, Type clrType)
    {
        IProperty mapped = Assert.IsAssignableFrom<IProperty>(entityType.FindProperty(property));

        Assert.True(mapped.IsShadowProperty(), $"{property} must be a shadow property, not a domain member.");
        Assert.Equal(clrType, mapped.ClrType);
        Assert.True(mapped.IsNullable);
        Assert.Equal(column, mapped.GetColumnName());
    }
}
