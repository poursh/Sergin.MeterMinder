using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Infrastracture.Data;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Authorization;

/// <summary>
/// The rows <c>ua</c>'s one migration seeds: the scope tree's root and the two built-in roles with their permissions.
/// Pinned against the baseline taken before the migrations were collapsed into one, so the reset changed no seeded row.
/// Other tests add roles and scopes of their own, so each query names the seeded rows; the role query also catches a
/// second live role with a built-in name.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class UserAccessSeedTests(SerginWebApiFactory<Program> factory)
{
    private static readonly Guid RootId = new("01920000-0000-7000-8000-0000000000b1");
    private static readonly Guid AdministratorRoleId = new("01920000-0000-7000-8000-0000000000a1");
    private static readonly Guid ViewerRoleId = new("01920000-0000-7000-8000-0000000000a2");

    [Fact]
    public async Task Seed_HoldsTheBaselineRolesAndRoot()
    {
        IReadOnlyList<string> scopes = await RowsAsync(
            """
            SELECT id::text || '|' || name || '|' || coalesce(parent_id::text, '-') || '|' || path::text
            FROM ua.scopes
            WHERE id = @Root OR (parent_id IS NULL AND deleted_at_utc IS NULL)
            ORDER BY 1;
            """);
        IReadOnlyList<string> roles = await RowsAsync(
            """
            SELECT id::text || '|' || name || '|' || owner_scope_id::text || '|' || is_inheritable::text
            FROM ua.roles
            WHERE id IN (@Administrator, @Viewer) OR (name IN ('administrator', 'viewer') AND deleted_at_utc IS NULL)
            ORDER BY 1;
            """);
        IReadOnlyList<string> permissions = await RowsAsync(
            """
            SELECT role_id::text || '|' || permission
            FROM ua.role_permissions
            WHERE role_id IN (@Administrator, @Viewer)
            ORDER BY 1;
            """);

        Assert.Equal(
            ["01920000-0000-7000-8000-0000000000b1|Root|-|019200000000700080000000000000b1"],
            scopes);
        Assert.Equal(
            [
                "01920000-0000-7000-8000-0000000000a1|administrator|01920000-0000-7000-8000-0000000000b1|true",
                "01920000-0000-7000-8000-0000000000a2|viewer|01920000-0000-7000-8000-0000000000b1|true",
            ],
            roles);
        Assert.Equal(
            [
                "01920000-0000-7000-8000-0000000000a1|permission.sys.platform-all",
                "01920000-0000-7000-8000-0000000000a2|permission.dm.devices.read",
                "01920000-0000-7000-8000-0000000000a2|permission.dm.manufacturers.read",
                "01920000-0000-7000-8000-0000000000a2|permission.ua.users.read",
            ],
            permissions);
    }

    private async Task<IReadOnlyList<string>> RowsAsync(string sql)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        using DbConnection connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnectionAsync();
        await using DbCommand command = connection.CreateCommand();

        // Every statement is a literal in this class; the ids go in as parameters.
#pragma warning disable CA2100
        command.CommandText = sql;
#pragma warning restore CA2100

        AddParameter(command, "Root", RootId);
        AddParameter(command, "Administrator", AdministratorRoleId);
        AddParameter(command, "Viewer", ViewerRoleId);

        List<string> rows = [];
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static void AddParameter(DbCommand command, string name, Guid value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
