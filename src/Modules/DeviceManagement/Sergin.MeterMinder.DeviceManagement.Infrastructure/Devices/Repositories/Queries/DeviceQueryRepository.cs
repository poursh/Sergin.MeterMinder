using System.Data.Common;
using Sergin.MeterMinder.DeviceManagement.Application.Devices;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOverview;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Infrastracture.Data;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Devices.Repositories.Queries;

internal sealed class DeviceQueryRepository(
    IDbConnectionFactory connectionFactory,
    ScopeFilter scopeFilter) : IDeviceAllQueryRepositoriy
{
    // The device list's sort and filter fields, keyed by DeviceListFields. The joins are inner and on required
    // foreign keys, so the count and the page read the same rows.
    private static readonly ListQueryColumns ListColumns = new(
        DeviceListFields.Set,
        new Dictionary<string, string>
        {
            [DeviceListFields.DeviceId] = "d.device_id",
            [DeviceListFields.DeviceModelName] = "dm_.name",
            [DeviceListFields.ManufacturerName] = "m_.name",
            [DeviceListFields.ManufacturerId] = "m_.id",
            [DeviceListFields.DeviceModelId] = "d.device_model_id",
            [DeviceListFields.ScopeId] = "d.scope_id",
            [DeviceListFields.CreatedAtUtc] = "d.created_at_utc",
        },
        searchColumns: ["d.device_id", "dm_.name"],
        tieBreaker: "d.id");

    // Every read filters deleted_at_utc IS NULL (SoftDeleteColumns.NotDeletedSql) on the table it reads: raw
    // SQL is not reached by EF's soft-delete query filter. A joined table is not filtered, so a live row still
    // shows the name of a deleted row it points at. A device is scoped, so every read also applies
    // ScopeColumns.ReachSql: a device outside the current subtree or the caller's reach is not found.
    // row_version is split off into its own mapped part: DeviceQueryResponse binds through its constructor, which
    // has no parameter for it, and the version travels beside the response, not on it.
    public async Task<Versioned<DeviceQueryResponse>?> GetDeviceById(
        DeviceIntenralId Id, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        const string queries =
           $$"""
            SELECT d.id, d.device_id AS deviceId, d.device_model_id AS deviceModelId, dm_.name AS deviceModelName,
                   m_.id AS manufacturerId, m_.name AS manufacturerName,
                   d.row_version AS rowVersion
            FROM dm.device d
            JOIN dm.device_model dm_ ON dm_.id = d.device_model_id
            JOIN dm.manufacturer m_ ON m_.id = dm_.manufacturer_id
            WHERE d.id = @Id AND d.deleted_at_utc IS NULL AND {{ScopeColumns.ReachSql}};
            """;

        DynamicParameters parameters = ScopeColumns.AddParameters(new DynamicParameters(new { Id = Id.Value }), scopeFilter);

        IEnumerable<Versioned<DeviceQueryResponse>> rows = await connection.QueryAsync<DeviceQueryResponse, Guid, Versioned<DeviceQueryResponse>>(
            queries,
            (device, rowVersion) => new Versioned<DeviceQueryResponse>(device, RowVersion.Create(rowVersion)),
            parameters,
            splitOn: "rowVersion");

        return rows.SingleOrDefault();
    }

    // Term, Filtering and Sorting come in through ListQuerySql: only ListColumns' expressions reach the text,
    // every value is a parameter, and the same WHERE goes to the count so the total matches the filtered list.
    public async Task<ListQueryResponse<GetDeviceListItem>> GetListAsync(
        ListQuery query, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        DynamicParameters parameters = ScopeColumns.AddParameters(
            new DynamicParameters(new { PageSize = query.Paggination.Size.Value, Offset = query.Paggination.Skip }), scopeFilter);

        ListQuerySqlParts criteria = ListQuerySql.Apply(query, ListColumns, parameters);

        const string from =
            $$"""
            FROM dm.device d
            JOIN dm.device_model dm_ ON dm_.id = d.device_model_id
            JOIN dm.manufacturer m_ ON m_.id = dm_.manufacturer_id
            WHERE d.deleted_at_utc IS NULL AND {{ScopeColumns.ReachSql}}
            """;

        string queries =
            $"""
            SELECT count(*) {from}{criteria.Where};

            SELECT d.id, d.device_id AS deviceId, d.device_model_id AS deviceModelId, dm_.name AS deviceModelName,
                   m_.id AS manufacturerId, m_.name AS manufacturerName
            {from}{criteria.Where}
            {criteria.OrderBy}
            LIMIT @PageSize OFFSET @Offset;
            """;

        // The criteria text holds only ListColumns' expressions; every value is a parameter.
#pragma warning disable S2077
        GridReader res = await connection.QueryMultipleAsync(queries, parameters);
#pragma warning restore S2077

        int count = await res.ReadSingleAsync<int>();
        IReadOnlyCollection<GetDeviceListItem> list = [.. await res.ReadAsync<GetDeviceListItem>()];

        return new ListQueryResponse<GetDeviceListItem>(list, count);
    }

    // Device figures are within the caller's reach, like the list; the catalog figures are not scoped, since the
    // catalog belongs to no scope.
    public async Task<DeviceOverview> GetOverviewAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        const string query =
            $$"""
            SELECT
                (SELECT count(*) FROM dm.device d WHERE d.deleted_at_utc IS NULL AND {{ScopeColumns.ReachSql}})::int AS devices,
                (SELECT count(DISTINCT d.device_model_id) FROM dm.device d WHERE d.deleted_at_utc IS NULL AND {{ScopeColumns.ReachSql}})::int AS modelsInUse,
                (SELECT count(*) FROM dm.device_model WHERE deleted_at_utc IS NULL)::int AS modelsTotal,
                (SELECT count(*) FROM dm.manufacturer WHERE deleted_at_utc IS NULL)::int AS manufacturers,
                (SELECT count(*) FROM dm.device d WHERE d.deleted_at_utc IS NULL AND d.created_at_utc >= @SinceUtc AND {{ScopeColumns.ReachSql}})::int AS addedLast30Days;
            """;

        DynamicParameters parameters = ScopeColumns.AddParameters(
            new DynamicParameters(new { SinceUtc = DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc) }), scopeFilter);

        return await connection.QuerySingleAsync<DeviceOverview>(query, parameters);
    }

    public async Task<AuditInfo?> GetDeviceAuditAsync(DeviceIntenralId id, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        const string query =
            $$"""
            SELECT {{AuditColumns.SelectSql}}
            FROM dm.device
            WHERE id = @Id AND deleted_at_utc IS NULL AND {{ScopeColumns.ReachSql}};
            """;

        DynamicParameters parameters = ScopeColumns.AddParameters(new DynamicParameters(new { Id = id.Value }), scopeFilter);

        return await connection.QuerySingleOrDefaultAsync<AuditInfo>(query, parameters);
    }
}
