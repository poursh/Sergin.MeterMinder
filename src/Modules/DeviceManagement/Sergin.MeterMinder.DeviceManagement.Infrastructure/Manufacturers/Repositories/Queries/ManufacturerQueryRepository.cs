using System.Data.Common;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Infrastracture.Data;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Manufacturers.Repositories.Queries;

internal sealed class ManufacturerQueryRepository(
    IDbConnectionFactory connectionFactory) : IManufacturerAllQueryRepository
{
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

        string queries =
            """
            SELECT count(*) FROM dm.manufacturer WHERE deleted_at_utc IS NULL;

            SELECT id, name, address
            FROM dm.manufacturer
            WHERE deleted_at_utc IS NULL
            ORDER BY id
            LIMIT @PageSize OFFSET @Offset;
            """;

        GridReader res = await connection.QueryMultipleAsync(
            queries, new { PageSize = query.Paggination.Size.Value, Offset = query.Paggination.Skip });

        int count = await res.ReadSingleAsync<int>();
        IReadOnlyCollection<GetManufacturerListItem> list = [.. await res.ReadAsync<GetManufacturerListItem>()];

        return new ListQueryResponse<GetManufacturerListItem>(list, count);
    }
}
