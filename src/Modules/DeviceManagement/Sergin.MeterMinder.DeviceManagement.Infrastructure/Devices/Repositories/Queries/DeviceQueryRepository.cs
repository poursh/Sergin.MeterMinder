using System.Data.Common;
using Sergin.MeterMinder.DeviceManagement.Application.Devices;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetList;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Infrastracture.Data;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Devices.Repositories.Queries;

internal sealed class DeviceQueryRepository(
    IDbConnectionFactory connectionFactory) : IDeviceAllQueryRepositoriy
{
    // Every read filters deleted_at_utc IS NULL (SoftDeleteColumns.NotDeletedSql) on the table it reads: raw
    // SQL is not reached by EF's soft-delete query filter. A joined table is not filtered, so a live row still
    // shows the name of a deleted row it points at.
    // row_version is split off into its own mapped part: DeviceQueryResponse binds through its constructor, which
    // has no parameter for it, and the version travels beside the response, not on it.
    public async Task<Versioned<DeviceQueryResponse>?> GetDeviceById(
        DeviceIntenralId Id, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        string queries =
           """
            SELECT d.id, d.device_id AS deviceId, d.device_model_id AS deviceModelId, dm_.name AS deviceModelName,
                   d.row_version AS rowVersion
            FROM dm.device d
            JOIN dm.device_model dm_ ON dm_.id = d.device_model_id
            WHERE d.id = @Id AND d.deleted_at_utc IS NULL;
            """;

        IEnumerable<Versioned<DeviceQueryResponse>> rows = await connection.QueryAsync<DeviceQueryResponse, Guid, Versioned<DeviceQueryResponse>>(
            queries,
            (device, rowVersion) => new Versioned<DeviceQueryResponse>(device, RowVersion.Create(rowVersion)),
            new { Id = Id.Value },
            splitOn: "rowVersion");

        return rows.SingleOrDefault();
    }

    public async Task<ListQueryResponse<GetDeviceListItem>> GetListAsync(
        ListQuery query, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        string queries =
            """
            SELECT count(*) FROM dm.device WHERE deleted_at_utc IS NULL;

            SELECT d.id, d.device_id AS deviceId, d.device_model_id AS deviceModelId, dm_.name AS deviceModelName
            FROM dm.device d
            JOIN dm.device_model dm_ ON dm_.id = d.device_model_id
            WHERE d.deleted_at_utc IS NULL
            ORDER BY d.id
            LIMIT @PageSize OFFSET @Offset;
            """;

        GridReader res = await connection.QueryMultipleAsync(
            queries, new { PageSize = query.Paggination.Size.Value, Offset = query.Paggination.Skip });

        int count = await res.ReadSingleAsync<int>();
        IReadOnlyCollection<GetDeviceListItem> list = [.. await res.ReadAsync<GetDeviceListItem>()];

        return new ListQueryResponse<GetDeviceListItem>(list, count);
    }
}
