using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Lists;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Localizations;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// The one formatter for every displayed date and number (spec decisions 11 and 12): Jalali calendar and Persian digits
/// under a Persian culture, the culture's own formats otherwise, Latin digits left alone in ids.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class CultureFormatterTests(SerginWebApiFactory<Program> factory)
{
    private static readonly DateTimeOffset Day = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Persian_Date_IsJalaliWithPersianDigits() =>
        Assert.Equal("۱۴۰۵/۰۸/۱۴", Under("fa-IR", f => f.Date(Day)));

    [Fact]
    public void Persian_Number_PersianDigits() =>
        Assert.Equal("۱٬۰۰۰", Under("fa-IR", f => f.Number(1000)));

    [Fact]
    public void Persian_Decimal_PersianDigitsAndSeparator() =>
        Assert.Equal("۱٬۲۳۴٫۵۰", Under("fa-IR", f => f.Number(1234.5m, 2)));

    [Fact]
    public void Persian_DateTime_AppliesTheZone() =>
        Assert.Equal("۱۴۰۵/۰۸/۱۴ ۰۳:۳۰", Under("fa-IR", f => f.DateTime(Day, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"))));

    [Fact]
    public void English_Date_Gregorian() =>
        Assert.Equal("11/5/2026", Under("en-US", f => f.Date(Day)));

    [Fact]
    public void English_DateTime_KeepsTheExistingShape() =>
        Assert.Equal("05 Nov 2026 00:00", Under("en-US", f => f.DateTime(Day)));

    [Fact]
    public void Persian_Culture_CalendarIsPersian() =>
        Assert.True(Under("fa-IR", f => f.Culture.DateTimeFormat.Calendar is System.Globalization.PersianCalendar));

    [Fact]
    public async Task DeviceId_KeepsLatinDigits()
    {
        string marker = CatalogSeed.Marker();
        string deviceId = $"MTR-{marker}-1";
        Guid id;

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
            ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
            DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
            id = await CatalogSeed.DeviceAsync(dispatcher, deviceId, model);
        }

        string list = await PersianPageAsync("/dm/devices");
        string detail = await PersianPageAsync($"/dm/devices/{id}");

        // The overview figures are counts, so they carry Persian digits (HTML-encoded as &#x6F0;..&#x6F9;).
        Assert.Matches("h5\">&#x6F[0-9]", list);
        // The id the page shows is data, untouched.
        Assert.Contains(deviceId, detail, StringComparison.Ordinal);
    }

    private async Task<string> PersianPageAsync(string path)
    {
        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<ITranslationCache>(new PersianCache())));
        using HttpClient client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add("Cookie", $".AspNetCore.Culture={Uri.EscapeDataString("c=fa-IR|uic=fa-IR")}");
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private T Under<T>(string culture, Func<ICultureFormatter, T> act)
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            using IServiceScope scope = factory.Services.CreateScope();
            return act(scope.ServiceProvider.GetRequiredService<ICultureFormatter>());
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    private sealed class PersianCache : ITranslationCache
    {
        private static readonly LanguageInfo[] Offered = [new("fa-IR", "Persian", true, true)];

        public IReadOnlyList<LanguageInfo> Languages => Offered;

        public Task EnsureLoadedAsync(string culture, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RefreshAsync(string culture, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RefreshLanguagesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void InvalidateLanguages()
        {
        }

        public bool TryGet(string culture, string key, [NotNullWhen(true)] out StoredTranslation? translation)
        {
            translation = null;
            return false;
        }
    }
}
