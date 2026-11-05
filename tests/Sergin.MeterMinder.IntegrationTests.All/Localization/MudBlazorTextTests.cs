using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>MudBlazor's own texts go through the catalog (spec decision 18).</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class MudBlazorTextTests(SerginWebApiFactory<Program> factory)
{
    private const string CookieName = ".AspNetCore.Culture";
    private const string RowsPerPageKey = "common.mud-blazor.mud-data-grid-pager.rows-per-page";

    [Fact]
    public void Catalog_HoldsMudPagerText()
    {
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();

        Assert.True(catalog.TryGet(RowsPerPageKey, out TextDefinition? definition));
        Assert.Equal("Rows per page:", definition.Default);
        Assert.All(
            catalog.Entries.Where(entry => entry.DeclaringType.Name == "MudBlazorTexts"),
            entry => Assert.StartsWith("common.mud-blazor.", entry.Definition.Key.Value, StringComparison.Ordinal));
        Assert.Contains(catalog.Entries, entry => entry.Definition.Default == "Toggle {Value}");
    }

    [Fact]
    public async Task MudPager_RendersTranslation()
    {
        TestTranslationStore store = new();
        store.Set("de", RowsPerPageKey, "Zeilen pro Seite:", "Rows per page:");

        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddSingleton<ITranslationStore>(store)));
        using HttpClient client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/dm/devices", UriKind.Relative));
        request.Headers.Add("Cookie", $"{CookieName}={Uri.EscapeDataString("c=de|uic=de")}");

        using HttpResponseMessage response = await client.SendAsync(request);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Zeilen pro Seite:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Rows per page:", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MudPager_WithoutTranslation_RendersEnglish()
    {
        using HttpClient client = factory.CreateClient();

        string html = await client.GetStringAsync(new Uri("/dm/devices", UriKind.Relative));

        Assert.Contains("Rows per page:", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Adapter_FillsPositionalArguments_AndLeavesUnknownKeysToMudBlazor()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        MudBlazor.MudLocalizer localizer = scope.ServiceProvider.GetRequiredService<MudBlazor.MudLocalizer>();

        LocalizedString toggle = localizer["MudNavGroup_ToggleExpand", "Admin"];

        Assert.Equal("Toggle Admin", toggle.Value);
        Assert.True(localizer["NoSuchMudKey"].ResourceNotFound);
        Assert.True(localizer["Has1Digit_Key"].ResourceNotFound);
    }
}
