using System.Net;
using System.Text.RegularExpressions;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.UserAccess.Application.Users.Commands.Create;
using Sergin.UserAccess.Domain.Users;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// The breadcrumb strip every page declares through <c>SerginBreadcrumbs</c> and the shell renders above the
/// body. Prerendered HTML only, like <see cref="ModulePageRenderingTests"/>: the strip reaches the layout
/// through a Blazor section, and these prove that section is filled server-side — including after a detail
/// page's async load — and empty on the home slot.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed partial class BreadcrumbRenderingTests(SerginWebApiFactory<Program> factory)
{
    private const string UnseededId = "01920000-0000-7000-8000-000000000001";

    /// <summary>
    /// The strip alone. The drawer also links <c>/</c> and every section, so an assertion over the whole
    /// page would pass with no breadcrumbs at all.
    /// </summary>
    [GeneratedRegex("<nav aria-label=\"Breadcrumb\">.*?</nav>", RegexOptions.Singleline)]
    private static partial Regex BreadcrumbNav();

    [Theory]
    [InlineData("/dm/devices", "Devices")]
    [InlineData("/dm/manufacturers", "Manufacturers")]
    [InlineData("/ua/users", "Users")]
    public async Task ListPage_RendersHomeLink_ThenSectionAsCurrent(string path, string section)
    {
        string strip = await GetBreadcrumbStripAsync(path);

        Assert.Contains("<a href=\"/\">Home</a>", strip, StringComparison.Ordinal);
        Assert.Contains(Current(section), strip, StringComparison.Ordinal);
        Assert.DoesNotContain($"href=\"{path}\"", strip, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/dm/devices/new", "/dm/devices", "New device")]
    [InlineData("/dm/manufacturers/new", "/dm/manufacturers", "New manufacturer")]
    [InlineData("/ua/users/new", "/ua/users", "New user")]
    public async Task CreatePage_LinksToItsList_AndNamesItselfLast(string path, string listHref, string title)
    {
        string strip = await GetBreadcrumbStripAsync(path);

        Assert.Contains($"href=\"{listHref}\"", strip, StringComparison.Ordinal);
        Assert.Contains(Current(title), strip, StringComparison.Ordinal);
    }

    /// <summary>
    /// The manufacturer step links by the route parameter, so it is a link even when the manufacturer does
    /// not exist and the name stays at its placeholder.
    /// </summary>
    [Fact]
    public async Task NestedCreatePage_LinksToItsManufacturer()
    {
        string strip = await GetBreadcrumbStripAsync($"/dm/manufacturers/{UnseededId}/models/new");

        Assert.Contains("href=\"/dm/manufacturers\"", strip, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"/dm/manufacturers/{UnseededId}\">Manufacturer</a>", strip, StringComparison.Ordinal);
        Assert.Contains(Current("New device model"), strip, StringComparison.Ordinal);
    }

    /// <summary>
    /// A detail page's tail is filled from the loaded record, and prerendering awaits that load — so the
    /// name is in the first HTML, not only after the circuit connects.
    /// </summary>
    [Fact]
    public async Task DetailPage_NamesTheLoadedRecordLast()
    {
        // ISerginDispatcher is scoped (it carries the caller's IUserContext), so it comes from a scope.
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher sender = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        string userName = $"breadcrumb-test-{Guid.CreateVersion7()}";

        ErrorOr<CreateUserCommandResponse> created =
            await sender.SendAsync(new CreateUserCommand(new UserName(userName)));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        string strip = await GetBreadcrumbStripAsync($"/ua/users/{created.Value.Id}");

        Assert.Contains("href=\"/ua/users\"", strip, StringComparison.Ordinal);
        Assert.Contains(Current(userName), strip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetailPage_NotFound_KeepsThePlaceholderTail()
    {
        string strip = await GetBreadcrumbStripAsync($"/dm/devices/{UnseededId}");

        Assert.Contains("href=\"/dm/devices\"", strip, StringComparison.Ordinal);
        Assert.Contains(Current("Device"), strip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetailPage_HasNoBackButton()
    {
        HttpClient client = factory.CreateClient();

        string html = await client.GetStringAsync($"/ua/users/{UnseededId}");

        Assert.DoesNotContain("Back to users", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomePage_RendersNoBreadcrumbs()
    {
        HttpClient client = factory.CreateClient();

        string html = await client.GetStringAsync("/");

        Assert.DoesNotMatch(BreadcrumbNav(), html);
    }

    private async Task<string> GetBreadcrumbStripAsync(string path)
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();

        Match nav = BreadcrumbNav().Match(html);

        Assert.True(nav.Success, $"{path} rendered no breadcrumb strip.");

        return nav.Value;
    }

    /// <summary>
    /// How MudBlazor renders the current page: the item is disabled and its href is <c>#</c>, because
    /// <c>SerginBreadcrumbs</c> drops the last step's href rather than only disabling it.
    /// </summary>
    private static string Current(string label)
        => $"<li class=\"mud-breadcrumb-item mud-disabled\"><a href=\"#\">{label}</a></li>";
}
