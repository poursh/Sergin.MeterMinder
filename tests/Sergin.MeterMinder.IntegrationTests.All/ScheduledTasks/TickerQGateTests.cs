using System.Collections;
using System.Reflection;
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
using TickerQ.Utilities;
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
        string licence = RequiresTickerQLicenseFactAttribute.LicensePath!;
        await ResetSchemaAsync(licence);
        await using WebApplicationFactory<Program> host = CreateHost(licence);
        using HttpClient client = host.CreateClient();

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
        using WebApplicationFactory<Program> host = CreateHost(Path.Combine(Path.GetTempPath(), "no-such-licence.tqlicense"));

        // CreateClient builds and starts the host, so ValidateOnStart's failure surfaces here.
        OptionsValidationException failure = Assert.Throws<OptionsValidationException>(() => host.CreateClient());

        Assert.Contains(LicenseKey, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeFunction_IsRegistered_AndEngineActivatesWithoutLicence()
    {
        string licence = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tqlicense");
        await File.WriteAllTextAsync(licence, "not a real licence");
        try
        {
            await ResetSchemaAsync(licence);
            await using WebApplicationFactory<Program> host = CreateHost(licence);
            using HttpClient client = host.CreateClient();

            Assert.NotNull(host.Services.GetRequiredService<IHost>());

            // The source generator's module initializer, in this library, queues the descriptor in the provider's
            // static registry as soon as the assembly loads. The registry is built (frozen) only by UseTickerQ,
            // which needs a valid licence, so before that the only view is the provider's private queue.
            FieldInfo pending = typeof(TickerFunctionProvider).GetField("_pendingDescriptors", BindingFlags.NonPublic | BindingFlags.Static)!;
            string[] queued = [.. ((IEnumerable)pending.GetValue(null)!).Cast<object>()
                .Select(entry => entry.GetType().GetField("Item1")!.GetValue(entry)?.ToString() ?? string.Empty)];
            Assert.Contains(queued, name => name.StartsWith("Sergin.Probe", StringComparison.Ordinal));

            // Resolving the host and activating the engine against the real, migrated tables must not throw.
            Exception? activation = Record.Exception(() => host.Services.GetRequiredService<IHost>().UseTickerQ());
            Assert.Null(activation);
        }
        finally
        {
            File.Delete(licence);
        }
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

    /// <summary>
    /// Builds TickerQ's tables before the host starts: the engine's cluster-control service hits them during
    /// host start, so they cannot be created through the host's own services. A bare provider that only
    /// registers the engine's context never starts a hosted service.
    /// </summary>
    private async Task ResetSchemaAsync(string licensePath)
    {
        string connectionString = factory.Services.GetRequiredService<IConfiguration>()
            .GetSection("Sergin:ConnectionStrings")["Database"]
            ?? throw new InvalidOperationException("No Sergin:ConnectionStrings:Database.");
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [TickerQScheduledTaskOptions.SectionName + ":LicensePath"] = licensePath })
            .Build();

        await using ServiceProvider provider = new ServiceCollection()
            .AddSerginTickerQ(configuration, connectionString)
            .BuildServiceProvider();
        IDbContextFactory<TickerQDbContext> contexts = provider.GetRequiredService<IDbContextFactory<TickerQDbContext>>();
        await using TickerQDbContext context = await contexts.CreateDbContextAsync();

        await context.Database.ExecuteSqlRawAsync(ResetSchemaSql);
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }
}
