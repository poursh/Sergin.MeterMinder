using System.Globalization;
using Sergin.SharedKernel.Application.Localizations;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>An in-memory translation store a test fills before loading a culture into the cache.</summary>
internal sealed class TestTranslationStore : ITranslationStore
{
    private readonly Dictionary<string, Dictionary<string, StoredTranslation>> translations = new(StringComparer.Ordinal);

    private readonly Dictionary<string, bool> languages = new(StringComparer.Ordinal);

    /// <summary>Declares a language without translations; a culture only <see cref="Set"/> names is published.</summary>
    public void AddLanguage(string culture, bool published) => languages[culture] = published;

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
            [.. translations.Keys.Union(languages.Keys, StringComparer.Ordinal).Select(culture => new LanguageInfo(
                culture, culture, IsRightToLeft: false, IsPublished: !languages.TryGetValue(culture, out bool published) || published))]);

    public Task<IReadOnlyDictionary<string, StoredTranslation>> GetTranslationsAsync(string culture, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, StoredTranslation>>(
            translations.TryGetValue(culture, out Dictionary<string, StoredTranslation>? byKey)
                ? new Dictionary<string, StoredTranslation>(byKey, StringComparer.Ordinal)
                : new Dictionary<string, StoredTranslation>(StringComparer.Ordinal));
}

/// <summary>Sets the current UI culture for the test's flow and restores it on dispose.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo previous = CultureInfo.CurrentUICulture;

    public CultureScope(string culture) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

    public void Dispose() => CultureInfo.CurrentUICulture = previous;
}
