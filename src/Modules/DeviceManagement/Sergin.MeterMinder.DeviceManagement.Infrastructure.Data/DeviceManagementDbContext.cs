using Microsoft.EntityFrameworkCore;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Outbox;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;

public interface IDeviceManagementDbContext : IDbContext;

/// <summary>
/// Opts into the outbox (<see cref="IOutboxDbContext"/>): the <c>outbox_messages</c> and <c>inbox_messages</c>
/// tables live in this module's schema, added by the <c>AddOutbox</c> migration, so an integration event this
/// module raises rides on the same save as the aggregate that raised it, and a consumer here dedups through
/// its own inbox. No translator, event or handler is declared yet — the tables are ready for the first one.
/// </summary>
internal sealed class DeviceManagementDbContext(DbContextOptions<DeviceManagementDbContext> options)
    : SerginDbContext(options), IDeviceManagementDbContext, IDeviceManagementUnitOfWork, IOutboxDbContext
{
    public const string Schema = "dm";

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        modelBuilder.ApplyOutbox();

        base.OnModelCreating(modelBuilder);
    }
}
