using System.Linq.Expressions;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.MeterMinder.IntegrationTests.All.Concurrency;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Updates;

/// <summary>
/// The four update-side slices — UpdateDevice, UpdateManufacturer, RenameDeviceModel, RemoveDeviceModel —
/// through ISerginDispatcher from a scope, as the edit pages send them. Guarded sends go through
/// VersionedDispatch; the 428/412 cases live in DeviceManagementConcurrencyTests.Updates.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed partial class DeviceManagementUpdateTests(SerginWebApiFactory<Program> factory)
{
    private static readonly Guid DevUserId = Guid.Parse("01920000-0000-7000-8000-000000000001");

    private static readonly string[] SoftDeleteFilter = [SoftDeleteColumns.QueryFilterName];

    private static string NewName(string prefix) => $"{prefix}-{Guid.CreateVersion7()}";

    private static ISerginDispatcher AnonymousDispatcher(IServiceScope scope)
    {
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = ClaimsPrincipalUserContext.Create(null);

        return scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
    }

    private static async Task<ManufacturerId> CreateManufacturerAsync(ISerginDispatcher dispatcher, string? name = null)
    {
        ErrorOr<CreateManufacturerCommandResponse> created = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName(name ?? NewName("manufacturer")), Address: null));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);
        return new ManufacturerId(created.Value.Id);
    }

    private static async Task<DeviceModelInternalId> AddDeviceModelAsync(
        ISerginDispatcher dispatcher, ManufacturerId manufacturerId, string? name = null)
    {
        ErrorOr<AddDeviceModelCommandResponse> added = await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new AddDeviceModelCommand(manufacturerId, new DeviceModelName(name ?? NewName("model"))));

        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);
        return new DeviceModelInternalId(added.Value.Id);
    }

    private static async Task<Guid> CreateDeviceAsync(
        ISerginDispatcher dispatcher, DeviceModelInternalId modelId, string? deviceId = null)
    {
        ErrorOr<CreateDeviceCommandResponse> created = await dispatcher.SendAsync(
            new CreateDeviceCommand(new DeviceId(deviceId ?? NewName("device")), modelId));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);
        return created.Value.Id;
    }

    private static async Task DeleteDeviceAsync(ISerginDispatcher dispatcher, Guid id)
    {
        ErrorOr<DeleteDeviceCommandResponse> deleted = await dispatcher.SendAtDeviceVersionAsync(id, new DeleteDeviceCommand(id));

        Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Description : string.Empty);
    }

    private async Task<ModifiedStamp> ReadModifiedAsync<TEntity>(Expression<Func<TEntity, bool>> match)
        where TEntity : class
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IDeviceManagementDbContext context = scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>();

        return await context.Set<TEntity>()
            .Where(match)
            .Select(entity => new ModifiedStamp(
                EF.Property<DateTime?>(entity, AuditColumns.ModifiedAtUtc),
                EF.Property<Guid?>(entity, AuditColumns.ModifiedBy)))
            .SingleAsync();
    }

    private async Task<DateTime?> ReadDeletedAtAsync<TEntity>(Expression<Func<TEntity, bool>> match)
        where TEntity : class
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IDeviceManagementDbContext context = scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>();

        return await context.Set<TEntity>()
            .IgnoreQueryFilters(SoftDeleteFilter)
            .Where(match)
            .Select(entity => EF.Property<DateTime?>(entity, SoftDeleteColumns.DeletedAtUtc))
            .SingleAsync();
    }

    private sealed record ModifiedStamp(DateTime? ModifiedAtUtc, Guid? ModifiedBy);
}
