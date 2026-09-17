using System.Data.Common;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Infrastracture.Data;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Manufacturers.DeviceModels.Repositories.Queries;

/// <summary>
/// The read side of the <c>DeviceModel</c> entity. Reads are raw SQL over <c>dm.device_model</c> and owe
/// nothing to the aggregate boundary, so the entity gets its own query repository; writes still go through
/// <c>Manufacturer.AddModel</c> and <c>ManufacturerRepository</c> only.
/// </summary>
internal sealed class DeviceModelQueryRepository(
    IDbConnectionFactory connectionFactory) : IDeviceModelAllQueryRepository
{
    // dm is the schema, so the device_model alias is dm_ — the two must never read alike.
    public async Task<DeviceModelQueryResponse?> GetDeviceModelById(
        ManufacturerId manufacturerId, DeviceModelInternalId id, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        // Keyed by both ids: a model addressed under the wrong manufacturer is null, hence NotFound.
        string queries =
           """
            SELECT dm_.id, dm_.manufacturer_id AS manufacturerId, m.name AS manufacturerName, dm_.name
            FROM dm.device_model dm_
            JOIN dm.manufacturer m ON m.id = dm_.manufacturer_id
            WHERE dm_.id = @Id AND dm_.manufacturer_id = @ManufacturerId;
            """;

        return await connection.QuerySingleOrDefaultAsync<DeviceModelQueryResponse>(
            queries, new { Id = id.Value, ManufacturerId = manufacturerId.Value });
    }

    public async Task<ListQueryResponse<GetDeviceModelListItem>> GetListAsync(
        ManufacturerId manufacturerId, ListQuery query, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        string queries =
            """
            SELECT count(*) FROM dm.device_model WHERE manufacturer_id = @ManufacturerId;

            SELECT id, name
            FROM dm.device_model
            WHERE manufacturer_id = @ManufacturerId
            ORDER BY id
            LIMIT @PageSize OFFSET @Offset;
            """;

        GridReader res = await connection.QueryMultipleAsync(
            queries,
            new
            {
                ManufacturerId = manufacturerId.Value,
                PageSize = query.Paggination.Size.Value,
                Offset = query.Paggination.Skip
            });

        int count = await res.ReadSingleAsync<int>();
        IReadOnlyCollection<GetDeviceModelListItem> list = [.. await res.ReadAsync<GetDeviceModelListItem>()];

        return new ListQueryResponse<GetDeviceModelListItem>(list, count);
    }
}
