using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Infrastructure.ScheduledTasks.TickerQ;
using TickerQ.EntityFrameworkCore.DbContextFactory;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// TickerQ's own tables for a test host, and a bare engine for the checks that need no scheduler. The tables are built
/// before the host starts: the engine's cluster-control service reads them during host start, so they cannot be created
/// through the host's own services. A bare provider that only registers the engine never starts a hosted service.
/// </summary>
internal static class TickerQTestStore
{
    public const string LicenseKey = "Sergin:ScheduledTasks:TickerQ:LicensePath";

    private const string ResetSchemaSql = "DROP SCHEMA IF EXISTS tickerq CASCADE; CREATE SCHEMA tickerq;";

    public static string ConnectionString(IServiceProvider services) =>
        services.GetRequiredService<IConfiguration>().GetSection("Sergin:ConnectionStrings")["Database"]
            ?? throw new InvalidOperationException("No Sergin:ConnectionStrings:Database.");

    /// <summary>The engine's services over the database, without a host: nothing starts, nothing is activated.</summary>
    public static ServiceProvider BareProvider(string connectionString, string licensePath)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [TickerQScheduledTaskOptions.SectionName + ":LicensePath"] = licensePath })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddSerginTickerQ(configuration, connectionString)
            .BuildServiceProvider();
    }

    public static async Task ResetSchemaAsync(string connectionString, string licensePath)
    {
        await using ServiceProvider provider = BareProvider(connectionString, licensePath);
        IDbContextFactory<TickerQDbContext> contexts = provider.GetRequiredService<IDbContextFactory<TickerQDbContext>>();
        await using TickerQDbContext context = await contexts.CreateDbContextAsync();

        await context.Database.ExecuteSqlRawAsync(ResetSchemaSql);
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }
}
