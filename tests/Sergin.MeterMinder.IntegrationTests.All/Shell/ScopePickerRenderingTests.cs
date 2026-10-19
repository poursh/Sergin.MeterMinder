using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// The shell renders the scope picker in the app bar on every page, already prerendered on the scope the session
/// settled. The development user holds a root grant, so the home scope is the root.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ScopePickerRenderingTests(SerginWebApiFactory<Program> factory)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/dm/devices")]
    public async Task ScopePicker_RendersTheCurrentScope(string path)
    {
        using HttpClient client = factory.CreateClient();

        string html = await client.GetStringAsync(new Uri(path, UriKind.Relative));

        Assert.Contains("aria-label=\"Scope: Root\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"Set scope\"", html, StringComparison.Ordinal);
    }
}
