using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// An unrecognized <c>Sergin:TimeZone</c> id should stop the host at startup naming the key, not surface
/// the first time a page tries to convert a UTC value for display.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TimeZoneOptionsValidationTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void UnknownTimeZoneId_FailsStartupNamingTheKey()
    {
        WebApplicationFactory<Program> misconfigured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sergin:TimeZone", "Not/A_RealZone");
        });

        // CreateClient is what builds and starts the host, so the failure surfaces here.
        OptionsValidationException failure =
            Assert.Throws<OptionsValidationException>(() => misconfigured.CreateClient());

        Assert.Contains("Sergin:TimeZone", failure.Message, StringComparison.Ordinal);
    }
}
