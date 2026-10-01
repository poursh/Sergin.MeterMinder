using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Audit;

/// <summary>
/// The real module end to end: a manufacturer created through ISerginDispatcher, as a Blazor page does it,
/// is stamped with the dispatcher's user (the configured dev user) and has no modified stamp yet.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceManagementAuditTests(SerginWebApiFactory<Program> factory)
{
    private static readonly Guid DevUserId = Guid.Parse("01920000-0000-7000-8000-000000000001");

    [Fact]
    public async Task CreateManufacturer_IsStampedWithTheDispatchersUser()
    {
        DateTime before = DateTime.UtcNow;

        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<CreateManufacturerCommandResponse> created = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName($"audit-{Guid.CreateVersion7()}"), null));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);
        DateTime after = DateTime.UtcNow;

        using IServiceScope readScope = factory.Services.CreateScope();
        IDeviceManagementDbContext context = readScope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>();
        ManufacturerId id = new(created.Value.Id);

        AuditStampTests.AuditRow row = await context.Set<Manufacturer>()
            .Where(manufacturer => manufacturer.Id == id)
            .Select(manufacturer => new AuditStampTests.AuditRow(
                EF.Property<DateTime>(manufacturer, AuditColumns.CreatedAtUtc),
                EF.Property<Guid>(manufacturer, AuditColumns.CreatedBy),
                EF.Property<DateTime?>(manufacturer, AuditColumns.ModifiedAtUtc),
                EF.Property<Guid?>(manufacturer, AuditColumns.ModifiedBy)))
            .SingleAsync();

        Assert.Equal(DevUserId, row.CreatedBy);
        AuditStampTests.AssertWithin(before, after, row.CreatedAtUtc);
        Assert.Null(row.ModifiedAtUtc);
        Assert.Null(row.ModifiedBy);
    }
}
