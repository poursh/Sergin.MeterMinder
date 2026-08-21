using Microsoft.EntityFrameworkCore;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;

public interface IDeviceManagementDbContext : IDbContext;

internal sealed class DeviceManagementDbContext(DbContextOptions<DeviceManagementDbContext> options) : SerginDbContext(options), IDeviceManagementDbContext, IDeviceManagementUnitOfWork
{
    public const string Schema = "dm";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
