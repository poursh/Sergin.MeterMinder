using System.Data.Common;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOverview;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Infrastracture.Data;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Manufacturers.Repositories.Queries;

internal sealed class ManufacturerQueryRepository(
    IDbConnectionFactory connectionFactory) : IManufacturerAllQueryRepository
{
    private static readonly ListQueryColumns ListColumns = new(
        ManufacturerListFields.Set,
        new Dictionary<string, string> { [ManufacturerListFields.Name] = "name" },
        searchColumns: ["name", "address"],
        tieBreaker: "id");

    // Every read filters deleted_at_utc IS NULL (SoftDeleteColumns.NotDeletedSql) on the table it reads: raw
    // SQL is not reached by EF's soft-delete query filter. A joined table is not filtered, so a live row still
    // shows the name of a deleted row it points at.
    // row_version is split off into its own mapped part: ManufacturerQueryResponse binds through its constructor,
    // which has no parameter for it, and the version travels beside the response, not on it.
    public async Task<Versioned<ManufacturerQueryResponse>?> GetManufacturerById(
        ManufacturerId id, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        string queries =
           """
            SELECT id, name, address, row_version AS rowVersion
            FROM dm.manufacturer
            WHERE id = @Id AND deleted_at_utc IS NULL;
            """;

        IEnumerable<Versioned<ManufacturerQueryResponse>> rows = await connection.QueryAsync<ManufacturerQueryResponse, Guid, Versioned<ManufacturerQueryResponse>>(
            queries,
            (manufacturer, rowVersion) => new Versioned<ManufacturerQueryResponse>(manufacturer, RowVersion.Create(rowVersion)),
            new { Id = id.Value },
            splitOn: "rowVersion");

        return rows.SingleOrDefault();
    }

    public async Task<ListQueryResponse<GetManufacturerListItem>> GetListAsync(
        ListQuery query, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        DynamicParameters parameters = new(new { PageSize = query.Paggination.Size.Value, Offset = query.Paggination.Skip });
        ListQuerySqlParts criteria = ListQuerySql.Apply(query, ListColumns, parameters);

        string queries =
            $"""
            SELECT count(*) FROM dm.manufacturer WHERE deleted_at_utc IS NULL{criteria.Where};

            SELECT id, name, address
            FROM dm.manufacturer
            WHERE deleted_at_utc IS NULL{criteria.Where}
            {criteria.OrderBy}
            LIMIT @PageSize OFFSET @Offset;
            """;

        // The criteria text holds only ListColumns' expressions; every value is a parameter.
#pragma warning disable S2077
        GridReader res = await connection.QueryMultipleAsync(queries, parameters);
#pragma warning restore S2077

        int count = await res.ReadSingleAsync<int>();
        IReadOnlyCollection<GetManufacturerListItem> list = [.. await res.ReadAsync<GetManufacturerListItem>()];

        return new ListQueryResponse<GetManufacturerListItem>(list, count);
    }

    // Device counts are in every scope: the catalog belongs to no scope, and the number is the one that decides
    // whether a model can be removed, which RemoveDeviceModelCommandValidator also counts in every scope.
    public async Task<ManufacturerOverview?> GetManufacturerOverviewAsync(
        ManufacturerId id, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        const string query =
            """
            SELECT
                (SELECT count(*) FROM dm.device_model dm_ WHERE dm_.manufacturer_id = m.id AND dm_.deleted_at_utc IS NULL)::int AS models,
                (SELECT count(*) FROM dm.device d
                    JOIN dm.device_model dm_ ON dm_.id = d.device_model_id
                    WHERE dm_.manufacturer_id = m.id AND dm_.deleted_at_utc IS NULL AND d.deleted_at_utc IS NULL)::int AS devices
            FROM dm.manufacturer m
            WHERE m.id = @Id AND m.deleted_at_utc IS NULL;
            """;

        return await connection.QuerySingleOrDefaultAsync<ManufacturerOverview>(query, new { Id = id.Value });
    }

    public async Task<AuditInfo?> GetManufacturerAuditAsync(ManufacturerId id, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        const string query =
            $$"""
            SELECT {{AuditColumns.SelectSql}}, {{ScopeColumns.UnscopedSelectSql}}
            FROM dm.manufacturer
            WHERE id = @Id AND deleted_at_utc IS NULL;
            """;

        return await connection.QuerySingleOrDefaultAsync<AuditInfo>(query, new { Id = id.Value });
    }
}
