using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Outbox;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Audit;

/// <summary>
/// The EF side of the audit feature: the three DeviceManagement types configured Audited() carry the four
/// shadow properties under their snake_case column names with the agreed nullability, and a type nothing
/// configured (the outbox table) carries none. Read off the runtime model, which is what the interceptor
/// and the startup guard see.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class AuditColumnsTests(SerginWebApiFactory<Program> factory)
{
    public static TheoryData<Type, string> AuditedTypes => new()
    {
        { typeof(Device), "device" },
        { typeof(Manufacturer), "manufacturer" },
        { typeof(DeviceModel), "device_model" },
    };

    [Theory]
    [MemberData(nameof(AuditedTypes))]
    public void AuditedType_CarriesTheFourColumns(Type entityType, string table)
    {
        IEntityType mapped = FindEntityType(entityType);

        Assert.Equal(table, mapped.GetTableName());
        Assert.True(AuditColumns.IsAudited(mapped));

        AssertColumn(mapped, AuditColumns.CreatedAtUtc, AuditColumns.CreatedAtUtcColumn, typeof(DateTime), nullable: false);
        AssertColumn(mapped, AuditColumns.CreatedBy, AuditColumns.CreatedByColumn, typeof(Guid), nullable: false);
        AssertColumn(mapped, AuditColumns.ModifiedAtUtc, AuditColumns.ModifiedAtUtcColumn, typeof(DateTime?), nullable: true);
        AssertColumn(mapped, AuditColumns.ModifiedBy, AuditColumns.ModifiedByColumn, typeof(Guid?), nullable: true);
    }

    [Fact]
    public void UnconfiguredType_CarriesNoAuditColumns()
    {
        IEntityType outbox = FindEntityType(typeof(OutboxMessage));

        Assert.False(AuditColumns.IsAudited(outbox));
        Assert.Null(outbox.FindProperty(AuditColumns.CreatedAtUtc));
    }

    private IEntityType FindEntityType(Type entityType)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext context = Assert.IsAssignableFrom<DbContext>(
            scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>());

        return Assert.IsAssignableFrom<IEntityType>(context.Model.FindEntityType(entityType));
    }

    private static void AssertColumn(IEntityType entityType, string property, string column, Type clrType, bool nullable)
    {
        IProperty mapped = Assert.IsAssignableFrom<IProperty>(entityType.FindProperty(property));

        Assert.True(mapped.IsShadowProperty(), $"{property} must be a shadow property, not a domain member.");
        Assert.Equal(clrType, mapped.ClrType);
        Assert.Equal(nullable, mapped.IsNullable);
        Assert.Equal(column, mapped.GetColumnName());
    }
}
