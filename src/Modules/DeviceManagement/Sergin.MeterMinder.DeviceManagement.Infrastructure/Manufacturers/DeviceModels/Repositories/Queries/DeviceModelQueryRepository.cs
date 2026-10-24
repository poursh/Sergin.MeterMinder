using System.Data.Common;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Infrastracture.Data;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Manufacturers.DeviceModels.Repositories.Queries;

/// <summary>
/// The read side of the <c>DeviceModel</c> entity. Reads are raw SQL over <c>dm.device_model</c> and owe
/// nothing to the aggregate boundary, so the entity gets its own query repository; writes still go through
/// <c>Manufacturer.AddModel</c> and <c>ManufacturerRepository</c> only.
/// </summary>
internal sealed class DeviceModelQueryRepository(
    IDbConnectionFactory connectionFactory) : IDeviceModelAllQueryRepository
{
    private static readonly ListQueryColumns ListColumns = new(
        DeviceModelListFields.Set,
        new Dictionary<string, string> { [DeviceModelListFields.Name] = "dm_.name" },
        searchColumns: ["dm_.name"],
        tieBreaker: "dm_.id");

    // Every read filters deleted_at_utc IS NULL (SoftDeleteColumns.NotDeletedSql) on the table it reads: raw
    // SQL is not reached by EF's soft-delete query filter. A joined table is not filtered, so a live row still
    // shows the name of a deleted row it points at.
    //
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
            WHERE dm_.id = @Id AND dm_.manufacturer_id = @ManufacturerId AND dm_.deleted_at_utc IS NULL;
            """;

        return await connection.QuerySingleOrDefaultAsync<DeviceModelQueryResponse>(
            queries, new { Id = id.Value, ManufacturerId = manufacturerId.Value });
    }

    public async Task<ListQueryResponse<GetDeviceModelListItem>> GetListAsync(
        ManufacturerId manufacturerId, ListQuery query, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        DynamicParameters parameters = new(new
        {
            ManufacturerId = manufacturerId.Value,
            PageSize = query.Paggination.Size.Value,
            Offset = query.Paggination.Skip
        });
        ListQuerySqlParts criteria = ListQuerySql.Apply(query, ListColumns, parameters);

        string queries =
            $"""
            SELECT count(*) FROM dm.device_model dm_ WHERE dm_.manufacturer_id = @ManufacturerId AND dm_.deleted_at_utc IS NULL{criteria.Where};

            SELECT dm_.id, dm_.name,
                   (SELECT count(*)::int FROM dm.device d WHERE d.device_model_id = dm_.id AND d.deleted_at_utc IS NULL) AS deviceCount
            FROM dm.device_model dm_
            WHERE dm_.manufacturer_id = @ManufacturerId AND dm_.deleted_at_utc IS NULL{criteria.Where}
            {criteria.OrderBy}
            LIMIT @PageSize OFFSET @Offset;
            """;

        // The criteria text holds only ListColumns' expressions; every value is a parameter.
#pragma warning disable S2077
        GridReader res = await connection.QueryMultipleAsync(queries, parameters);
#pragma warning restore S2077

        int count = await res.ReadSingleAsync<int>();
        IReadOnlyCollection<GetDeviceModelListItem> list = [.. await res.ReadAsync<GetDeviceModelListItem>()];

        return new ListQueryResponse<GetDeviceModelListItem>(list, count);
    }
}
