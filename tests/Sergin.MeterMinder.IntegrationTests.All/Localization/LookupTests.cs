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
            services.AddSingleton(TextSource.FromTypes("DeviceManagement", TextOrigin.Application, typeof(LookupTexts)));
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
        await Cache.EnsureLoadedAsync("fa-IR", CancellationToken.None);

        using CultureScope culture = new("fa-IR");

        Assert.Equal("ذخیره", Localizer.Text(CommonTexts.Save));
    }

    // A draft parent's text is unreviewed: a user of its published child culture must not see it.
    [Fact]
    public async Task DraftParentCulture_Skipped()
    {
        store.Set("fa-IR", LookupTexts.DeleteDevice.Key.Value, "حذف دستگاه {DeviceId}؟", LookupTexts.DeleteDevice.Default);
        store.Set("fa", CommonTexts.Save.Key.Value, "ذخیره", CommonTexts.Save.Default);
        store.MarkDraft("fa");
        await Cache.EnsureLoadedAsync("fa-IR", CancellationToken.None);

        using CultureScope culture = new("fa-IR");

        Assert.Equal("Save", Localizer.Text(CommonTexts.Save));
    }

    [Fact]
    public async Task PublishedParentCulture_Shown()
    {
        store.Set("fa-IR", LookupTexts.DeleteDevice.Key.Value, "حذف دستگاه {DeviceId}؟", LookupTexts.DeleteDevice.Default);
        store.Set("fa", CommonTexts.Save.Key.Value, "ذخیره", CommonTexts.Save.Default);
        await Cache.EnsureLoadedAsync("fa-IR", CancellationToken.None);

        using CultureScope culture = new("fa-IR");

        Assert.Equal("ذخیره", Localizer.Text(CommonTexts.Save));
    }

    // The requested culture already passed the culture provider's gate, so a translator previewing a draft sees it.
    [Fact]
    public async Task DraftRequestedCulture_StillShown()
    {
        store.Set("fa", CommonTexts.Save.Key.Value, "ذخیره", CommonTexts.Save.Default);
        store.MarkDraft("fa");
        await Cache.EnsureLoadedAsync("fa", CancellationToken.None);

        using CultureScope culture = new("fa");

        Assert.Equal("ذخیره", Localizer.Text(CommonTexts.Save));
    }

    [Fact]
    public async Task ParentCulture_UsedWhenTheCultureTranslationDrifted()
    {
        store.Set("fa-IR", LookupTexts.DeleteConfirm.Key.Value, "حذف {Device}؟", LookupTexts.DeleteConfirm.Default);
        store.Set("fa", LookupTexts.DeleteConfirm.Key.Value, "حذف {DeviceId}؟", LookupTexts.DeleteConfirm.Default);
        await Cache.EnsureLoadedAsync("fa-IR", CancellationToken.None);

        using CultureScope culture = new("fa-IR");

        Assert.Equal("حذف MTR-1؟", Localizer.Text(LookupTexts.DeleteConfirm, new { DeviceId = "MTR-1" }));
    }

    [Fact]
    public async Task SlowEarlierLoad_DoesNotOverwriteALaterRefresh()
    {
        store.Set("de", CommonTexts.Save.Key.Value, "Alt", CommonTexts.Save.Default);
        TaskCompletionSource release = store.HoldNextTranslationRead();

        Task ensure = Cache.EnsureLoadedAsync("de", CancellationToken.None);
        await store.HeldReadStarted;

        store.Set("de", CommonTexts.Save.Key.Value, "Neu", CommonTexts.Save.Default);
        Task refresh = Cache.RefreshAsync("de", CancellationToken.None);

        release.SetResult();
        await Task.WhenAll(ensure, refresh);

        using CultureScope culture = new("de");

        Assert.Equal("Neu", Localizer.Text(CommonTexts.Save));
    }

    // A language write refreshes the list after its save; a slower read started earlier must not land after it.
    [Fact]
    public async Task SlowEarlierLanguagesRead_DoesNotOverwriteALaterRefresh()
    {
        store.Set("de", CommonTexts.Save.Key.Value, "Speichern", CommonTexts.Save.Default);
        TaskCompletionSource release = store.HoldNextLanguagesRead();

        Task earlier = Cache.RefreshLanguagesAsync(CancellationToken.None);
        await store.HeldLanguagesReadStarted;

        store.Set("fr", CommonTexts.Save.Key.Value, "Enregistrer", CommonTexts.Save.Default);
        Task later = Cache.RefreshLanguagesAsync(CancellationToken.None);

        release.SetResult();
        await Task.WhenAll(earlier, later);

        Assert.Contains(Cache.Languages, language => language.Culture == "fr");
    }

    [Fact]
    public async Task InvalidateLanguages_ReloadsOnTheNextLoad()
    {
        store.Set("de", CommonTexts.Save.Key.Value, "Speichern", CommonTexts.Save.Default);
        await Cache.EnsureLoadedAsync("de", CancellationToken.None);
        store.Set("fr", CommonTexts.Save.Key.Value, "Enregistrer", CommonTexts.Save.Default);

        Cache.InvalidateLanguages();
        Assert.Empty(Cache.Languages);

        await Cache.EnsureLoadedAsync("de", CancellationToken.None);
        Assert.Contains(Cache.Languages, language => language.Culture == "fr");
    }

    [Fact]
    public async Task Invalidate_ReloadsTheCultureOnTheNextLoad()
    {
        store.Set("de", CommonTexts.Save.Key.Value, "Speichern", CommonTexts.Save.Default);
        await Cache.EnsureLoadedAsync("de", CancellationToken.None);
        store.Set("de", CommonTexts.Save.Key.Value, "Sichern", CommonTexts.Save.Default);

        Cache.Invalidate("de");
        Assert.False(Cache.TryGet("de", CommonTexts.Save.Key.Value, out _));

        await Cache.EnsureLoadedAsync("de", CancellationToken.None);
        Assert.True(Cache.TryGet("de", CommonTexts.Save.Key.Value, out StoredTranslation? reloaded));
        Assert.Equal("Sichern", reloaded.Text);
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
        private readonly HashSet<string> drafts = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource heldReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource heldLanguagesReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource? hold;
        private TaskCompletionSource? languagesHold;

        /// <summary>Completes once the held language read has taken its snapshot of the data.</summary>
        public Task HeldLanguagesReadStarted => heldLanguagesReadStarted.Task;

        /// <summary>The next language read snapshots the data now and returns it only once the result is released.</summary>
        public TaskCompletionSource HoldNextLanguagesRead()
        {
            languagesHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return languagesHold;
        }

        /// <summary>Completes once the held read has taken its snapshot of the data.</summary>
        public Task HeldReadStarted => heldReadStarted.Task;

        /// <summary>The next translation read snapshots the data now and returns it only once the result is released.</summary>
        public TaskCompletionSource HoldNextTranslationRead()
        {
            hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return hold;
        }

        /// <summary>The language list answers <paramref name="culture"/> as a draft rather than published.</summary>
        public void MarkDraft(string culture) => drafts.Add(culture);

        public void Set(string culture, string key, string text, string sourceText)
        {
            if (!translations.TryGetValue(culture, out Dictionary<string, StoredTranslation>? byKey))
            {
                byKey = new Dictionary<string, StoredTranslation>(StringComparer.Ordinal);
                translations[culture] = byKey;
            }

            byKey[key] = new StoredTranslation(text, sourceText);
        }

        public async Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<LanguageInfo> snapshot =
                [.. translations.Keys.Select(culture => new LanguageInfo(culture, culture, IsRightToLeft: false, IsPublished: !drafts.Contains(culture)))];

            if (languagesHold is { } held)
            {
                languagesHold = null;
                heldLanguagesReadStarted.SetResult();
                await held.Task;
            }

            return snapshot;
        }

        public async Task<IReadOnlyDictionary<string, StoredTranslation>> GetTranslationsAsync(string culture, CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, StoredTranslation> snapshot =
                translations.TryGetValue(culture, out Dictionary<string, StoredTranslation>? byKey)
                    ? new Dictionary<string, StoredTranslation>(byKey, StringComparer.Ordinal)
                    : new Dictionary<string, StoredTranslation>(StringComparer.Ordinal);

            if (hold is { } held)
            {
                hold = null;
                heldReadStarted.SetResult();
                await held.Task;
            }

            return snapshot;
        }
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentUICulture;

        public CultureScope(string culture) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        public void Dispose() => CultureInfo.CurrentUICulture = previous;
    }
}
