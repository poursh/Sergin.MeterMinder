using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sergin.SharedKernel.Infrastructure.ScheduledTasks.TickerQ;
using Sergin.SharedKernel.IntegrationTests;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// The two things the TickerQ docs left open: a function declared in a referenced library runs, and the
/// licence comes from configuration and its absence stops the host.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TickerQGateTests(SerginWebApiFactory<Program> factory)
{
    private const string LicenseKey = "Sergin:ScheduledTasks:TickerQ:LicensePath";

    private const string ResetSchemaSql = "DROP SCHEMA IF EXISTS tickerq CASCADE; CREATE SCHEMA tickerq;";

    [RequiresTickerQLicenseFact]
    public async Task FunctionInReferencedAssembly_Runs()
    {
        WebApplicationFactory<Program> host = CreateHost(RequiresTickerQLicenseFactAttribute.LicensePath!);
        using HttpClient client = host.CreateClient();

        await ResetSchemaAsync(host);
        host.Services.GetRequiredService<IHost>().UseTickerQ();

        int before = ProbeFunctions.Calls;
        ITimeTickerManager<TimeTickerEntity> manager = host.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        TickerResult<TimeTickerEntity> result = await manager.AddAsync(new TimeTickerEntity { Function = "Sergin.Probe", ExecutionTime = DateTime.UtcNow });
        Assert.True(result.IsSucceeded);

        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (ProbeFunctions.Calls == before && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
        }

        Assert.Equal(before + 1, ProbeFunctions.Calls);
    }

    [Fact]
    public void MissingLicenseFile_FailsStart()
    {
        WebApplicationFactory<Program> host = CreateHost(Path.Combine(Path.GetTempPath(), "no-such-licence.tqlicense"));

        // CreateClient builds and starts the host, so ValidateOnStart's failure surfaces here.
        OptionsValidationException failure = Assert.Throws<OptionsValidationException>(() => host.CreateClient());

        Assert.Contains(LicenseKey, failure.Message, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateHost(string licensePath)
        => factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(LicenseKey, licensePath);
            builder.ConfigureServices((context, services) =>
            {
                IConfigurationSection sergin = context.Configuration.GetSection("Sergin");
                string connectionString = sergin.GetSection("ConnectionStrings")["Database"]
                    ?? throw new InvalidOperationException("No Sergin:ConnectionStrings:Database.");
                services.AddSerginTickerQ(sergin, connectionString);
            });
        });

    private static async Task ResetSchemaAsync(WebApplicationFactory<Program> host)
    {
        using IServiceScope scope = host.Services.CreateScope();
        IDbContextFactory<TickerQDbContext> contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TickerQDbContext>>();
        await using TickerQDbContext context = await contexts.CreateDbContextAsync();

        await context.Database.ExecuteSqlRawAsync(ResetSchemaSql);
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }
}
