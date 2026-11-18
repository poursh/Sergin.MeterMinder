using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.ScheduledTasks.Commands;
using Sergin.SharedKernel.Infrastracture.Data;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>Reads a started background run back from its module's schema, polling until it is no longer Running.</summary>
internal static class BackgroundRuns
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public static async Task<(string Status, IReadOnlyList<(string Status, string? ErrorCode)> Items)> WaitAsync(
        IServiceProvider services, BackgroundRunRef run, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(run);

        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            using IServiceScope scope = services.CreateScope();
            using DbConnection connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnectionAsync();

            string? status = await ReadStatusAsync(connection, run);
            if (status is not null and not "Running")
            {
                return (status, await ReadItemsAsync(connection, run));
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Background run {run.Schema}/{run.RunId} was still {status ?? "missing"} after {timeout}.");
            }

            await Task.Delay(PollInterval);
        }
    }

    private static async Task<string?> ReadStatusAsync(DbConnection connection, BackgroundRunRef run)
    {
        await using DbCommand command = connection.CreateCommand();
#pragma warning disable CA2100, S2077 // the schema is a module constant held by the run reference, never user input
        command.CommandText = $"""SELECT status FROM "{run.Schema}".scheduled_task_runs WHERE id = @id""";
#pragma warning restore CA2100, S2077
        AddId(command, run.RunId);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<IReadOnlyList<(string Status, string? ErrorCode)>> ReadItemsAsync(DbConnection connection, BackgroundRunRef run)
    {
        await using DbCommand command = connection.CreateCommand();
#pragma warning disable CA2100, S2077
        command.CommandText = $"""SELECT status, error_code FROM "{run.Schema}".scheduled_task_run_items WHERE run_id = @id ORDER BY id""";
#pragma warning restore CA2100, S2077
        AddId(command, run.RunId);

        List<(string Status, string? ErrorCode)> items = [];
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add((reader.GetString(0), await reader.IsDBNullAsync(1) ? null : reader.GetString(1)));
        }

        return items;
    }

    private static void AddId(DbCommand command, Guid id)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = id;
        command.Parameters.Add(parameter);
    }
}
