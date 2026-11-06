using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// The shell's own texts, the error titles and the permission labels are declared texts: a translation reaches the
/// page, and the catalog holds each one under its key.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ShellTextTests(SerginWebApiFactory<Program> factory)
{
    private const string CookieName = ".AspNetCore.Culture";
    private const string ReloadKey = "common.actions.reload";
    private const string HomeKey = "common.navigation.home";
    private const string NotFoundTitleKey = "common.errors.not-found.title";

    [Fact]
    public async Task Shell_RendersTranslatedChrome()
    {
        TestTranslationStore store = new();
        store.Set("de", ReloadKey, "Neu laden", "Reload");
        store.Set("de", HomeKey, "Startseite", "Home");

        string html = await GetAsync(store, "/dm/devices");

        Assert.Contains("Neu laden", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Startseite\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"Reload\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotFound_TitleTranslated()
    {
        TestTranslationStore store = new();
        store.Set("de", NotFoundTitleKey, "Nicht gefunden", "Not found");

        string html = await GetAsync(store, $"/dm/devices/{Guid.CreateVersion7()}");

        Assert.Contains("Nicht gefunden", html, StringComparison.Ordinal);
    }

    [Fact]
    public void PermissionLabel_IsCatalogText()
    {
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();

        Assert.True(catalog.TryGet("device-management.devices.permissions.read", out TextDefinition? definition));
        Assert.Equal(DevicePermissions.Read.Label, definition.Default);
    }

    [Fact]
    public async Task Shell_WithoutTranslation_RendersEnglish()
    {
        using HttpClient client = factory.CreateClient();

        string list = await client.GetStringAsync(new Uri("/dm/devices", UriKind.Relative));
        string missing = await client.GetStringAsync(new Uri($"/dm/devices/{Guid.CreateVersion7()}", UriKind.Relative));

        Assert.Contains("aria-label=\"Reload\"", list, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Home\"", list, StringComparison.Ordinal);
        Assert.Contains("Not found", missing, StringComparison.Ordinal);
        Assert.DoesNotContain("General.NotFound", missing, StringComparison.Ordinal);
    }

    private async Task<string> GetAsync(TestTranslationStore store, string path)
    {
        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddSingleton<ITranslationStore>(store)));
        using HttpClient client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add("Cookie", $"{CookieName}={Uri.EscapeDataString("c=de|uic=de")}");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }
}
