using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// The shell renders the development scope setter on every page. The test host runs in Development; outside it
/// the component renders nothing.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ScopeSetterRenderingTests(SerginWebApiFactory<Program> factory)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/dm/devices")]
    public async Task ScopeSetterButton_RendersInDevelopment(string path)
    {
        using HttpClient client = factory.CreateClient();

        string html = await client.GetStringAsync(new Uri(path, UriKind.Relative));

        Assert.Contains("aria-label=\"Set scope\"", html, StringComparison.Ordinal);
    }
}
