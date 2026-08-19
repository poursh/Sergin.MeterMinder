using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

[Collection(nameof(IntegrationTestCollection))]
public sealed partial class ModulePageRenderingTests(SerginWebApiFactory<Program> factory)
{
    /// <summary>
    /// The one anchor in the shell whose href is the site root — the home nav entry. Matching on
    /// <c>&lt;a</c> rather than the href alone keeps it off <c>App.razor</c>'s <c>&lt;base href="/" /&gt;</c>.
    /// </summary>
    [GeneratedRegex("<a[^>]*href=\"/\"[^>]*>")]
    private static partial Regex HomeNavAnchor();

    [Theory]
    [InlineData("/")]
    [InlineData("/mm/devices")]
    [InlineData("/ua/users")]
    [InlineData("/mm/devices/new")]
    [InlineData("/ua/users/new")]
    public async Task Page_RendersServerSide_WithNavFromBothModules(string path)
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();

        // Both modules contributed nav entries, so the shell composed them.
        Assert.Contains("/mm/devices", html, StringComparison.Ordinal);
        Assert.Contains("/ua/users", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/mm/devices")]
    [InlineData("/ua/users")]
    public async Task ModulePage_IsInteractive_NotStaticallyRenderedOnly(string path)
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();

        // The prerenderer emits a <!--Blazor:{"type":"server",...}--> marker per interactive component.
        // Without an @rendermode on <Routes>, the whole app silently falls back to static SSR: every page
        // still returns 200 with markup, but OnAfterRenderAsync never runs, so MudTable's ServerData
        // callback is never invoked and every grid renders permanently empty. Asserting 200 alone
        // cannot see that, which is exactly how it reached review once already.
        Assert.Contains("\"type\":\"server\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomePage_RendersConfiguredHomeComponent_AndHomeNavEntry()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/");

        // 200 is the whole point of this test. Before the home slot existed, every @page in the solution
        // was module-owned and schema-prefixed, so "/" matched no mapped Razor component endpoint and
        // routing returned an empty 404 — Routes.razor's <NotFound> fragment never ran, because the
        // request was rejected before Blazor saw it.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();

        // Program.cs fills the slot with MeterMinderHome. Asserting on its own copy rather than the
        // shared prompt keeps this a test of the seam: SharedKernel's SerginWelcome fallback would
        // satisfy a more generic assertion without the host's registration ever being honoured.
        Assert.Contains(
            "A head-end system for smart electricity, gas, and water meters.", html, StringComparison.Ordinal);

        // The shell composed its own home entry alongside the modules'.
        Assert.Matches(HomeNavAnchor(), html);
    }

    [Fact]
    public async Task HomeNavLink_IsNotActive_OnAModulePage()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/mm/devices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();

        Match homeLink = HomeNavAnchor().Match(html);

        Assert.True(homeLink.Success, "The home nav entry should render on every page.");

        // NavLink's prefix matching treats any href ending in a non-alphanumeric character as a prefix of
        // everything below it, so a root href under the menu's default Match would light up on every page
        // in the app. SerginNavMenu switches the root to NavLinkMatch.All; this is what proves it.
        Assert.DoesNotContain("active", homeLink.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shell_RendersConfiguredApplicationName_InAppBar()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/mm/devices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();

        // Read the expected value out of the host's own configuration rather than hard-coding it, so
        // renaming the application in appsettings.json doesn't break this test. The app bar hard-coded
        // "Sergin" before this was configurable, so finding the *configured* value proves the whole chain:
        // the option bound off the Sergin section, survived validation, and reached SerginMainLayout.
        string configuredName = factory.Services
            .GetRequiredService<IOptions<SerginApplicationOptions>>().Value.ApplicationName;

        Assert.Contains(configuredName, html, StringComparison.Ordinal);

        // SerginMainLayout also supplies a default <PageTitle>, but it must stay a *fallback* — a page that
        // declares its own title still wins, because HeadOutlet keeps the last one rendered.
        Assert.Contains("<title>Devices</title>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserListPage_AndCreateUserPage_BothRender()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage listResponse = await client.GetAsync("/ua/users");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        HttpResponseMessage createResponse = await client.GetAsync("/ua/users/new");

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.Contains("User name", await createResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
