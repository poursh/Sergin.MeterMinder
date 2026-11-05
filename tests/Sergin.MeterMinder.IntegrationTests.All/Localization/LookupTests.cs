using System.Globalization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// The string localizer's lookup order (spec decision 8): the culture's translation when its placeholders match the
/// default's, then the parent culture's under the same rule, then the catalog default, then — for a key the catalog
/// does not know — the key, marked not found. Each test builds its own host, so the fake store and the singleton
/// translation cache never leak between tests.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class LookupTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private readonly FakeTranslationStore store = new();

    private WebApplicationFactory<Program> lookupFactory = default!;

    public Task InitializeAsync()
    {
        lookupFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<ITranslationStore>(store);
            services.AddSingleton(TextSource.FromTypes("DeviceManagement", typeof(LookupTexts)));
        }));

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await lookupFactory.DisposeAsync();
    }

    [Fact]
    public void NoTranslation_ShowsDefault()
    {
        using CultureScope culture = new("de-DE");

        Assert.Equal("Save", Localizer.Text(CommonTexts.Save));
    }

    [Fact]
    public async Task Translation_Shown()
    {
        store.Set("de", CommonTexts.Save.Key.Value, "Speichern", CommonTexts.Save.Default);
        await Cache.EnsureLoadedAsync("de", CancellationToken.None);

        using CultureScope culture = new("de");

        Assert.Equal("Speichern", Localizer.Text(CommonTexts.Save));
    }

    [Fact]
    public async Task ParentCulture_Fallback()
    {
        store.Set("fa", CommonTexts.Save.Key.Value, "ذخیره", CommonTexts.Save.Default);
        await Cache.EnsureLoadedAsync("fa", CancellationToken.None);

        using CultureScope culture = new("fa-IR");

        Assert.Equal("ذخیره", Localizer.Text(CommonTexts.Save));
    }

    [Fact]
    public async Task NeedsReview_StillShown()
    {
        store.Set("de", LookupTexts.DeleteConfirm.Key.Value, "{DeviceId} löschen?", "Remove {DeviceId}?");
        await Cache.EnsureLoadedAsync("de", CancellationToken.None);

        using CultureScope culture = new("de");

        Assert.Equal("MTR-1 löschen?", Localizer.Text(LookupTexts.DeleteConfirm, new { DeviceId = "MTR-1" }));
    }

    [Fact]
    public async Task PlaceholderDrift_Skipped()
    {
        store.Set("fa", LookupTexts.DeleteConfirm.Key.Value, "حذف {Device}؟", LookupTexts.DeleteConfirm.Default);
        await Cache.EnsureLoadedAsync("fa", CancellationToken.None);

        using CultureScope culture = new("fa");

        Assert.Equal("Delete MTR-1?", Localizer.Text(LookupTexts.DeleteConfirm, new { DeviceId = "MTR-1" }));
    }

    [Fact]
    public void UnknownKey_ReturnsKeyNotFound()
    {
        LocalizedString text = Localizer["nope.key"];

        Assert.True(text.ResourceNotFound);
        Assert.Equal("nope.key", text.Value);
    }

    [Fact]
    public void Arguments_Formatted()
    {
        using CultureScope culture = new("en");

        Assert.Equal("Delete device MTR-1?", Localizer.Text(LookupTexts.DeleteDevice, new { DeviceId = "MTR-1" }));
        Assert.Equal(
            "Delete device MTR-2?",
            Localizer.Text(LookupTexts.DeleteDevice, new Dictionary<string, object?> { ["DeviceId"] = "MTR-2" }));
    }

    [Fact]
    public async Task Refresh_ShowsNewTextOnNextLookup()
    {
        store.Set("de", CommonTexts.Save.Key.Value, "Speichern", CommonTexts.Save.Default);
        await Cache.EnsureLoadedAsync("de", CancellationToken.None);

        store.Set("de", CommonTexts.Save.Key.Value, "Sichern", CommonTexts.Save.Default);
        await Cache.RefreshAsync("de", CancellationToken.None);

        using CultureScope culture = new("de");

        Assert.Equal("Sichern", Localizer.Text(CommonTexts.Save));
    }

    [Fact]
    public void TypedLocalizer_AnswersTheSameStore()
    {
        using CultureScope culture = new("en");

        IStringLocalizer<LookupTests> typed = lookupFactory.Services.GetRequiredService<IStringLocalizer<LookupTests>>();

        Assert.Equal("Save", typed.Text(CommonTexts.Save));
    }

    private IStringLocalizer Localizer => lookupFactory.Services.GetRequiredService<IStringLocalizer>();

    private ITranslationCache Cache => lookupFactory.Services.GetRequiredService<ITranslationCache>();

    public static class LookupTexts
    {
        private static readonly TextCategory category = TextCategory.Create("DeviceManagement", "LookupTests");

        public static TextDefinition DeleteConfirm { get; } = category.Text("DeleteConfirm", "Delete {DeviceId}?");

        public static TextDefinition DeleteDevice { get; } = category.Text("DeleteDevice", "Delete device {DeviceId}?");
    }

    private sealed class FakeTranslationStore : ITranslationStore
    {
        private readonly Dictionary<string, Dictionary<string, StoredTranslation>> translations = new(StringComparer.Ordinal);

        public void Set(string culture, string key, string text, string sourceText)
        {
            if (!translations.TryGetValue(culture, out Dictionary<string, StoredTranslation>? byKey))
            {
                byKey = new Dictionary<string, StoredTranslation>(StringComparer.Ordinal);
                translations[culture] = byKey;
            }

            byKey[key] = new StoredTranslation(text, sourceText);
        }

        public Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LanguageInfo>>(
                [.. translations.Keys.Select(culture => new LanguageInfo(culture, culture, IsRightToLeft: false, IsPublished: true))]);

        public Task<IReadOnlyDictionary<string, StoredTranslation>> GetTranslationsAsync(string culture, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, StoredTranslation>>(
                translations.TryGetValue(culture, out Dictionary<string, StoredTranslation>? byKey)
                    ? new Dictionary<string, StoredTranslation>(byKey, StringComparer.Ordinal)
                    : new Dictionary<string, StoredTranslation>(StringComparer.Ordinal));
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentUICulture;

        public CultureScope(string culture) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        public void Dispose() => CultureInfo.CurrentUICulture = previous;
    }
}
