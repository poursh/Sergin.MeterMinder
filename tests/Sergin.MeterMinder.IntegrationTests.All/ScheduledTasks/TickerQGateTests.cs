using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sergin.SharedKernel.Infrastructure.ScheduledTasks.TickerQ;
using Sergin.SharedKernel.IntegrationTests;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Models;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// The two things the TickerQ docs left open: the functions declared in the adapter library are registered with the
/// engine, and the licence comes from configuration and its absence stops the host. That a registered function actually
/// runs is <see cref="TickerQEngineTests"/>' end-to-end proof, which needs a licence.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TickerQGateTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void MissingLicenseFile_FailsStart()
    {
        using WebApplicationFactory<Program> host = CreateHost(Path.Combine(Path.GetTempPath(), "no-such-licence.tqlicense"));

        // CreateClient builds and starts the host, so ValidateOnStart's failure surfaces here.
        OptionsValidationException failure = Assert.Throws<OptionsValidationException>(() => host.CreateClient());

        Assert.Contains(TickerQTestStore.LicenseKey, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SerginFunctions_AreRegistered_AndEngineActivatesWithoutLicence()
    {
        string licence = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tqlicense");
        await File.WriteAllTextAsync(licence, "not a real licence");
        try
        {
            await TickerQTestStore.ResetSchemaAsync(TickerQTestStore.ConnectionString(factory.Services), licence);
            await using WebApplicationFactory<Program> host = CreateHost(licence);
            using HttpClient client = host.CreateClient();

            // Resolving the host and activating the engine against the real tables must not throw.
            Exception? activation = Record.Exception(() => host.Services.GetRequiredService<IHost>().UseTickerQ());
            Assert.Null(activation);

            string[] registered = RegisteredFunctionNames();
            Assert.Contains("Sergin.ScheduledTasks.Fire", registered);
            Assert.Contains("Sergin.ScheduledTasks.Execute", registered);
        }
        finally
        {
            File.Delete(licence);
        }
    }

    /// <summary>
    /// The source generator's module initializer, in the adapter library, queues each descriptor in the provider's static
    /// registry as soon as the assembly loads. Building the registry (UseTickerQ, which a licensed test in this process may
    /// already have done) moves them into the public descriptors; before that the only view is the provider's private
    /// queue. Both are read, so the answer does not depend on which test ran first.
    /// </summary>
    private static string[] RegisteredFunctionNames()
    {
        _ = typeof(TickerQScheduledTaskEngine).Assembly;
        FieldInfo pending = typeof(TickerFunctionProvider).GetField("_pendingDescriptors", BindingFlags.NonPublic | BindingFlags.Static)!;
        IEnumerable<string> queued = ((IEnumerable)pending.GetValue(null)!).Cast<object>()
            .Select(entry => entry.GetType().GetField("Item1")!.GetValue(entry))
            .OfType<TickerFunctionDescriptor>()
            .Select(descriptor => descriptor.FunctionName);

        return [.. queued.Concat(TickerFunctionProvider.TickerFunctionDescriptors.Keys).Distinct()];
    }

    private WebApplicationFactory<Program> CreateHost(string licensePath)
        => factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(TickerQTestStore.LicenseKey, licensePath);
            builder.ConfigureServices((context, services) =>
            {
                IConfigurationSection sergin = context.Configuration.GetSection("Sergin");
                string connectionString = sergin.GetSection("ConnectionStrings")["Database"]
                    ?? throw new InvalidOperationException("No Sergin:ConnectionStrings:Database.");
                services.AddSerginTickerQ(sergin, connectionString);
            });
        });
}
