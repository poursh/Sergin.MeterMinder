using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Localizations;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// Culture selection (spec decisions 9 and 25): the culture cookie, the <c>/culture/set</c> switch, and the per-caller
/// gate that keeps draft languages from everyone the preview policy refuses. A page "renders in a culture" when every
/// text the prerender looks up is looked up under that culture, which a recording localizer observes.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class CultureSelectionTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string CookieName = ".AspNetCore.Culture";

    private readonly TestTranslationStore store = new();
    private readonly ConcurrentQueue<string> lookupCultures = new();
    private readonly ConcurrentQueue<LogLevel> logLevels = new();

    private bool previewAllowed;

    private WebApplicationFactory<Program> host = default!;

    public Task InitializeAsync()
    {
        store.AddLanguage("de", published: true);
        store.AddLanguage("fr", published: false);

        host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ITranslationStore>(store);
            services.AddScoped<IDraftLanguagePreviewPolicy>(_ => new FixedPreviewPolicy(previewAllowed));
            services.AddSingleton<IStringLocalizer>(sp => new CultureRecordingLocalizer(
                sp.GetRequiredService<IStringLocalizerFactory>().Create(typeof(TextCatalog)), lookupCultures));
            services.AddSingleton<ILoggerProvider>(new RecordingLoggerProvider(logLevels));
        }));

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task SetCulture_WritesCookieAndRedirects()
    {
        using HttpResponseMessage response = await Client().GetAsync(new Uri("/culture/set?culture=de&redirectUri=/dm/devices", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/dm/devices", response.Headers.Location?.OriginalString);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.Contains($"{CookieName}=c%3Dde%7Cuic%3Dde", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetCulture_UnknownCulture_WritesNothingAndRedirects()
    {
        using HttpResponseMessage response = await Client().GetAsync(new Uri("/culture/set?culture=xx-unknown&redirectUri=/dm/devices", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/dm/devices", response.Headers.Location?.OriginalString);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    public async Task SetCulture_RejectsOffsiteRedirect(string target)
    {
        using HttpResponseMessage response = await Client().GetAsync(
            new Uri($"/culture/set?culture=de&redirectUri={Uri.EscapeDataString(target)}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Uri location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.False(location.IsAbsoluteUri, $"Redirected to {location}.");
        Assert.DoesNotContain("evil.example", location.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishedCookie_RendersInThatCulture()
    {
        IReadOnlySet<string> cultures = await RenderHomeAsync("de");

        Assert.Equal(["de"], cultures);
    }

    [Fact]
    public async Task NoCookie_RendersInTheDefaultCulture()
    {
        IReadOnlySet<string> cultures = await RenderHomeAsync(cookieCulture: null);

        Assert.Equal(["en"], cultures);
    }

    [Fact]
    public async Task DeletedLanguageCookie_FallsBackQuietly()
    {
        IReadOnlySet<string> cultures = await RenderHomeAsync("xx-unknown");

        Assert.Equal(["en"], cultures);
        Assert.DoesNotContain(logLevels, entry => entry >= LogLevel.Error);
    }

    [Fact]
    public async Task DraftCookie_WithoutPreview_FallsBack()
    {
        previewAllowed = false;

        IReadOnlySet<string> cultures = await RenderHomeAsync("fr");

        Assert.Equal(["en"], cultures);
    }

    [Fact]
    public async Task DraftCookie_WithPreview_Honoured()
    {
        previewAllowed = true;

        IReadOnlySet<string> cultures = await RenderHomeAsync("fr");

        Assert.Equal(["fr"], cultures);
    }

    [Fact]
    public void DefaultCulture_Invalid_FailsStart()
    {
        WebApplicationFactory<Program> misconfigured = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Sergin:Localization:DefaultCulture", "zz-ZZ-bad"));

        OptionsValidationException failure = Assert.Throws<OptionsValidationException>(() => misconfigured.CreateClient());

        Assert.Contains("Sergin:Localization:DefaultCulture", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultCulture_DefaultsToEnglish()
    {
        Assert.Equal("en", host.Services.GetRequiredService<IOptions<SerginLocalizationOptions>>().Value.DefaultCulture);
    }

    /// <summary>The unhosted Web API registration: Accept-Language, restricted to published languages.</summary>
    [Fact]
    public async Task WebApi_AcceptLanguage_HonoursPublishedOnly()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ITranslationCache>(new FixedLanguagesCache(
            new LanguageInfo("de", "Deutsch", IsRightToLeft: false, IsPublished: true),
            new LanguageInfo("fr", "Français", IsRightToLeft: false, IsPublished: false)));
        builder.Services.AddOptions<SerginLocalizationOptions>();

        await using WebApplication app = builder.Build();
        app.UseSerginApiRequestLocalization();
        app.MapGet("/culture", () => CultureInfo.CurrentUICulture.Name + "|" + CultureInfo.CurrentCulture.Name);
        await app.StartAsync();

        using HttpClient client = app.GetTestClient();

        Assert.Equal("de|de", await GetCultureAsync(client, "fr, de;q=0.8"));
        Assert.Equal("en|en", await GetCultureAsync(client, "fr"));
        Assert.Equal("en|en", await GetCultureAsync(client, null));

        static async Task<string> GetCultureAsync(HttpClient client, string? acceptLanguage)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/culture", UriKind.Relative));

            if (acceptLanguage is not null)
            {
                request.Headers.Add("Accept-Language", acceptLanguage);
            }

            using HttpResponseMessage response = await client.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }
    }

    private HttpClient Client() =>
        host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    /// <summary>Prerenders <c>/</c> with the culture cookie (or none) and answers every culture a text was looked up in.</summary>
    private async Task<IReadOnlySet<string>> RenderHomeAsync(string? cookieCulture)
    {
        using HttpClient client = Client();
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/", UriKind.Relative));

        if (cookieCulture is not null)
        {
            // Written by hand rather than through MakeCookieValue, whose RequestCulture refuses a name the runtime cannot
            // build, which is exactly what a stale cookie for a deleted language may hold.
            request.Headers.Add("Cookie", $"{CookieName}={Uri.EscapeDataString($"c={cookieCulture}|uic={cookieCulture}")}");
        }

        lookupCultures.Clear();

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(lookupCultures.IsEmpty, "The prerender looked no text up.");
        return lookupCultures.ToHashSet(StringComparer.Ordinal);
    }

    private sealed class FixedPreviewPolicy(bool allowed) : IDraftLanguagePreviewPolicy
    {
        public Task<bool> CanPreviewAsync(CancellationToken cancellationToken) => Task.FromResult(allowed);
    }

    private sealed class CultureRecordingLocalizer(IStringLocalizer inner, ConcurrentQueue<string> cultures) : IStringLocalizer
    {
        public LocalizedString this[string name]
        {
            get
            {
                cultures.Enqueue(CultureInfo.CurrentUICulture.Name);
                return inner[name];
            }
        }

        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                cultures.Enqueue(CultureInfo.CurrentUICulture.Name);
                return inner[name, arguments];
            }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => inner.GetAllStrings(includeParentCultures);
    }

    private sealed class FixedLanguagesCache(params LanguageInfo[] languages) : ITranslationCache
    {
        public IReadOnlyList<LanguageInfo> Languages { get; private set; } = [];

        public Task EnsureLoadedAsync(string culture, CancellationToken cancellationToken)
        {
            Languages = languages;
            return Task.CompletedTask;
        }

        public Task RefreshAsync(string culture, CancellationToken cancellationToken) => Task.CompletedTask;

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

    private sealed class RecordingLoggerProvider(ConcurrentQueue<LogLevel> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(entries);

        public void Dispose()
        {
            // Nothing to release.
        }

        private sealed class RecordingLogger(ConcurrentQueue<LogLevel> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(logLevel);
        }
    }
}
