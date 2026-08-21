using System.Data.Common;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Infrastracture.Data;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Manufacturers.Repositories.Queries;

internal sealed class ManufacturerQueryRepository(
    IDbConnectionFactory connectionFactory) : IManufacturerAllQueryRepository
{
    public async Task<ManufacturerQueryResponse?> GetManufacturerById(
        ManufacturerId id, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        string queries =
           """
            SELECT id, name, address
            FROM dm.manufacturer
            WHERE id = @Id;
            """;

        return await connection.QuerySingleOrDefaultAsync<ManufacturerQueryResponse>(
            queries, new { Id = id.Value });
    }

    public async Task<ListQueryResponse<GetManufacturerListItem>> GetListAsync(
        ListQuery<GetManufacturerListItem> query, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        string queries =
            """
            SELECT count(*) FROM dm.manufacturer;

            SELECT id, name, address
            FROM dm.manufacturer
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
