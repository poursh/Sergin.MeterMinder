using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// Direction and language (spec decision 10): the document's <c>lang</c>/<c>dir</c>, MudBlazor's right-to-left layout and
/// the app bar's language switcher, all prerendered for the culture the cookie names.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RightToLeftRenderingTests(SerginWebApiFactory<Program> factory)
{
    private const string CookieName = ".AspNetCore.Culture";

    [Fact]
    public async Task RtlCulture_PrerendersDirAndLang()
    {
        string html = await RenderAsync("fa-IR", [Language("fa-IR", "فارسی", rtl: true, published: true)]);

        Assert.Contains("<html lang=\"fa-IR\" dir=\"rtl\"", html, StringComparison.Ordinal);
        Assert.Contains("mud-application-layout-rtl", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/dm/devices?page=2")]
    [InlineData("/dm/devices?page=2&term=a%20b")]
    public async Task SwitchTarget_WithQueryString_RedirectsBack(string current)
    {
        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(_ => { });
        using HttpClient client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        using HttpResponseMessage response = await client.GetAsync(
            new Uri($"/culture/set?culture=en&redirectUri={Uri.EscapeDataString(current)}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(current, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task SwitcherStyles_AreLinkedAndServed()
    {
        string html = await RenderAsync(null, [Language("de", "Deutsch", rtl: false, published: true)]);

        Match link = Regex.Match(html, @"href=""(?<href>Sergin\.MeterMinder\.Hosts\.All\.[^""]*styles\.css)""");
        Assert.True(link.Success, "The page links no scoped-CSS bundle.");

        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri("/" + link.Groups["href"].Value, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The host's bundle imports each referenced RCL's own scoped bundle; the switcher's rule is in the RCL's.
        string css = await response.Content.ReadAsStringAsync();

        foreach (Match import in Regex.Matches(css, @"@import\s+'(?<path>[^']+)'"))
        {
            using HttpResponseMessage imported = await client.GetAsync(new Uri("/" + import.Groups["path"].Value, UriKind.Relative));
            css += await imported.Content.ReadAsStringAsync();
        }

        Assert.Contains(".sergin-language-select", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LtrCulture_Dir_Ltr()
    {
        string html = await RenderAsync(null, [Language("de", "Deutsch", rtl: false, published: true)]);

        Assert.Contains("<html lang=\"en\" dir=\"ltr\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("mud-application-layout-rtl", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectionOverride_Wins()
    {
        string html = await RenderAsync("fa-IR", [Language("fa-IR", "فارسی", rtl: false, published: true)]);

        Assert.Contains("<html lang=\"fa-IR\" dir=\"ltr\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("mud-application-layout-rtl", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switcher_HiddenWithOnlyEnglish()
    {
        string html = await RenderAsync(null, [Language("fr", "Francais", rtl: false, published: false)]);

        Assert.DoesNotContain("sergin-language-switcher", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switcher_ListsPublished_NotDrafts()
    {
        string html = await RenderAsync(
            null,
            [Language("de", "Deutsch", rtl: false, published: true), Language("fr", "Francais", rtl: false, published: false)]);

        Assert.Contains("sergin-language-switcher", html, StringComparison.Ordinal);
        Assert.Contains("Deutsch", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Francais", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switcher_ListsDrafts_WhenPreviewAllowed_MarkedAsDraft()
    {
        string html = await RenderAsync(null, [Language("fr", "Francais", rtl: false, published: false)], previewAllowed: true);

        Assert.Contains("Francais (draft)", html, StringComparison.Ordinal);
    }

    private static LanguageInfo Language(string culture, string name, bool rtl, bool published) => new(culture, name, rtl, published);

    private async Task<string> RenderAsync(string? cookieCulture, LanguageInfo[] languages, bool previewAllowed = false)
    {
        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
            services.AddSingleton<ITranslationCache>(new FixedLanguagesCache(languages));
            services.AddScoped<IDraftLanguagePreviewPolicy>(_ => new FixedPreviewPolicy(previewAllowed));
        }));

        using HttpClient client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/dm/devices", UriKind.Relative));

        if (cookieCulture is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={Uri.EscapeDataString($"c={cookieCulture}|uic={cookieCulture}")}");
        }

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private sealed class FixedPreviewPolicy(bool allowed) : IDraftLanguagePreviewPolicy
    {
        public Task<bool> CanPreviewAsync(CancellationToken cancellationToken) => Task.FromResult(allowed);
    }

    private sealed class FixedLanguagesCache(LanguageInfo[] languages) : ITranslationCache
    {
        public IReadOnlyList<LanguageInfo> Languages { get; private set; } = [];

        public Task EnsureLoadedAsync(string culture, CancellationToken cancellationToken)
        {
            Languages = languages;
            return Task.CompletedTask;
        }

        public Task RefreshAsync(string culture, CancellationToken cancellationToken) => Task.CompletedTask;

        public void InvalidateLanguages()
        {
        }

        public Task RefreshLanguagesAsync(CancellationToken cancellationToken)
        {
            Languages = languages;
            return Task.CompletedTask;
        }

        public bool TryGet(string culture, string key, [NotNullWhen(true)] out StoredTranslation? translation)
        {
            translation = null;
            return false;
        }
    }
}
