using System.Data.Common;
using Sergin.MeterMinder.DeviceManagement.Application.Devices;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Infrastracture.Data;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Devices.Repositories.Queries;

internal sealed class DeviceQueryRepository(
    IDbConnectionFactory connectionFactory) : IDeviceAllQueryRepositoriy
{
    public async Task<DeviceQueryResponse?> GetDeviceById(
        DeviceIntenralId Id, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        string queries =
           """
            SELECT id, device_id AS deviceId, manufacturer_id AS manufacturerId
            FROM dm.device
            WHERE id = @Id;
            """;

        return await connection.QuerySingleOrDefaultAsync<DeviceQueryResponse>(
            queries, new { Id = Id.Value });
    }

    public async Task<ListQueryResponse<GetDeviceListItem>> GetListAsync(
        ListQuery query, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        string queries =
            """
            SELECT count(*) FROM dm.device;

            SELECT id, device_id AS deviceId, manufacturer_id AS manufacturerId
            FROM dm.device
            ORDER BY id
            LIMIT @PageSize OFFSET @Offset;
            """;

        GridReader res = await connection.QueryMultipleAsync(
            queries, new { PageSize = query.Paggination.Size.Value, Offset = query.Paggination.Skip });

        int count = await res.ReadSingleAsync<int>();
        IReadOnlyCollection<GetDeviceListItem> list = [.. await res.ReadAsync<GetDeviceListItem>()];

        return new ListQueryResponse<GetDeviceListItem>(list, count);
    }
}
