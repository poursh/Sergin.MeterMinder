using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Authentication;

/// <summary>
/// A half-configured realm should stop the host at startup naming the key that is wrong, not surface as
/// a failed redirect the first time somebody tries to sign in.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class AuthOptionsValidationTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void KeycloakMode_WithNoClientId_FailsStartupNamingTheKey()
    {
        WebApplicationFactory<Program> misconfigured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sergin:Auth:Mode", "Keycloak");
            builder.UseSetting("Sergin:Auth:Authority", "http://localhost:8080/realms/sergin");
            builder.UseSetting("Sergin:Auth:ClientId", string.Empty);
            builder.UseSetting("Sergin:Auth:ClientSecret", "secret");
        });

        // CreateClient is what builds and starts the host, so the failure surfaces here.
        OptionsValidationException failure =
            Assert.Throws<OptionsValidationException>(() => misconfigured.CreateClient());

        Assert.Contains("Sergin:Auth:ClientId", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DevUserMode_OutsideDevelopment_RefusesToStart()
    {
        WebApplicationFactory<Program> production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Sergin:Auth:Mode", "DevUser");
        });

        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(() => production.CreateClient());

        Assert.Contains("DevUser", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Production", failure.Message, StringComparison.Ordinal);
    }
}
